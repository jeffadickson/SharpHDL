using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using SharpHDL.Core;
using SharpHDL.HLS;

namespace SharpHDL
{
#nullable enable
    /// <summary>Scheduled Core hardware and its original method/IL control-state map.</summary>
    public sealed class ScheduledCilCompilation
    {
        internal ScheduledCilCompilation(ScheduledHardwareDesign design,MethodInfo method,Dictionary<string,int> offsets,
            Dictionary<string,int> encodings,InstructionControlFlow controlFlow)
        {
            Design=design;Method=method;ControlFlow=controlFlow;StateOffsets=new ReadOnlyDictionary<string,int>(offsets);
            StateEncodings=new ReadOnlyDictionary<string,int>(encodings);Symbols=PortablePdbSourceMap.Read(method);
            StateSources=new ReadOnlyDictionary<string,CilSourceSpan?>(encodings.Keys.ToDictionary(key=>key,
                key=>offsets.TryGetValue(key,out int offset)?Symbols.FindSource(offset):null));
        }
        /// <summary>Executable, backend-neutral scheduled hardware.</summary>
        public ScheduledHardwareDesign Design { get; }
        /// <summary>Original CIL method; never executed by the compiler.</summary>
        public MethodInfo Method { get; }
        /// <summary>Validated original instruction graph, including canonical reachable cycles.</summary>
        public InstructionControlFlow ControlFlow { get; }
        /// <summary>Control-state names to original IL offsets, including load wait states.</summary>
        public IReadOnlyDictionary<string,int> StateOffsets { get; }
        /// <summary>All control-state names and actual scheduler register encodings, including idle.</summary>
        public IReadOnlyDictionary<string,int> StateEncodings { get; }
        /// <summary>Matched symbol availability and original sequence-point boundaries.</summary>
        public PortablePdbMethodMap Symbols { get; }
        /// <summary>Visible source spans for states, with null for idle/hidden/unmapped states.</summary>
        public IReadOnlyDictionary<string,CilSourceSpan?> StateSources { get; }
    }
#nullable restore
    public static partial class CilCompiler
    {
        /// <summary>
        /// Lower static integer/Boolean control flow and at most one uint[] RAM
        /// through the existing decoder/stack analysis into ScheduledMachineBuilder.
        /// Unchecked 8/16-bit stack conversions truncate then sign/zero-extend to Int32;
        /// byte/sbyte/short/ushort/char storage uses declared widths and extends on load.
        /// Checked/wide conversions remain unsupported. Ports use CLR scalar widths.
        /// One instruction executes
        /// per edge; RAM loads add a capture/wait edge. Unsupported constructs are
        /// rejected with method and IL-offset diagnostics; no legacy fallback occurs.
        /// </summary>
        public static ScheduledCilCompilation BuildScheduledSequential(MethodInfo method,string name,SequentialOptions options=null)
        {
            if(method==null)throw new ArgumentNullException(nameof(method));
            var o=options??new SequentialOptions();var body=method.GetMethodBody();var ps=method.GetParameters();
            bool Scalar(Type t)=>TryScheduledScalar(t,out _);
            if(!method.IsStatic||method.ContainsGenericParameters||body==null||body.ExceptionHandlingClauses.Count!=0||!TryScheduledScalar(method.ReturnType,out var returnType))
                throw Error(method,0,"Scheduled sequential methods require a static CIL body, supported integer/Boolean return and no exception handlers.");
            if(o.MaxCycles<1||o.MaxCycles>1000000||o.MaxInstructions<1||o.MaxInstructions>4096)
                throw new ArgumentException("Invalid scheduled sequential instruction/clock limits.",nameof(options));
            if(o.MemoryOptions!=null)
                throw Error(method,0,"Scheduled sequential vendor memory options require an explicit Core inference policy.");
            if(!ps.Any(p=>p.ParameterType==typeof(uint[]))&&o.InitialMemory!=null)
                throw Error(method,0,"Initial memory requires a uint[] RAM argument.");
            if(body.LocalVariables.Any(v=>!Scalar(v.LocalType))||!body.InitLocals&&body.LocalVariables.Count>0||
                ps.Any(p=>p.ParameterType!=typeof(uint[])&&!Scalar(p.ParameterType))||ps.Count(p=>p.ParameterType==typeof(uint[]))>1)
                throw Error(method,0,"Scheduled locals/arguments must be byte, sbyte, short, ushort, char, Int32, UInt32 or Boolean with at most one uint[] argument.");
            if(Attribute.IsDefined(method.ReturnParameter,typeof(PortWidthAttribute))||ps.Any(p=>Attribute.IsDefined(p,typeof(PortWidthAttribute))))
                throw Error(method,0,"Scheduled sequential ports use CLR widths; PortWidth overrides are unsupported.");
            bool hasRam=ps.Any(p=>p.ParameterType==typeof(uint[]));
            if(hasRam&&(o.MemoryDepth<1||o.MemoryDepth>65536))throw new ArgumentOutOfRangeException(nameof(options));
            var list=Decode(method);
            if(list.Count>o.MaxInstructions)throw Error(method,0,"Scheduled sequential instruction limit exceeded.");
            bool SupportedInstruction(string op)=>op=="nop"||op=="ret"||op=="dup"||op=="pop"||op=="not"||op=="neg"||
                op=="add"||op=="sub"||op=="mul"||op=="and"||op=="or"||op=="xor"||op=="ceq"||op=="clt"||op=="clt.un"||op=="cgt"||op=="cgt.un"||
                op=="conv.i4"||op=="conv.u4"||op=="conv.i1"||op=="conv.u1"||op=="conv.i2"||op=="conv.u2"||
                op=="shl"||op=="shr"||op=="shr.un"||op=="br"||op=="br.s"||op=="switch"||Conditional(op)||
                op=="ldlen"||op=="ldelem.u4"||op=="ldelem.i4"||op=="stelem.i4"||
                op.StartsWith("ldc.i4")||op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg.")||
                op=="starg"||op=="starg.s"||op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc.")||
                op=="stloc"||op=="stloc.s"||op.StartsWith("stloc.");
            foreach(var ins in list)if(!SupportedInstruction(ins.Name))
                throw Error(method,ins.Offset,"Unsupported scheduled sequential opcode "+ins.Name+"; use the documented subset or the explicit legacy backend.");
            var code=list.ToDictionary(i=>i.Offset);var heights=new Dictionary<int,int>();
            var successors=new Dictionary<int,IReadOnlyList<int>>();
            foreach(var ins in list)
            {
                var targets=Successors(ins).ToArray();
                foreach(int target in targets)if(!code.ContainsKey(target))
                    throw Error(method,ins.Offset,"Invalid branch target "+target+"; target must be an instruction boundary.");
                successors.Add(ins.Offset,targets);
            }
            InstructionControlFlow flow;
            try { flow=InstructionControlFlow.Analyze(successors); }
            catch(ArgumentException error) { throw Error(method,0,error.Message); }
            var pending=new Queue<(int Offset,int Height)>();pending.Enqueue((0,0));int maxStack=0;
            while(pending.Count!=0)
            {
                var item=pending.Dequeue();
                if(heights.TryGetValue(item.Offset,out var previous))
                {if(previous!=item.Height)throw Error(method,item.Offset,"Inconsistent stack height.");continue;}
                if(!code.TryGetValue(item.Offset,out var ins))throw Error(method,item.Offset,"Invalid branch target.");
                int after=item.Height-PopCount(ins)+PushCount(ins);
                if(item.Height<PopCount(ins)||after>32||ins.Name=="ret"&&after!=0)
                    throw Error(method,ins.Offset,"Invalid/excessive evaluation stack.");
                heights.Add(item.Offset,item.Height);maxStack=Math.Max(maxStack,Math.Max(item.Height,after));
                foreach(int target in flow.Successors[ins.Offset])pending.Enqueue((target,after));
            }
            var b=new ScheduledMachineBuilder(name);var start=b.Input("start",1);
            var busy=b.Register("busy_state",1);var done=b.Register("done_state",1);
            var fault=b.Register("fault_state",1);var timeout=b.Register("timeout_state",1);
            var result=b.Register("result_state",returnType.PortWidth);
            int cw=1;while((1<<cw)<=o.MaxCycles)cw++;
            var cycles=b.Register("cycles",cw);
            var args=new ScheduledRegister[ps.Length];var captured=new List<(ScheduledRegister Register,ScheduledValue Input)>();
            var argumentTypes=new ScheduledScalarType[ps.Length];
            for(int i=0;i<ps.Length;i++)if(ps[i].ParameterType!=typeof(uint[]))
            {
                TryScheduledScalar(ps[i].ParameterType,out argumentTypes[i]);
                var input=b.Input("arg"+i,argumentTypes[i].PortWidth);
                args[i]=b.Register("saved_arg"+i,argumentTypes[i].StorageWidth);
                captured.Add((args[i],input.Resize(argumentTypes[i].StorageWidth)));
            }
            var localTypes=body.LocalVariables.Select(v=>{TryScheduledScalar(v.LocalType,out var type);return type;}).ToArray();
            var locals=localTypes.Select((type,i)=>b.Register("local"+i,type.StorageWidth)).ToArray();
            var stack=Enumerable.Range(0,maxStack).Select(i=>b.Register("stack"+i,32)).ToArray();
            var idle=b.State("idle");var reachable=list.Where(i=>heights.ContainsKey(i.Offset)).ToArray();
            var states=reachable.ToDictionary(i=>i.Offset,i=>b.State("IL_"+i.Offset.ToString("x4")));
            var waits=reachable.Where(i=>i.Name=="ldelem.u4"||i.Name=="ldelem.i4")
                .ToDictionary(i=>i.Offset,i=>b.State("wait_IL_"+i.Offset.ToString("x4")));
            var one=b.Constant(1,1);var zero=b.Constant(1,0);
            ScheduledValue K32(int value)=>b.Constant(32,unchecked((uint)value));
            var zero32=K32(0);int aw=1;while((1<<aw)<o.MemoryDepth)aw++;
            var ram=hasRam?b.Memory("data",32,o.MemoryDepth,o.InitialMemory):null;
            var readData=hasRam?b.Register("ram_read_data",32):null;
            var multiplier=b.ArithmeticResource("cil_multiplier",HardwareOperation.Multiply,32);
            var live=cycles.Value.LessThan(b.Constant(cw,o.MaxCycles));var expired=live.Not();
            void Stop(ScheduledState state,ScheduledValue condition,bool failed,bool timedOut=false)
            {
                state.Assign(busy,zero,condition).Assign(done,one,condition)
                    .Assign(fault,failed?one:zero,condition).Assign(timeout,timedOut?one:zero,condition)
                    .Transition("idle",condition);
            }
            var clear=new[]{done,fault,timeout,result,cycles}.Concat(locals).Concat(stack);
            foreach(var register in clear)idle.Assign(register,b.Constant(register.Width,0),start);
            // busy is separate because earlier guarded assignments have priority.
            idle.Assign(busy,one,start);
            foreach(var pair in captured)idle.Assign(pair.Register,pair.Input,start);
            idle.Transition(states[reachable[0].Offset].Name,start);
            ScheduledValue Compare(string test,ScheduledValue left,ScheduledValue right,bool unsigned)
                =>test=="eq"?left.Equal(right):test=="lt"?(unsigned?left.LessThan(right):left.SignedLessThan(right)):
                    (unsigned?right.LessThan(left):right.SignedLessThan(left));
            foreach(var ins in reachable)
            {
                var state=states[ins.Offset];int h=heights[ins.Offset];string op=ins.Name;
                ScheduledValue Top(int n)=>stack[h-1-n].Value;
                void Push(int slot,ScheduledValue value)=>state.Assign(stack[slot],value,live);
                Stop(state,expired,true,true);state.Assign(cycles,cycles.Value.Add(b.Constant(cw,1)),live);
                string next=op=="ret"||op=="br"||op=="br.s"?"idle":states[ins.Next].Name;
                if(op=="ret"){state.Assign(result,Top(0).Resize(returnType.PortWidth),live);Stop(state,live,false);continue;}
                if(op=="br"||op=="br.s"){state.Transition(states[(int)ins.Operand].Name,live);continue;}
                if(Conditional(op))
                {
                    ScheduledValue condition;
                    if(op.StartsWith("brtrue")||op.StartsWith("brfalse"))
                    {condition=Top(0).Equal(zero32);if(op.StartsWith("brtrue"))condition=condition.Not();}
                    else
                    {
                        string test=op.Substring(0,3);
                        condition=Compare(test=="beq"||test=="bne"?"eq":test=="blt"||test=="bge"?"lt":"gt",Top(1),Top(0),op.Contains(".un"));
                        if(test=="bne"||test=="ble"||test=="bge")condition=condition.Not();
                    }
                    state.Transition(states[(int)ins.Operand].Name,live.And(condition)).Transition(next,live);continue;
                }
                if(op=="switch")
                {int[] targets=(int[])ins.Operand;for(int i=0;i<targets.Length;i++)state.Transition(states[targets[i]].Name,live.And(Top(0).Equal(K32(i))));state.Transition(next,live);continue;}
                if(op=="ldelem.u4"||op=="ldelem.i4"||op=="stelem.i4")
                {
                    if(ram==null)throw Error(method,ins.Offset,"RAM access requires uint[] argument.");
                    bool write=op=="stelem.i4";var address=Top(write?1:0);var valid=address.LessThan(b.Constant(32,o.MemoryDepth));
                    Stop(state,live.And(valid.Not()),true);
                    if(write){state.Write(ram,address.Resize(aw),Top(0),live.And(valid));state.Transition(next,live.And(valid));}
                    else
                    {
                        state.Read(ram,address.Resize(aw),readData,live.And(valid)).Transition(waits[ins.Offset].Name,live.And(valid));
                        var wait=waits[ins.Offset];Stop(wait,expired,true,true);
                        wait.Assign(cycles,cycles.Value.Add(b.Constant(cw,1)),live).Assign(stack[h-2],readData.Value,live).Transition(next,live);
                    }
                    continue;
                }
                if(op.StartsWith("ldc.i4"))Push(h,K32(op=="ldc.i4.m1"?-1:op=="ldc.i4"||op=="ldc.i4.s"?(int)ins.Operand:int.Parse(op.Substring(7))));
                else if(op=="ldarg"||op=="ldarg.s"||op.StartsWith("ldarg."))
                {int index=VarIndex(ins,"ldarg");Push(h,args[index] is null?zero32:argumentTypes[index].Load(args[index].Value));}
                else if(op=="starg"||op=="starg.s")
                {int index=(int)ins.Operand;if(args[index]==null)throw Error(method,ins.Offset,"Cannot reassign RAM reference.");state.Assign(args[index],argumentTypes[index].Store(Top(0)),live);}
                else if(op=="ldloc"||op=="ldloc.s"||op.StartsWith("ldloc."))
                {int index=VarIndex(ins,"ldloc");Push(h,localTypes[index].Load(locals[index].Value));}
                else if(op=="stloc"||op=="stloc.s"||op.StartsWith("stloc."))
                {int index=VarIndex(ins,"stloc");state.Assign(locals[index],localTypes[index].Store(Top(0)),live);}
                else if(op=="dup")Push(h,Top(0));
                else if(op=="not")Push(h-1,Top(0).Not());
                else if(op=="neg")Push(h-1,zero32.Subtract(Top(0)));
                else if(op=="conv.i1"||op=="conv.u1"||op=="conv.i2"||op=="conv.u2")Push(h-1,ScheduledClrNarrow(Top(0),op));
                else if(op=="shl"||op=="shr"||op=="shr.un")Push(h-2,ScheduledClrShift(b,Top(1),Top(0),op));
                else if(op=="ldlen"){if(ram==null)throw Error(method,ins.Offset,"ldlen requires RAM.");Push(h-1,b.Constant(32,o.MemoryDepth));}
                else if(op=="ceq"||op.StartsWith("clt")||op.StartsWith("cgt"))Push(h-2,Compare(op.Substring(1,2),Top(1),Top(0),op.EndsWith(".un")).Resize(32));
                else if(op=="mul")Push(h-2,state.Compute(multiplier,Top(1),Top(0),live));
                else if(op=="add")Push(h-2,Top(1).Add(Top(0)));
                else if(op=="sub")Push(h-2,Top(1).Subtract(Top(0)));
                else if(op=="and")Push(h-2,Top(1).And(Top(0)));
                else if(op=="or")Push(h-2,Top(1).Or(Top(0)));
                else if(op=="xor")Push(h-2,Top(1).Xor(Top(0)));
                // nop/pop/32-bit conversions need no data movement.
                state.Transition(next,live);
            }
            b.Output("busy",busy.Value);b.Output("done",done.Value);b.Output("fault",fault.Value);
            b.Output("timeout",timeout.Value);b.Output("assertion_failed",zero);b.Output("result",result.Value);
            b.Output("accept",b.InState(idle).And(start));
            var offsets=states.ToDictionary(p=>p.Value.Name,p=>p.Key);
            foreach(var pair in waits)offsets.Add(pair.Value.Name,pair.Key);
            var encodings=new[]{idle}.Concat(states.Values).Concat(waits.Values).ToDictionary(s=>s.Name,s=>s.Encoding);
            return new ScheduledCilCompilation(b.Lower(),method,offsets,encodings,flow);
        }
    }
}
