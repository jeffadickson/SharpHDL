using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
namespace SharpHDL
{
    public static partial class CilCompiler
    {
        static bool IsChannel(Type t) { return t!=null&&t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(HwStream<>); }
        static bool ProcessScalar(Type t) { return t==typeof(bool)||t==typeof(byte)||t==typeof(sbyte)||t==typeof(short)||t==typeof(ushort)||t==typeof(int)||t==typeof(uint)||PackedLayout.IsBits(t); }
        static bool ProcessSigned(Type t) { return t==typeof(int)||t==typeof(short)||t==typeof(sbyte); }
        /// <summary>Attach an independent, free-running instruction-per-clock CIL actor. Reset restarts it.</summary>
        public static ProcessHandle AttachProcess(Module m,string name,MethodInfo method,params HwChannel[] channels)
        {
            var body=method.GetMethodBody();var ps=method.GetParameters();
            if(!method.IsStatic||method.ContainsGenericParameters||method.ReturnType!=typeof(void)||body==null||body.ExceptionHandlingClauses.Count!=0)
                throw Error(method,0,"Processes require a static void CIL body without exception handlers.");
            if(ps.Length!=channels.Length||ps.Any(p=>!IsChannel(p.ParameterType))||channels.Any(c=>c==null)||channels.Distinct().Count()!=channels.Length)
                throw Error(method,0,"Bind distinct typed channels, one per parameter.");
            for(int i=0;i<ps.Length;i++)if(ps[i].ParameterType!=channels[i].GetType()||channels[i].Data.Owner!=m||!ProcessScalar(channels[i].TokenType))
                throw Error(method,0,"Process channel bindings must belong to this module and match scalar token types (1..32 bits).");
            if(body.LocalVariables.Any(v=>!ProcessScalar(v.LocalType))||(!body.InitLocals&&body.LocalVariables.Count>0))
                throw Error(method,0,"Process locals must be initialized scalar values; channel aliases and record locals are unsupported.");
            var list=Decode(method);if(list.Count>512)throw Error(method,0,"Process exceeds 512 CIL instructions.");var code=list.ToDictionary(i=>i.Offset);
            // Track handle provenance separately from numeric hardware stack values. Never infer a channel from runtime data.
            var tags=new Dictionary<int,int[]>();var todo=new Queue<Tuple<int,int[]>>();todo.Enqueue(Tuple.Create(0,new int[0]));
            var roles=new int[channels.Length];int maxStack=0;
            while(todo.Count>0)
            {
                var item=todo.Dequeue();int[] old;
                if(tags.TryGetValue(item.Item1,out old)){if(!old.SequenceEqual(item.Item2))throw Error(method,item.Item1,"Inconsistent stack or channel provenance at branch merge.");continue;}
                Instruction ins;if(!code.TryGetValue(item.Item1,out ins))throw Error(method,item.Item1,"Invalid branch target.");
                string op=ins.Name;var before=item.Item2;tags[ins.Offset]=before;int h=before.Length;
                bool call=op=="call"||op=="callvirt";var callee=call?ins.Operand as MethodInfo:null;
                if(call&&callee==null)throw Error(method,ins.Offset,"Unsupported call target.");
                int pops=op=="ret"?0:call?callee.GetParameters().Length+(callee.IsStatic?0:1):PopCount(ins);
                int pushes=call?(callee.ReturnType==typeof(void)?0:1):PushCount(ins);
                if(h<pops||h-pops+pushes>32)throw Error(method,ins.Offset,"Invalid or excessive process stack.");
                int tag=-1;
                if(op.StartsWith("ldarg")){int arg=VarIndex(ins,"ldarg");if(arg<0||arg>=channels.Length)throw Error(method,ins.Offset,"Invalid channel argument.");tag=arg;}
                if(call&&IsChannel(callee.DeclaringType))
                {
                    bool read=callee.Name=="Read"&&callee.GetParameters().Length==0;
                    bool write=callee.Name=="Write"&&callee.GetParameters().Length==1;
                    if(callee.IsStatic||(!read&&!write)||before[h-pops]<0)throw Error(method,ins.Offset,"Only direct channel Read/Write calls are supported.");
                    int arg=before[h-pops];if(ps[arg].ParameterType!=callee.DeclaringType||before.Skip(h-pops+1).Any(t=>t>=0))throw Error(method,ins.Offset,"Invalid channel receiver or token.");
                    roles[arg]|=read?1:2;if(roles[arg]==3)throw Error(method,ins.Offset,"A process cannot both read and write the same channel.");
                }
                else if(op=="dup")tag=before[h-1];
                else if(before.Skip(h-pops).Any(t=>t>=0)&&op!="pop")throw Error(method,ins.Offset,"Channel handles cannot be stored, compared or used as numeric values.");
                var after=before.Take(h-pops).Concat(Enumerable.Repeat(tag,pushes)).ToArray();
                if(op=="ret"&&after.Length!=0)throw Error(method,ins.Offset,"Nonempty return stack.");
                maxStack=Math.Max(maxStack,Math.Max(h,after.Length));foreach(int target in Successors(ins))todo.Enqueue(Tuple.Create(target,after));
            }
            if(roles.Any(r=>r==0))throw Error(method,0,"Every bound channel must be read or written.");
            var reachable=list.Where(i=>tags.ContainsKey(i.Offset)).ToArray();var ids=reachable.Select((i,n)=>new{i.Offset,n}).ToDictionary(x=>x.Offset,x=>x.n);
            int pw=1;while((1<<pw)<reachable.Length)pw++;
            var pc=m.Register(name+"_pc",pw,0);var halted=m.Register(name+"_halted",1,0);var fault=m.Register(name+"_fault",1,0);var assertion=m.Register(name+"_assertion_failed",1,0);
            pc.Describe(name+": one CIL instruction per clock, blocked Read/Write holds this state. "+string.Join("; ",reachable.Select(i=>ids[i.Offset]+"=IL_"+i.Offset.ToString("x4")+" "+i.Name)));
            halted.Describe(name+": return or fault stops this actor until synchronous reset.");fault.Describe(name+": sticky arithmetic/assertion fault; previously transferred tokens remain committed.");
            var locals=body.LocalVariables.Select((v,i)=>m.Register(name+"_local"+i,32,0)).ToArray();
            var stack=Enumerable.Range(0,maxStack).Select(i=>m.Register(name+"_stack"+i,32,0)).ToArray();
            for(int i=0;i<locals.Length;i++)locals[i].Describe(name+": CIL local "+i+" ("+body.LocalVariables[i].LocalType.Name+"); 32-bit evaluation representation, reset zero.");
            for(int i=0;i<stack.Length;i++)stack[i].Describe(name+": CIL evaluation-stack slot "+i+"; saved operands hold while a channel call is blocked.");
            var registers=new List<Signal>{pc,halted,fault,assertion};registers.AddRange(locals);registers.AddRange(stack);
            var next=registers.ToDictionary(r=>r,r=>(Expr)r);Action<Signal,Expr,Expr> set=(r,v,c)=>next[r]=Select(c,Resize(v,r.Width),next[r]);
            var active=~halted;Expr waitingInput=K(1,0),waitingOutput=K(1,0);
            var readiness=channels.Select(c=>K(1,0)).ToArray();var validity=channels.Select(c=>K(1,0)).ToArray();var payload=channels.Select(c=>K(c.Data.Width,0)).ToArray();
            Action<Expr,Expr,bool> stop=(condition,failed,isAssertion)=>{set(halted,K(1,1),condition);set(fault,failed,condition);if(isAssertion)set(assertion,failed,condition);};
            foreach(var ins in reachable)
            {
                int h=tags[ins.Offset].Length;string op=ins.Name;var condition=active & pc.Eq(K(pw,ids[ins.Offset]));
                Func<int,Expr> top=n=>stack[h-1-n];Action<int,Expr> push=(slot,value)=>set(stack[slot],value,condition);
                if(op!="ret"&&op!="br"&&op!="br.s")set(pc,K(pw,ids[ins.Next]),condition);
                if(op=="nop")continue;
                if(op.StartsWith("ldc.i4")){push(h,K(32,op=="ldc.i4.m1"?-1:op=="ldc.i4"||op=="ldc.i4.s"?(int)ins.Operand:int.Parse(op.Substring(7))));continue;}
                if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")){push(h,K(32,VarIndex(ins,"ldarg")));continue;}
                if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")){push(h,locals[VarIndex(ins,"ldloc")]);continue;}
                if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc.")){set(locals[VarIndex(ins,"stloc")],top(0),condition);continue;}
                if(op=="dup"){push(h,top(0));continue;}if(op=="pop")continue;
                if(op=="ret"){stop(condition,K(1,0),false);continue;}
                if(op=="not"||op=="neg"){push(h-1,Unary(op,top(0)));continue;}
                if(op=="conv.i4"||op=="conv.u4")continue;
                if(op=="conv.i1"||op=="conv.u1"||op=="conv.i2"||op=="conv.u2"){push(h-1,Resize(Resize(top(0),op.EndsWith("1")?8:16),32,op[5]=='i'));continue;}
                if(op=="ceq"||op=="clt"||op=="clt.un"||op=="cgt"||op=="cgt.un"){push(h-2,Resize(Test(op.Substring(1,2),top(1),top(0),op.EndsWith(".un")),32));continue;}
                if(op=="shl"||op=="shr"||op=="shr.un"){push(h-2,Shift(top(1),top(0),op));continue;}
                if(op=="and"||op=="or"||op=="xor"||op.StartsWith("add")||op.StartsWith("sub")||op.StartsWith("mul"))
                {string basic=op.Split('.')[0];var a=top(1);var b=top(0);var v=Bin(basic,a,b).Named(name+"_"+method.Name+"_IL_"+ins.Offset.ToString("x4")+"_"+op).Describe(name+": "+method.Name+" IL_"+ins.Offset.ToString("x4")+" "+op+"; 32-bit CIL arithmetic.");push(h-2,v);if(op.Contains("ovf")){bool signed=!op.EndsWith(".un");var fits=Test("eq",Bin(basic,Resize(a,64,signed),Resize(b,64,signed)),Resize(v,64,signed));stop(condition & Not(fits),K(1,1),false);}continue;}
                if(op=="div"||op=="div.un"||op=="rem"||op=="rem.un")
                {var d=IntegerDivide.Evaluate(top(1),top(0),DivisionRounding.TowardZero,!op.EndsWith(".un"));push(h-2,op.StartsWith("div")?d.Quotient:Resize(d.Remainder,32));stop(condition & (d.DivideByZero|d.Overflowed),K(1,1),false);continue;}
                if(op=="call"||op=="callvirt")
                {
                    var callee=(MethodInfo)ins.Operand;int n=callee.GetParameters().Length;
                    if(IsChannel(callee.DeclaringType))
                    {
                        int arg=tags[ins.Offset][h-n-1];var channel=channels[arg];bool read=callee.Name=="Read";
                        Expr fire=read?channel.Valid:channel.Ready;
                        set(pc,pc,condition & ~fire);
                        if(read)
                        {
                            readiness[arg]=readiness[arg]|condition;
                            set(stack[h-1],Resize(channel.Data,32,ProcessSigned(channel.TokenType)),condition & fire);
                            waitingInput=waitingInput|(condition & ~fire);
                        }
                        else
                        {
                            validity[arg]=validity[arg]|condition;
                            payload[arg]=Select(condition,Resize(top(0),channel.Data.Width),payload[arg]);
                            waitingOutput=waitingOutput|(condition & ~fire);
                        }
                        continue;
                    }
                    if(callee.DeclaringType==typeof(Hardware)&&callee.Name=="Assert"){stop(condition & Test("eq",top(0),K(32,0)),K(1,1),true);continue;}
                    if(PackedLayout.IsBits(callee.DeclaringType)){push(h-n,BitsCall(callee,Enumerable.Range(h-n,n).Select(i=>(Expr)stack[i]).ToArray()));continue;}
                    throw Error(method,ins.Offset,"Process calls support stream Read/Write, Hardware.Assert and UBits intrinsics only.");
                }
                if(op=="br"||op=="br.s"){set(pc,K(pw,ids[(int)ins.Operand]),condition);continue;}
                if(op=="switch")
                {Expr target=K(pw,ids[ins.Next]);int[] targets=(int[])ins.Operand;for(int i=targets.Length-1;i>=0;i--)target=Select(top(0).Eq(K(32,i)),K(pw,ids[targets[i]]),target);set(pc,target,condition);continue;}
                if(Conditional(op))
                {
                    Expr c;if(op.StartsWith("brtrue")||op.StartsWith("brfalse")){c=Test("eq",top(0),K(32,0));if(op.StartsWith("brtrue"))c=Not(c);}
                    else{string test=op.Substring(0,3);c=Test(test=="beq"||test=="bne"?"eq":test=="blt"||test=="bge"?"lt":"gt",top(1),top(0),op.Contains(".un"));if(test=="bne"||test=="ble"||test=="bge")c=Not(c);}
                    set(pc,Select(c,K(pw,ids[(int)ins.Operand]),K(pw,ids[ins.Next])),condition);continue;
                }
                throw Error(method,ins.Offset,"Unsupported sequential opcode "+op);
            }
            foreach(var pair in next)m.Next(pair.Key,pair.Value);
            for(int i=0;i<channels.Length;i++)if(roles[i]==1)m.Drive(channels[i].Ready,readiness[i]);else{m.Drive(channels[i].Valid,validity[i]);m.Drive(channels[i].Data,payload[i]);}
            var wi=m.Wire(name+"_waiting_input",1);var wo=m.Wire(name+"_waiting_output",1);m.Drive(wi,waitingInput);m.Drive(wo,waitingOutput);
            wi.Describe(name+": Read is blocked waiting for a valid token.");wo.Describe(name+": Write is blocked waiting for ready; offered token remains stable.");
            return new ProcessHandle{ProgramCounter=pc,Halted=halted,Fault=fault,AssertionFailed=assertion,WaitingForInput=wi,WaitingForOutput=wo};
        }
    }
}
