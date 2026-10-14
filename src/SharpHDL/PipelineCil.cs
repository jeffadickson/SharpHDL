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
            var body=method.GetMethodBody();var ps=method.GetParameters();
            if(!method.IsStatic||method.ContainsGenericParameters||method.ReturnType!=typeof(void)||body==null||body.ExceptionHandlingClauses.Count!=0)
                throw Error(method,0,"Pipelines require a static void CIL body without exception handlers.");
            if(ps.Length!=2||ps[0].ParameterType!=typeof(HwStream<TIn>)||ps[1].ParameterType!=typeof(HwStream<TOut>)||input==null||output==null||ReferenceEquals(input,output)||input.Data.Owner!=m||output.Data.Owner!=m)
                throw Error(method,0,"Bind distinct input/output channels of the declared parameter types in this module.");
            if(!ProcessScalar(typeof(TIn))||!ProcessScalar(typeof(TOut))||body.LocalVariables.Any(v=>!ProcessScalar(v.LocalType)))
                throw Error(method,0,"Pipeline tokens and locals must be scalar values of 1..32 bits.");
            var list=Decode(method);if(list.Count>512)throw Error(method,0,"Pipeline exceeds 512 CIL instructions.");
            var code=list.ToDictionary(i=>i.Offset);var path=new List<Instruction>();var seen=new Dictionary<int,int>();int pc=0;
            while(!seen.ContainsKey(pc))
            {
                Instruction i;if(!code.TryGetValue(pc,out i))throw Error(method,pc,"Invalid pipeline branch target.");
                seen[pc]=path.Count;path.Add(i);
                if(Conditional(i.Name)||i.Name=="switch"||i.Name=="ret")throw Error(method,pc,"Pipeline requires an unconditional infinite loop; data-dependent branches/returns need AttachProcess.");
                pc=i.Name=="br"||i.Name=="br.s"?(int)i.Operand:i.Next;
            }
            int entry=seen[pc];
            foreach(var i in path.Take(entry))if(i.Name!="nop"&&i.Name!="br"&&i.Name!="br.s")throw Error(method,i.Offset,"Pipeline initialization outside the repeated iteration is unsupported; no loop-carried state.");
            var stack=new List<object>();var locals=new Expr[body.LocalVariables.Count];Expr result=null;int reads=0,writes=0,offset=0;
            Func<object> pop=()=>{if(stack.Count==0)throw Error(method,offset,"Empty pipeline CIL stack.");var x=stack[stack.Count-1];stack.RemoveAt(stack.Count-1);return x;};
            Func<Expr> scalar=()=>{var e=pop() as Expr;if(e==null)throw Error(method,offset,"Channel handles cannot be used as scalar data.");return e;};
            Action<Expr,string> push=(e,op)=>{if(!(e is Signal)&&e.Op!="const")e.Named(method.Name+"_IL_"+offset.ToString("x4")+"_"+op).Describe(method.Name+" IL_"+offset.ToString("x4")+" "+op+"; explicit CIL width/sign semantics.");stack.Add(e);};
            foreach(var i in path.Skip(entry))
            {
                offset=i.Offset;string op=i.Name;
                if(stack.Count>32)throw Error(method,offset,"Pipeline evaluation stack exceeds 32 entries.");
                if(op=="nop"||op=="br"||op=="br.s")continue;
                if(op.StartsWith("ldc.i4")){stack.Add(K(32,op=="ldc.i4.m1"?-1:op=="ldc.i4"||op=="ldc.i4.s"?(int)i.Operand:int.Parse(op.Substring(7))));continue;}
                if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")){int arg=VarIndex(i,"ldarg");if(arg<0||arg>1)throw Error(method,offset,"Invalid channel parameter.");stack.Add(arg);continue;}
                if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")){var e=locals[VarIndex(i,"ldloc")];if(e==null)throw Error(method,offset,"Local read before assignment in this iteration: loop-carried state is unsupported.");stack.Add(e);continue;}
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
                            stack.Add(Resize(input.Data,32,ProcessSigned(typeof(TIn))));continue;
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
            if(reads!=1||writes!=1||result==null||stack.Count!=0)throw Error(method,offset,"Pipeline iteration must have exactly one read/write and an empty stack at the back edge.");
            return PipelineScheduler.Attach(m,name,input,output,result,options);
        }
    }
}
