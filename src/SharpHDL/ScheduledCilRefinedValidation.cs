#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    private sealed record ScheduledRefinedFrame(ScheduledStackValue[] Stack,ScheduledStackKind[] Locals,ScheduledNullTest?[] Tests,BigInteger[] Relations);

    private static Dictionary<int,ScheduledStackKind[]> ValidateRefinedScheduledStack(MethodInfo method,
        IReadOnlyList<Instruction> instructions,InstructionControlFlow flow,ScheduledRamPlan[] ramPlans)
    {
        var parameters=method.GetParameters();var locals=method.GetMethodBody()!.LocalVariables;
        var origins=ramPlans.ToDictionary(p=>p.Parameter,p=>(ScheduledStackKind)(16<<(p.Tag-1)));
        if(parameters.Length+locals.Count>1024)throw Error(method,0,"Typed analysis supports at most 1024 arguments and locals combined.");
        foreach(var ins in instructions)
        {
            string op=ins.Name;
            if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")||op=="starg"||op=="starg.s")
            {
                int index=VarIndex(ins,op.StartsWith("ldarg")?"ldarg":"starg");
                if(index<0||index>=parameters.Length)throw Error(method,ins.Offset,"Argument index is outside the method signature.");
                if(op.StartsWith("starg")&&parameters[index].ParameterType==typeof(uint[]))throw Error(method,ins.Offset,"Cannot reassign the bound RAM reference.");
            }
            if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")||op=="stloc"||op=="stloc.s"||op.StartsWith("stloc."))
            {
                int index=VarIndex(ins,op.StartsWith("ldloc")?"ldloc":"stloc");
                if(index<0||index>=locals.Count)throw Error(method,ins.Offset,"Local index is outside the method body.");
            }
        }
        var code=instructions.ToDictionary(i=>i.Offset);var entries=new Dictionary<int,ScheduledRefinedFrame>();
        var pending=new Queue<(int Offset,ScheduledRefinedFrame Frame)>();
        BigInteger initialAliases=BigInteger.Zero;foreach(var local in locals)if(local.LocalType==typeof(uint[]))initialAliases|=BigInteger.One<<local.LocalIndex;
        pending.Enqueue((0,new(Array.Empty<ScheduledStackValue>(),locals.Select(l=>l.LocalType==typeof(uint[])?ScheduledStackKind.NullReference:ScheduledStackKind.Integer32).ToArray(),new ScheduledNullTest?[locals.Count],locals.Select(l=>l.LocalType==typeof(uint[])?initialAliases:BigInteger.Zero).ToArray())));
        while(pending.Count!=0)
        {
            var (offset,frame)=pending.Dequeue();
            if(entries.ContainsKey(offset))
            {
                var previous=entries[offset];
                if(previous.Stack.Length!=frame.Stack.Length)throw Error(method,offset,"Inconsistent stack height.");
                ScheduledStackKind Join(ScheduledStackKind left,ScheduledStackKind right)
                {
                    if(left==right)return left;
                    if(!IsScheduledReference(left)||!IsScheduledReference(right))throw Error(method,offset,"Incompatible typed stack at control-flow merge.");
                    return left|right;
                }
                var merged=new ScheduledRefinedFrame(frame.Stack.Select((v,i)=>new ScheduledStackValue(
                    Join(previous.Stack[i].Kind,v.Kind),previous.Stack[i].Local==v.Local?v.Local:-1,
                    JoinScheduledTests(previous.Stack[i].Test,v.Test),previous.Stack[i].Boolean==v.Boolean?v.Boolean:null)).ToArray(),
                    frame.Locals.Select((k,i)=>Join(previous.Locals[i],k)).ToArray(),
                    frame.Tests.Select((t,i)=>JoinScheduledTests(previous.Tests[i],t)).ToArray(),
                    frame.Relations.Select((r,i)=>r&previous.Relations[i]).ToArray());
                if(previous.Stack.SequenceEqual(merged.Stack)&&previous.Locals.SequenceEqual(merged.Locals)&&previous.Tests.SequenceEqual(merged.Tests)&&previous.Relations.SequenceEqual(merged.Relations))continue;
                frame=merged;
            }
            var ins=code[offset];string op=ins.Name;int count=PopCount(ins);
            if(frame.Stack.Length<count||frame.Stack.Length-count+PushCount(ins)>32||op=="ret"&&frame.Stack.Length!=1)
                throw Error(method,offset,"Invalid/excessive evaluation stack.");
            entries[offset]=frame;
            var localState=(ScheduledStackKind[])frame.Locals.Clone();var localTests=(ScheduledNullTest?[])frame.Tests.Clone();var relations=(BigInteger[])frame.Relations.Clone();var stack=frame.Stack.ToList();
            ScheduledStackValue Pop(){var v=stack[^1];stack.RemoveAt(stack.Count-1);return v;}
            void Require(ScheduledStackKind expected)
            {
                var actual=Pop().Kind;
                bool bound=expected==ScheduledStackKind.RamReference&&IsScheduledReference(actual)&&(actual&ScheduledStackKind.NullReference)==0;
                if(actual!=expected&&!bound)throw Error(method,offset,$"{op} requires {expected} on the typed stack (RAM must be definitely bound); found {actual}.");
            }
            void Invalidate(int index)
            {
                for(int i=0;i<stack.Count;i++)stack[i]=ForgetScheduledLocal(stack[i],index);
                for(int i=0;i<localTests.Length;i++)localTests[i]=ForgetScheduledLocal(localTests[i],index);
            }
            ScheduledNullTest? test=null;bool? truth=null;bool conditional=false;
            if(op=="dup")stack.Add(stack[^1]);
            else if(op=="pop")Pop();
            else if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg."))
            {
                int i=VarIndex(ins,"ldarg");stack.Add(new(origins.ContainsKey(i)?origins[i]:ScheduledStackKind.Integer32));
            }
            else if(op=="ldnull")stack.Add(new(ScheduledStackKind.NullReference));
            else if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc."))
            {
                int i=VarIndex(ins,"ldloc");stack.Add(new(localState[i],IsScheduledReference(localState[i])?i:-1,localTests[i]));
            }
            else if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc."))
            {
                int i=VarIndex(ins,"stloc");var value=Pop();Invalidate(i);
                if(locals[i].LocalType==typeof(uint[]))
                {if(!IsScheduledReference(value.Kind))throw Error(method,offset,"Array alias storage requires a reference, not an integer.");localState[i]=value.Kind;AssignScheduledRelation(relations,i,value.Local);}
                else
                {if(value.Kind!=ScheduledStackKind.Integer32)throw Error(method,offset,$"{op} requires Integer32 on the typed stack.");localTests[i]=value.Test;}
            }
            else if(op.StartsWith("brtrue")||op.StartsWith("brfalse"))
            {
                var value=Pop();conditional=true;
                if(IsScheduledReference(value.Kind))test=new(value.Local,true,value.Kind);
                else if(value.Kind==ScheduledStackKind.Integer32){test=value.Test;truth=value.Boolean;}
                else throw Error(method,offset,"Branch requires an integer or reference; normalize native array length first.");
                if(op.StartsWith("brfalse")){if(test.HasValue)test=test.Value with{TrueIsNonNull=!test.Value.TrueIsNonNull};if(truth.HasValue)truth=!truth.Value;}
            }
            else if(op=="ceq"||op.StartsWith("beq")||op.StartsWith("bne")||op=="cgt.un")
            {
                var right=Pop();var left=Pop();
                bool integers=left.Kind==ScheduledStackKind.Integer32&&right.Kind==ScheduledStackKind.Integer32;
                bool refs=IsScheduledReference(left.Kind)&&IsScheduledReference(right.Kind);
                if(!integers&&(!refs||op=="cgt.un"&&left.Kind!=ScheduledStackKind.NullReference&&right.Kind!=ScheduledStackKind.NullReference))
                    throw Error(method,offset,"Comparison requires matching integer/reference categories; reference ordering only supports cgt.un against definite null.");
                if(refs)
                {
                    var value=right.Kind==ScheduledStackKind.NullReference?left:right;
                    if(left.Kind==ScheduledStackKind.NullReference||right.Kind==ScheduledStackKind.NullReference)
                    {
                        if(op=="cgt.un"&&right.Kind!=ScheduledStackKind.NullReference)truth=false;
                        else test=new(value.Local,op=="cgt.un"||op.StartsWith("bne"),value.Kind);
                    }
                }
                else if(op=="ceq")
                {
                    // C# commonly negates a materialized null test using ceq 0.
                    if(left.Test.HasValue&&right.Boolean.HasValue)test=left.Test.Value with{TrueIsNonNull=left.Test.Value.TrueIsNonNull==right.Boolean.Value};
                    if(right.Test.HasValue&&left.Boolean.HasValue)test=right.Test.Value with{TrueIsNonNull=right.Test.Value.TrueIsNonNull==left.Boolean.Value};
                }
                if(op=="ceq"||op=="cgt.un"){stack.Add(new(ScheduledStackKind.Integer32,Test:test,Boolean:truth));test=null;truth=null;}
                else conditional=true;
            }
            else if(op=="ldlen"){Require(ScheduledStackKind.RamReference);stack.Add(new(ScheduledStackKind.ArrayLength));}
            else if(op=="ldelem.i4"||op=="ldelem.u4")
            {Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.RamReference);stack.Add(new(ScheduledStackKind.Integer32));}
            else if(op=="stelem.i4"){Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.RamReference);}
            else if(op=="conv.i4"||op=="conv.u4")
            {var v=Pop();if(IsScheduledReference(v.Kind))throw Error(method,offset,"Cannot convert a RAM reference to an integer.");stack.Add(v with{Kind=ScheduledStackKind.Integer32});}
            else
            {
                for(int i=0;i<count;i++)Require(ScheduledStackKind.Integer32);
                bool? literal=op=="ldc.i4.0"?false:op=="ldc.i4.1"?true:null;
                for(int i=0;i<PushCount(ins);i++)stack.Add(new(ScheduledStackKind.Integer32,Boolean:literal));
            }
            foreach(int next in flow.Successors[offset])
            {
                var output=new ScheduledRefinedFrame(stack.ToArray(),(ScheduledStackKind[])localState.Clone(),(ScheduledNullTest?[])localTests.Clone(),(BigInteger[])relations.Clone());
                // If target and fallthrough coincide, both outcomes reach the
                // same block. Refining it as only the taken edge is unsound.
                if(conditional&&(int)ins.Operand!=ins.Next)
                {
                    bool taken=next==(int)ins.Operand;
                    if(truth.HasValue&&truth.Value!=taken)continue;
                    if(test.HasValue)
                    {
                        var fact=test.Value;bool nonnull=taken==fact.TrueIsNonNull;
                        var narrowed=nonnull?fact.Candidates&~ScheduledStackKind.NullReference:fact.Candidates&ScheduledStackKind.NullReference;
                        if(narrowed==0)continue;
                        if(fact.Local>=0)
                        {
                            var equal=relations[fact.Local];
                            for(int i=0;i<output.Locals.Length;i++)if((equal&(BigInteger.One<<i))!=0)output.Locals[i]=narrowed;
                            for(int i=0;i<output.Stack.Length;i++)if(output.Stack[i].Local>=0&&(equal&(BigInteger.One<<output.Stack[i].Local))!=0)output.Stack[i]=output.Stack[i] with{Kind=narrowed};
                        }
                    }
                }
                pending.Enqueue((next,output));
            }
        }
        return entries.ToDictionary(p=>p.Key,p=>p.Value.Stack.Select(v=>v.Kind).ToArray());
    }
}
