using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace SharpHDL
{
    public static partial class CilCompiler
    {
        /// <summary>Pipeline a stateless infinite stream loop with one read and one write per iteration.</summary>
        public static PipelineHandle AttachPipeline<TIn,TOut>(Module m,string name,MethodInfo method,
            HwStream<TIn> input,HwStream<TOut> output,PipelineOptions options=null) where TIn:struct where TOut:struct
        {
            if(input==null||input.Data.Owner!=m)throw new ArgumentException("Pipeline input must belong to this module.");
            var program=CompilePipeline(method,input,output,false);
            var handle=PipelineScheduler.Attach(m,name,input,output,program.Value,options);
            PortablePdbSourceMap.Annotate(method,handle.DataflowGraph);
            handle.Report+=program.Report;return handle;
        }
        sealed class PipelineIteration
        {
            public Expr Value;
            public Expr[] Locals;
        }
        sealed class PipelineProgram
        {
            public Expr Value;
            public Expr[] Initial;
            public int[] Candidates,StateIds;
            public Expr[] NextState;
            public Signal Placeholder;
            public string Report;
        }
        static PipelineProgram CompilePipeline<TIn,TOut>(MethodInfo method,HwStream<TIn> input,HwStream<TOut> output,bool feedback)
            where TIn:struct where TOut:struct
        {
            var m=input==null?null:input.Data.Owner;
            var body=method.GetMethodBody();var ps=method.GetParameters();
            if(!method.IsStatic||method.ContainsGenericParameters||method.ReturnType!=typeof(void)||body==null||body.ExceptionHandlingClauses.Count!=0)
                throw Error(method,0,"Pipelines require a static void CIL body without exception handlers.");
            if(ps.Length!=2||ps[0].ParameterType!=typeof(HwStream<TIn>)||ps[1].ParameterType!=typeof(HwStream<TOut>)||input==null||output==null||ReferenceEquals(input,output)||input.Data.Owner!=m||output.Data.Owner!=m)
                throw Error(method,0,"Bind distinct input/output channels of the declared parameter types in this module.");
            if(!ProcessScalar(typeof(TIn))||!ProcessScalar(typeof(TOut))||body.LocalVariables.Any(v=>!ProcessScalar(v.LocalType)))
                throw Error(method,0,"Pipeline tokens and locals must be scalar values of 1..32 bits.");
            var list=Decode(method);if(list.Count>512)throw Error(method,0,"Pipeline exceeds 512 CIL instructions.");
            var code=list.ToDictionary(i=>i.Offset);
            var backEdges=list.Where(i=>(i.Name=="br"||i.Name=="br.s"||Conditional(i.Name))&&(int)i.Operand<=i.Offset).ToArray();
            if(backEdges.Length==0||backEdges.Any(i=>Conditional(i.Name))||backEdges.Select(i=>(int)i.Operand).Distinct().Count()!=1)
                throw Error(method,0,"Pipeline requires one unconditional outer loop; nested loops and conditional back edges are unsupported.");
            int entry=(int)backEdges[0].Operand;int prefix=0;var prefixStack=new Stack<Expr>();var initial=new Expr[body.LocalVariables.Count];var prefixSeen=new HashSet<int>();
            while(prefix!=entry)
            {
                Instruction i;if(!prefixSeen.Add(prefix)||!code.TryGetValue(prefix,out i))throw Error(method,prefix,"Invalid loop entry.");
                if(i.Name.StartsWith("ldc.i4"))prefixStack.Push(K(32,i.Name=="ldc.i4.m1"?-1:i.Name=="ldc.i4"||i.Name=="ldc.i4.s"?(int)i.Operand:int.Parse(i.Name.Substring(7))));
                else if(i.Name=="stloc"||i.Name=="stloc.s"||i.Name.StartsWith("stloc."))
                {if(prefixStack.Count==0)throw Error(method,prefix,"Nonconstant pipeline initialization is unsupported.");initial[VarIndex(i,"stloc")]=prefixStack.Pop();}
                else if(i.Name!="nop"&&i.Name!="br"&&i.Name!="br.s")throw Error(method,prefix,"Pipeline initialization outside the repeated iteration is unsupported; no loop-carried state.");
                prefix=i.Name=="br"||i.Name=="br.s"?(int)i.Operand:i.Next;
            }
            if(prefixStack.Count!=0)throw Error(method,entry,"Nonempty stack entering pipeline loop.");
            // Stateless mode requires assignment on every iteration path. Feedback mode
            // seeds initialized locals symbolically and records those actually read before assignment.
            var candidates=Enumerable.Range(0,initial.Length).Where(i=>initial[i]!=null).ToArray();
            if(feedback&&candidates.Length>32)throw Error(method,entry,"Pipeline has more than 32 initialized locals.");
            var placeholder=feedback?new Signal(m,"feedback_input_placeholder",input.Data.Width+32*candidates.Length,"wire"):input.Data;
            var seeds=new Expr[initial.Length];var used=new HashSet<int>();
            if(feedback)for(int n=0;n<candidates.Length;n++)seeds[candidates[n]]=placeholder.Slice(input.Data.Width+32*n+31,input.Data.Width+32*n);
            Expr token=feedback?placeholder.Slice(input.Data.Width-1,0):input.Data;
            int steps=0,branches=0,paths=0;
            Func<Expr,PipelineIteration,PipelineIteration,PipelineIteration> merge=(condition,yes,no)=>
            {
                var values=new Expr[initial.Length];
                if(feedback)foreach(int id in candidates)values[id]=Select(condition,yes.Locals[id],no.Locals[id]);
                return new PipelineIteration{Value=Select(condition,yes.Value,no.Value),Locals=values};
            };
            Func<int,List<object>,Expr[],Expr,int,int,HashSet<int>,PipelineIteration> run=null;
            run=(start,stack,locals,result,reads,writes,visited)=>
            {
                int offset=start;
                Func<object> pop=()=>{if(stack.Count==0)throw Error(method,offset,"Empty pipeline CIL stack.");var x=stack[stack.Count-1];stack.RemoveAt(stack.Count-1);return x;};
                Func<Expr> scalar=()=>{var e=pop() as Expr;if(e==null)throw Error(method,offset,"Channel handles cannot be used as scalar data.");return e;};
                Action<Expr,string> push=(e,op)=>{if(!(e is Signal)&&e.Op!="const")e.Named(method.Name+"_IL_"+offset.ToString("x4")+"_"+op).Describe(method.Name+" IL_"+offset.ToString("x4")+" "+op+"; explicit CIL width/sign semantics.");stack.Add(e);};
                int pc=start;
                while(true)
                {
                    if(pc==entry&&visited.Count>0)
                    {
                        if(++paths>128)throw Error(method,pc,"Pipeline exceeds 128 control paths.");
                        if(reads!=1||writes!=1||result==null||stack.Count!=0)throw Error(method,pc,"Every pipeline path must have exactly one read/write and an empty stack at the back edge.");
                        return new PipelineIteration{Value=result,Locals=locals};
                    }
                    Instruction i;if(!code.TryGetValue(pc,out i)||!visited.Add(pc))throw Error(method,pc,"Nested cycles or invalid pipeline branch target.");
                    if(++steps>8192)throw Error(method,pc,"Pipeline exceeds 8192 path-expanded instructions.");
                    offset=i.Offset;string op=i.Name;pc=i.Next;
                    if(stack.Count>32)throw Error(method,offset,"Pipeline evaluation stack exceeds 32 entries.");
                    if(op=="nop")continue;
                    if(op=="br"||op=="br.s"){pc=(int)i.Operand;continue;}
                    if(op=="ret")throw Error(method,offset,"Finite return is unsupported in an always-running pipeline.");
                    if(Conditional(op)||op=="switch")
                    {
                        if(++branches>128)throw Error(method,offset,"Pipeline exceeds 128 path-expanded branches.");
                        Func<int,PipelineIteration> branch=target=>run(target,new List<object>(stack),(Expr[])locals.Clone(),result,reads,writes,new HashSet<int>(visited));
                        if(op=="switch")
                        {
                            var selector=scalar();var targets=(int[])i.Operand;
                            if(targets.Any(t=>t<=offset))throw Error(method,offset,"Backward switch targets are unsupported.");
                            var selected=branch(i.Next);
                            for(int k=targets.Length-1;k>=0;k--)selected=merge(selector.Eq(K(32,k)),branch(targets[k]),selected);
                            selected.Value.Named(method.Name+"_IL_"+offset.ToString("x4")+"_switch").Describe(method.Name+": predicated switch; one output token on every path.");return selected;
                        }
                        Expr c;
                        if(op.StartsWith("brtrue")||op.StartsWith("brfalse"))
                        {c=Test("eq",scalar(),K(32,0));if(op.StartsWith("brtrue"))c=Not(c);}
                        else
                        {
                            var b=scalar();var a=scalar();string test=op.Substring(0,3);
                            c=Test(test=="beq"||test=="bne"?"eq":test=="blt"||test=="bge"?"lt":"gt",a,b,op.Contains(".un"));
                            if(test=="bne"||test=="ble"||test=="bge")c=Not(c);
                        }
                        var selectedBranch=merge(c,branch((int)i.Operand),branch(i.Next));
                        selectedBranch.Value.Named(method.Name+"_IL_"+offset.ToString("x4")+"_select")
                            .Describe(method.Name+" IL_"+offset.ToString("x4")+": predicated branch; condition and data aligned by scheduler.");
                        return selectedBranch;
                    }
                    if(op.StartsWith("ldc.i4")){stack.Add(K(32,op=="ldc.i4.m1"?-1:op=="ldc.i4"||op=="ldc.i4.s"?(int)i.Operand:int.Parse(op.Substring(7))));continue;}
                    if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")){int arg=VarIndex(i,"ldarg");if(arg<0||arg>1)throw Error(method,offset,"Invalid channel parameter.");stack.Add(arg);continue;}
                    if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")){int id=VarIndex(i,"ldloc");var e=locals[id];if(e==null)throw Error(method,offset,"Local read before assignment: use constant-initialized state with AttachFeedbackPipeline, or assign on every path.");if(feedback&&ReferenceEquals(e,seeds[id]))used.Add(id);stack.Add(e);continue;}
                    if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc.")){locals[VarIndex(i,"stloc")]=scalar();continue;}
                    if(op=="dup"){var x=pop();stack.Add(x);stack.Add(x);continue;}
                    if(op=="pop"){pop();continue;}
                    if(op=="call"||op=="callvirt")
                    {
                        var callee=i.Operand as MethodInfo;if(callee==null)throw Error(method,offset,"Unsupported pipeline call.");
                        if(IsChannel(callee.DeclaringType))
                        {
                            if(!callee.IsStatic&&callee.Name=="Read"&&callee.GetParameters().Length==0)
                            {
                                var receiver=pop();if(!(receiver is int)||(int)receiver!=0||callee.DeclaringType!=ps[0].ParameterType||++reads!=1||writes!=0)throw Error(method,offset,"Exactly one input.Read before output.Write is required per iteration.");
                                stack.Add(Resize(token,32,ProcessSigned(typeof(TIn))));continue;
                            }
                            if(!callee.IsStatic&&callee.Name=="Write"&&callee.GetParameters().Length==1)
                            {
                                var value=scalar();var receiver=pop();if(!(receiver is int)||(int)receiver!=1||callee.DeclaringType!=ps[1].ParameterType||++writes!=1||reads!=1)throw Error(method,offset,"Exactly one output.Write after input.Read is required per iteration.");
                                result=Resize(value,output.Data.Width);continue;
                            }
                            throw Error(method,offset,"Unsupported channel call in pipeline.");
                        }
                        if(callee.IsStatic&&PackedLayout.IsBits(callee.DeclaringType))
                        {
                            int n=callee.GetParameters().Length;var args=new Expr[n];for(int k=n-1;k>=0;k--)args[k]=scalar();push(Resize(BitsCall(callee,args),32),op);continue;
                        }
                        throw Error(method,offset,"Pipeline calls support only channel Read/Write and UBits intrinsics; faults/helper calls are unsupported.");
                    }
                    if(op=="conv.i4"||op=="conv.u4"){stack.Add(scalar());continue;}
                    if(op=="conv.i1"||op=="conv.u1"||op=="conv.i2"||op=="conv.u2"){push(Resize(Resize(scalar(),op.EndsWith("1")?8:16),32,op[5]=='i'),op);continue;}
                    if(op=="not"||op=="neg"){push(Unary(op,scalar()),op);continue;}
                    if(op=="add"||op=="sub"||op=="mul"||op=="and"||op=="or"||op=="xor")
                    {var b=scalar();var a=scalar();push(Bin(op,a,b),op);continue;}
                    if(op=="ceq"||op=="clt"||op=="clt.un"||op=="cgt"||op=="cgt.un")
                    {var b=scalar();var a=scalar();push(Resize(Test(op.Substring(1,2),a,b,op.EndsWith(".un")),32),op);continue;}
                    if(op=="shl"||op=="shr"||op=="shr.un")
                    {var b=scalar();var a=scalar();push(Shift(a,b,op),op);continue;}
                    throw Error(method,offset,"Unsupported pipelined opcode "+op+"; use AttachProcess for sequential semantics.");
                }
            };
            var iteration=run(entry,new List<object>(),(Expr[])seeds.Clone(),null,0,0,new HashSet<int>());
            if(feedback&&used.Count==0)throw Error(method,entry,"No loop-carried state found; use AttachPipeline.");
            if(used.Count>16)throw Error(method,entry,"Feedback pipeline supports at most 16 state locals.");
            var ids=used.OrderBy(i=>i).ToArray();
            return new PipelineProgram{Value=iteration.Value,Initial=initial,Candidates=candidates,StateIds=ids,
                NextState=ids.Select(i=>iteration.Locals[i]).ToArray(),Placeholder=placeholder,
                Report="Predicated control: "+branches+" path-expanded branches, "+paths+" terminal paths, "+steps+" visited instructions.\n"};
        }
    }
}
