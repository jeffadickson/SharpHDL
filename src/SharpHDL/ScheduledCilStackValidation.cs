#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpHDL.HLS;

namespace SharpHDL;

public static partial class CilCompiler
{
    // CLI intermediate categories, not storage widths. Array lengths are native
    // unsigned values; the bounded RAM length may normalize through conv.i4/u4,
    // but must never accidentally acquire 32-bit shift/arithmetic semantics.
    [Flags]
    private enum ScheduledStackKind { Integer32=1, RamReference=2, NullReference=4, ArrayLength=8 }
    private sealed record ScheduledStackFrame(ScheduledStackKind[] Stack,ScheduledStackKind[] Locals);
    private static bool IsScheduledReference(ScheduledStackKind kind)
        =>kind!=0&&(kind&~(ScheduledStackKind.RamReference|ScheduledStackKind.NullReference))==0;

    private static Dictionary<int,ScheduledStackKind[]> ValidateScheduledStack(MethodInfo method,
        IReadOnlyList<Instruction> instructions,InstructionControlFlow flow)
    {
        var parameters=method.GetParameters();var locals=method.GetMethodBody()!.LocalVariables;
        // Bound the product of instruction states and local provenance slots.
        if(parameters.Length+locals.Count>1024)throw Error(method,0,"Typed analysis supports at most 1024 arguments and locals combined.");
        foreach(var ins in instructions)
        {
            string op=ins.Name;
            if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")||op=="starg"||op=="starg.s")
            {
                int index=VarIndex(ins,op.StartsWith("ldarg")?"ldarg":"starg");
                if(index<0||index>=parameters.Length)throw Error(method,ins.Offset,"Argument index is outside the method signature.");
                if(op.StartsWith("starg")&&parameters[index].ParameterType==typeof(uint[]))
                    throw Error(method,ins.Offset,"Cannot reassign the bound RAM reference.");
            }
            if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")||op=="stloc"||op=="stloc.s"||op.StartsWith("stloc."))
            {
                int index=VarIndex(ins,op.StartsWith("ldloc")?"ldloc":"stloc");
                if(index<0||index>=locals.Count)throw Error(method,ins.Offset,"Local index is outside the method body.");
            }
        }
        var code=instructions.ToDictionary(i=>i.Offset);
        var entries=new Dictionary<int,ScheduledStackFrame>();
        var pending=new Queue<(int Offset,ScheduledStackFrame Frame)>();
        pending.Enqueue((0,new(Array.Empty<ScheduledStackKind>(),locals.Select(l=>l.LocalType==typeof(uint[])?ScheduledStackKind.NullReference:ScheduledStackKind.Integer32).ToArray())));
        while(pending.Count!=0)
        {
            var (offset,frame)=pending.Dequeue();
            var incoming=frame.Stack;var incomingLocals=frame.Locals;
            if(entries.ContainsKey(offset))
            {
                var previous=entries[offset];
                if(previous.Stack.Length!=incoming.Length)throw Error(method,offset,"Inconsistent stack height.");
                bool changed=false;
                ScheduledStackKind Join(ScheduledStackKind left,ScheduledStackKind right)
                {
                    if(left==right)return left;
                    if(!IsScheduledReference(left)||!IsScheduledReference(right))throw Error(method,offset,"Incompatible typed stack at control-flow merge.");
                    return left|right;
                }
                incoming=incoming.Select((kind,i)=>Join(previous.Stack[i],kind)).ToArray();
                incomingLocals=incomingLocals.Select((kind,i)=>Join(previous.Locals[i],kind)).ToArray();
                changed=!previous.Stack.SequenceEqual(incoming)||!previous.Locals.SequenceEqual(incomingLocals);
                if(!changed)continue;
            }
            var ins=code[offset];string op=ins.Name;int count=PopCount(ins);
            if(incoming.Length<count||incoming.Length-count+PushCount(ins)>32||op=="ret"&&incoming.Length!=1)
                throw Error(method,offset,"Invalid/excessive evaluation stack.");
            entries[offset]=new(incoming,incomingLocals);
            // Entry snapshots are immutable. Transfer functions overwrite only a
            // private copy; a wider alias join reprocesses all affected successors.
            var localState=(ScheduledStackKind[])incomingLocals.Clone();var stack=incoming.ToList();
            ScheduledStackKind Pop()
            {var value=stack[^1];stack.RemoveAt(stack.Count-1);return value;}
            void Require(ScheduledStackKind expected)
            {
                var actual=Pop();
                if(actual!=expected)throw Error(method,offset,$"{op} requires {expected} on the typed stack (RAM must be definitely bound); found {actual}.");
            }
            if(op=="dup")stack.Add(stack[^1]);
            else if(op=="pop")Pop();
            else if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg."))
                stack.Add(parameters[VarIndex(ins,"ldarg")].ParameterType==typeof(uint[])?ScheduledStackKind.RamReference:ScheduledStackKind.Integer32);
            else if(op=="ldnull")stack.Add(ScheduledStackKind.NullReference);
            else if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc."))stack.Add(localState[VarIndex(ins,"ldloc")]);
            else if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc."))
            {
                int index=VarIndex(ins,"stloc");
                if(locals[index].LocalType==typeof(uint[]))
                {
                    var value=Pop();if(!IsScheduledReference(value))throw Error(method,offset,"Array alias storage requires a reference, not an integer.");
                    localState[index]=value;
                }
                else Require(ScheduledStackKind.Integer32);
            }
            else if(op.StartsWith("brtrue")||op.StartsWith("brfalse"))
            {
                var value=Pop();if(value!=ScheduledStackKind.Integer32&&!IsScheduledReference(value))
                    throw Error(method,offset,"Branch requires an integer or reference; normalize native array length first.");
            }
            else if(op=="ceq"||op.StartsWith("beq")||op.StartsWith("bne")||op=="cgt.un")
            {
                var right=Pop();var left=Pop();
                bool integers=left==ScheduledStackKind.Integer32&&right==ScheduledStackKind.Integer32;
                bool references=IsScheduledReference(left)&&IsScheduledReference(right);
                if(!integers&&(!references||op=="cgt.un"&&left!=ScheduledStackKind.NullReference&&right!=ScheduledStackKind.NullReference))
                    throw Error(method,offset,"Comparison requires matching integer/reference categories; reference ordering only supports cgt.un against definite null.");
                if(op=="ceq"||op=="cgt.un")stack.Add(ScheduledStackKind.Integer32);
            }
            else if(op=="ldlen") {Require(ScheduledStackKind.RamReference);stack.Add(ScheduledStackKind.ArrayLength);}
            else if(op=="ldelem.i4"||op=="ldelem.u4")
            {Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.RamReference);stack.Add(ScheduledStackKind.Integer32);}
            else if(op=="stelem.i4")
            {Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.RamReference);}
            else if(op=="conv.i4"||op=="conv.u4")
            {
                if(IsScheduledReference(Pop()))throw Error(method,offset,"Cannot convert a RAM reference to an integer.");
                stack.Add(ScheduledStackKind.Integer32);
            }
            else
            {
                for(int i=0;i<count;i++)Require(ScheduledStackKind.Integer32);
                for(int i=0;i<PushCount(ins);i++)stack.Add(ScheduledStackKind.Integer32);
            }
            foreach(int next in flow.Successors[offset])pending.Enqueue((next,new(stack.ToArray(),localState)));
        }
        return entries.ToDictionary(p=>p.Key,p=>p.Value.Stack);
    }
}
