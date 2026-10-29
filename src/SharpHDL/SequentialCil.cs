using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
namespace SharpHDL
{
    public sealed class SequentialOptions
    {
        public int MaxCycles=4096;
        public int MemoryDepth=16;
        public MemoryOptions MemoryOptions;
        public BigInteger[] InitialMemory;
        /// <summary>Optional depths keyed by the original uint[] parameter index. Other RAMs use MemoryDepth.</summary>
        public IReadOnlyDictionary<int,int> MemoryDepths;
        /// <summary>Optional initial words keyed by uint[] parameter index; incompatible with legacy InitialMemory.</summary>
        public IReadOnlyDictionary<int,BigInteger[]> InitialMemories;
        /// <summary>Explicit alias-parameter to canonical-parameter bindings for scheduled RAMs. Chains resolve; cycles reject.</summary>
        public IReadOnlyDictionary<int,int> RamAliases;
        public int MaxInstructions=512;
    }
    public static partial class CilCompiler
    {
        static int VarIndex(Instruction ins,string prefix){return ins.Name==prefix||ins.Name==prefix+".s"?(int)ins.Operand:int.Parse(ins.Name.Substring(prefix.Length+1));}
        static bool Conditional(string op){return op.StartsWith("brtrue")||op.StartsWith("brfalse")||op.StartsWith("beq")||op.StartsWith("bne")||op.StartsWith("blt")||op.StartsWith("bgt")||op.StartsWith("ble")||op.StartsWith("bge");}
        static int PopCount(Instruction i)
        {
            if(i.Name=="ret")return 1;
            if(i.Name=="call")return ((MethodInfo)i.Operand).GetParameters().Length;
            var c=Codes.Values.First(x=>x.Name==i.Name);string s=c.StackBehaviourPop.ToString();
            if(s=="Pop0")return 0;if(s=="Varpop")throw new NotSupportedException("Unsupported variable stack instruction "+i.Name);return s.Split('_').Length;
        }
        static int PushCount(Instruction i)
        {
            if(i.Name=="call")return ((MethodInfo)i.Operand).ReturnType==typeof(void)?0:1;
            var c=Codes.Values.First(x=>x.Name==i.Name);string s=c.StackBehaviourPush.ToString();
            if(s=="Push0")return 0;if(s=="Varpush")throw new NotSupportedException("Unsupported variable stack instruction "+i.Name);return s.Split('_').Length;
        }
        static IEnumerable<int> Successors(Instruction i)
        {
            if(i.Name=="ret")yield break;
            if(i.Name=="br"||i.Name=="br.s"){yield return (int)i.Operand;yield break;}
            if(Conditional(i.Name))yield return (int)i.Operand;
            if(i.Name=="switch")foreach(int target in (int[])i.Operand)yield return target;
            yield return i.Next;
        }
        // Conservative instruction-per-cycle FSM; synchronous loads consume an extra wait state.
        public static Module BuildSequential(MethodInfo method,string name,SequentialOptions options=null)
        {
            var o=options??new SequentialOptions();var ps=method.GetParameters();var body=method.GetMethodBody();
            if(o.MemoryDepths?.Count>0||o.InitialMemories?.Count>0||o.RamAliases?.Count>0)throw Error(method,0,"Per-RAM binding options require the scheduled backend.");
            if(o.MaxCycles<1||o.MaxCycles>1000000||o.MaxInstructions<1||o.MaxInstructions>4096)throw new ArgumentException("Invalid sequential limits.");
            if(!method.IsStatic||method.ContainsGenericParameters||body==null||body.ExceptionHandlingClauses.Count!=0||!Supported(method.ReturnType)||PackedLayout.IsRecord(method.ReturnType))throw Error(method,0,"Sequential methods require a scalar result, a static CIL body and no exception handlers.");
            if(body.LocalVariables.Any(v=>!Supported(v.LocalType)||PackedLayout.IsRecord(v.LocalType))||!body.InitLocals&&body.LocalVariables.Count>0)throw Error(method,0,"Sequential locals must be initialized scalar values.");
            if(ps.Any(p=>p.ParameterType!=typeof(uint[])&&(!Supported(p.ParameterType)||PackedLayout.IsRecord(p.ParameterType)))||ps.Count(p=>p.ParameterType==typeof(uint[]))>1)throw Error(method,0,"Sequential parameters are scalars and at most one uint[] RAM.");
            bool hasRam=ps.Any(p=>p.ParameterType==typeof(uint[]));if(hasRam&&(o.MemoryDepth<1||o.MemoryDepth>65536))throw new ArgumentException("RAM depth must be 1..65536.");
            var list=Decode(method);if(list.Count>o.MaxInstructions)throw Error(method,0,"Sequential instruction limit.");var code=list.ToDictionary(i=>i.Offset);
            var height=new Dictionary<int,int>();var todo=new Queue<Tuple<int,int>>();todo.Enqueue(Tuple.Create(0,0));int maxStack=0;
            while(todo.Count>0)
            {
                var t=todo.Dequeue();int existing;
                if(height.TryGetValue(t.Item1,out existing)){if(existing!=t.Item2)throw Error(method,t.Item1,"Inconsistent stack height.");continue;}
                Instruction i;if(!code.TryGetValue(t.Item1,out i))throw Error(method,t.Item1,"Invalid branch target.");height[t.Item1]=t.Item2;
                int pops=PopCount(i),after=t.Item2-pops+PushCount(i);if(t.Item2<pops||after>32)throw Error(method,i.Offset,"Invalid or excessive stack depth.");
                maxStack=Math.Max(maxStack,Math.Max(t.Item2,after));if(i.Name=="ret"&&after!=0)throw Error(method,i.Offset,"Nonempty return stack.");
                foreach(int target in Successors(i))todo.Enqueue(Tuple.Create(target,after));
            }
            var reachable=list.Where(i=>height.ContainsKey(i.Offset)).ToArray();var ids=reachable.Select((i,n)=>new{i.Offset,n}).ToDictionary(x=>x.Offset,x=>x.n);
            int states=reachable.Length;var waits=new Dictionary<int,int>();foreach(var i in reachable.Where(i=>i.Name=="ldelem.u4"||i.Name=="ldelem.i4"))waits[i.Offset]=states++;
            int pw=1;while((1<<pw)<states)pw++;int cw=1;while((1<<cw)<=o.MaxCycles)cw++;
            var m=new Module(name,o.MemoryOptions);var start=m.Input("start",1);var busy=m.Register("busy_state",1,0);var done=m.Register("done_state",1,0);var fault=m.Register("fault_state",1,0);var assertion=m.Register("assertion_state",1,0);var timeout=m.Register("timeout_state",1,0);
            start.Describe("Request: accepted at rising edge only while busy=0; deassert after acceptance to prevent retriggering.");
            var pc=m.Register("pc",pw,0);var cycles=m.Register("cycles",cw,0);var result=m.Register("result_state",PortWidth(method.ReturnParameter),0);
            pc.Describe("Sequential CIL program counter; one operation per active edge, RAM load adds one wait edge. "+string.Join("; ",reachable.Select(i=>ids[i.Offset]+"=IL_"+i.Offset.ToString("x4")+" "+i.Name)));
            cycles.Describe("Execution edges elapsed; maximum "+o.MaxCycles+"; timeout aborts on following edge. Not source loop iterations.");
            var args=new Expr[ps.Length];var captured=new List<Tuple<Signal,Expr>>();
            for(int i=0;i<ps.Length;i++)
            {
                if(ps[i].ParameterType==typeof(uint[])){args[i]=K(32,0);continue;}
                var input=m.Input("arg"+i,PortWidth(ps[i]));input.Describe(ps[i].Name+": latched only when start=1 and busy=0.");var r=m.Register("saved_arg"+i,32,0);args[i]=r;captured.Add(Tuple.Create(r,Resize(input,32,Signed(ps[i].ParameterType))));
            }
            var locals=body.LocalVariables.Select((v,i)=>m.Register("local"+i,32,0)).ToArray();var stack=Enumerable.Range(0,maxStack).Select(i=>m.Register("stack"+i,32,0)).ToArray();
            var registers=new List<Signal>{busy,done,fault,assertion,timeout,pc,cycles,result};registers.AddRange(captured.Select(x=>x.Item1));registers.AddRange(locals);registers.AddRange(stack);
            var next=registers.ToDictionary(r=>r,r=>(Expr)r);
            Action<Signal,Expr,Expr> set=(r,v,c)=>next[r]=Select(c,Resize(v,r.Width),next[r]);
            var accept=start & ~busy;var expired=~cycles.Lt(K(cw,o.MaxCycles));var active=busy & ~expired;
            Expr ramAddress=K(32,0),ramData=K(32,0),ramWrite=K(1,0),ramRead=K(1,0);Signal ramAddr=null,ramWd=null,ramWe=null,ramRe=null;SynchronousRam ram=null;
            if(hasRam){ramAddr=m.Wire("ram_address",32);ramWd=m.Wire("ram_write_data",32);ramWe=m.Wire("ram_write_enable",1);ramRe=m.Wire("ram_read_enable",1);ram=SynchronousRam.Create(m,"data",32,o.MemoryDepth,ramAddr,ramWe,ramWd,ramRe,o.InitialMemory);}
            Action<Expr,Expr,bool> stop=(condition,failed,isAssertion)=>{set(busy,K(1,0),condition);set(done,K(1,1),condition);set(fault,failed,condition);if(isAssertion)set(assertion,failed,condition);};
            foreach(var ins in reachable)
            {
                int h=height[ins.Offset];string op=ins.Name;var condition=active & pc.Eq(K(pw,ids[ins.Offset]));
                Func<int,Expr> top=n=>stack[h-1-n];Action<int,Expr> push=(slot,value)=>set(stack[slot],value,condition);
                if(op!="ret"&&op!="br"&&op!="br.s")set(pc,K(pw,ids[ins.Next]),condition);
                if(op=="nop")continue;
                if(op.StartsWith("ldc.i4")){push(h,K(32,op=="ldc.i4.m1"?-1:op=="ldc.i4"||op=="ldc.i4.s"?(int)ins.Operand:int.Parse(op.Substring(7))));continue;}
                if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")){push(h,args[VarIndex(ins,"ldarg")]);continue;}
                if(op=="starg"||op=="starg.s"){var r=args[(int)ins.Operand] as Signal;if(r==null)throw Error(method,ins.Offset,"Cannot reassign RAM reference.");set(r,top(0),condition);continue;}
                if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")){push(h,locals[VarIndex(ins,"ldloc")]);continue;}
                if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc.")){set(locals[VarIndex(ins,"stloc")],top(0),condition);continue;}
                if(op=="dup"){push(h,top(0));continue;}if(op=="pop")continue;
                if(op=="ret"){set(result,top(0),condition);stop(condition,K(1,0),false);continue;}
                if(op=="not"||op=="neg"){push(h-1,Unary(op,top(0)));continue;}
                if(op=="conv.i4"||op=="conv.u4")continue;
                if(op=="conv.i1"||op=="conv.u1"||op=="conv.i2"||op=="conv.u2"){push(h-1,Resize(Resize(top(0),op.EndsWith("1")?8:16),32,op[5]=='i'));continue;}
                if(op=="ceq"||op=="clt"||op=="clt.un"||op=="cgt"||op=="cgt.un"){push(h-2,Resize(Test(op.Substring(1,2),top(1),top(0),op.EndsWith(".un")),32));continue;}
                if(op=="shl"||op=="shr"||op=="shr.un"){push(h-2,Shift(top(1),top(0),op));continue;}
                if(op=="and"||op=="or"||op=="xor"||op.StartsWith("add")||op.StartsWith("sub")||op.StartsWith("mul"))
                {string basic=op.Split('.')[0];var a=top(1);var b=top(0);var v=Bin(basic,a,b);push(h-2,v);if(op.Contains("ovf")){bool signed=!op.EndsWith(".un");var fits=Test("eq",Bin(basic,Resize(a,64,signed),Resize(b,64,signed)),Resize(v,64,signed));stop(condition & Not(fits),K(1,1),false);}continue;}
                if(op=="div"||op=="div.un"||op=="rem"||op=="rem.un")
                {var d=IntegerDivide.Evaluate(top(1),top(0),DivisionRounding.TowardZero,!op.EndsWith(".un"));push(h-2,op.StartsWith("div")?d.Quotient:Resize(d.Remainder,32));stop(condition & (d.DivideByZero|d.Overflowed),K(1,1),false);continue;}
                if(op=="call")
                {
                    var callee=ins.Operand as MethodInfo;
                    if(callee!=null&&callee.DeclaringType==typeof(Hardware)&&callee.Name=="Assert"){stop(condition & Test("eq",top(0),K(32,0)),K(1,1),true);continue;}
                    if(callee!=null&&PackedLayout.IsBits(callee.DeclaringType)){int n=callee.GetParameters().Length;push(h-n,BitsCall(callee,Enumerable.Range(h-n,n).Select(i=>(Expr)stack[i]).ToArray()));continue;}
                    throw Error(method,ins.Offset,"Sequential calls support Hardware.Assert and UBits intrinsics only.");
                }
                if(op=="ldlen"){if(!hasRam)throw Error(method,ins.Offset,"Missing RAM.");push(h-1,K(32,o.MemoryDepth));continue;}
                if(op=="ldelem.u4"||op=="ldelem.i4"||op=="stelem.i4")
                {
                    if(!hasRam)throw Error(method,ins.Offset,"Missing RAM.");bool write=op=="stelem.i4";var address=top(write?1:0);var valid=address.Lt(K(32,o.MemoryDepth));var access=condition & valid;
                    ramAddress=Select(access,address,ramAddress);stop(condition & ~valid,K(1,1),false);
                    if(write){ramWrite=ramWrite|access;ramData=Select(access,top(0),ramData);}
                    else
                    {ramRead=ramRead|access;set(pc,K(pw,waits[ins.Offset]),access);var wait=active & pc.Eq(K(pw,waits[ins.Offset]));set(stack[h-2],ram.ReadData,wait);set(pc,K(pw,ids[ins.Next]),wait);}
                    continue;
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
            set(cycles,cycles+K(cw,1),active);stop(busy & expired,K(1,1),false);set(timeout,K(1,1),busy & expired);
            foreach(var r in registers)set(r,K(r.Width,0),accept);set(busy,K(1,1),accept);foreach(var pair in captured)set(pair.Item1,pair.Item2,accept);
            foreach(var pair in next)m.Next(pair.Key,pair.Value);
            if(hasRam){m.Drive(ramAddr,ramAddress);m.Drive(ramWd,ramData);m.Drive(ramWe,ramWrite);m.Drive(ramRe,ramRead);}
            foreach(var pair in new[]{Tuple.Create("busy",busy),Tuple.Create("done",done),Tuple.Create("fault",fault),Tuple.Create("assertion_failed",assertion),Tuple.Create("timeout",timeout),Tuple.Create("result",result)})m.Drive(m.Output(pair.Item1,pair.Item2.Width),pair.Item2);
            m.Output("accept",1);m.Drive(m.Signals.Last(),accept);
            string[] notes={"busy: one operation in flight; new start requests are ignored while high.","done: sticky completion, including faults; cleared by reset or next accepted start.","fault: result invalid; arithmetic, bounds, assertion or watchdog failure. Prior RAM writes remain committed.","assertion_failed: selected Hardware.Assert was false; sticky until reset or new accepted start.","timeout: execution exceeded configured edge budget; sticky until reset or new accepted start.","result: declared-width return value, valid only when done=1 and fault=0; held until next start or reset.","accept: combinational start AND NOT busy; synchronous reset overrides acceptance."};
            string[] names={"busy","done","fault","assertion_failed","timeout","result","accept"};
            for(int k=0;k<names.Length;k++)m.Ports.Single(p=>p.Name==names[k]).Describe(notes[k]);
            return m;
        }
    }
}
