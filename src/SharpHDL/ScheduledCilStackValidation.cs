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
    private enum ScheduledStackKind { Integer32, RamReference, ArrayLength }

    private static Dictionary<int,ScheduledStackKind[]> ValidateScheduledStack(MethodInfo method,
        IReadOnlyList<Instruction> instructions,InstructionControlFlow flow)
    {
        var parameters=method.GetParameters();var locals=method.GetMethodBody()!.LocalVariables;
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
        var entries=new Dictionary<int,ScheduledStackKind[]>();
        var pending=new Queue<(int Offset,ScheduledStackKind[] Stack)>();pending.Enqueue((0,Array.Empty<ScheduledStackKind>()));
        while(pending.Count!=0)
        {
            var (offset,incoming)=pending.Dequeue();
            if(entries.ContainsKey(offset))
            {
                var previous=entries[offset];
                if(previous.Length!=incoming.Length)throw Error(method,offset,"Inconsistent stack height.");
                if(!previous.SequenceEqual(incoming))throw Error(method,offset,"Incompatible typed stack at control-flow merge.");
                continue;
            }
            var ins=code[offset];string op=ins.Name;int count=PopCount(ins);
            if(incoming.Length<count||incoming.Length-count+PushCount(ins)>32||op=="ret"&&incoming.Length!=1)
                throw Error(method,offset,"Invalid/excessive evaluation stack.");
            entries.Add(offset,incoming);
            var stack=incoming.ToList();
            ScheduledStackKind Pop()
            {var value=stack[^1];stack.RemoveAt(stack.Count-1);return value;}
            void Require(ScheduledStackKind expected)
            {
                var actual=Pop();
                if(actual!=expected)throw Error(method,offset,$"{op} requires {expected} on the typed stack; found {actual}.");
            }
            if(op=="dup")stack.Add(stack[^1]);
            else if(op=="pop")Pop();
            else if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg."))
                stack.Add(parameters[VarIndex(ins,"ldarg")].ParameterType==typeof(uint[])?ScheduledStackKind.RamReference:ScheduledStackKind.Integer32);
            else if(op=="ldlen") {Require(ScheduledStackKind.RamReference);stack.Add(ScheduledStackKind.ArrayLength);}
            else if(op=="ldelem.i4"||op=="ldelem.u4")
            {Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.RamReference);stack.Add(ScheduledStackKind.Integer32);}
            else if(op=="stelem.i4")
            {Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.Integer32);Require(ScheduledStackKind.RamReference);}
            else if(op=="conv.i4"||op=="conv.u4")
            {
                if(Pop()==ScheduledStackKind.RamReference)throw Error(method,offset,"Cannot convert a RAM reference to an integer.");
                stack.Add(ScheduledStackKind.Integer32);
            }
            else
            {
                for(int i=0;i<count;i++)Require(ScheduledStackKind.Integer32);
                for(int i=0;i<PushCount(ins);i++)stack.Add(ScheduledStackKind.Integer32);
            }
            foreach(int next in flow.Successors[offset])pending.Enqueue((next,stack.ToArray()));
        }
        return entries;
    }
}
