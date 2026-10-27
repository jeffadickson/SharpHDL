using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using SharpHDL.Core;

namespace SharpHDL.RTL;

/// <summary>
/// Independent Core-IR-to-Verilog backend. Does not depend on legacy Module,
/// the HLS compiler, or the Simulation engine. The first supported target is
/// a single-clock, two-state graph with combinational operators and registers.
/// </summary>
public static class CoreVerilogEmitter
{
    /// <summary>
    /// Emit synthesizable Verilog for a validated graph with named ports and
    /// synchronous active-high reset. The six-argument overload has no RAM
    /// metadata and rejects memory reads rather than guessing contents.
    /// Use the memory-port overload for synchronous RAM and constant ROM.
    /// Direct DSP primitives remain unsupported.
    /// </summary>
    public static string Emit(HardwareGraph graph,
        IReadOnlyDictionary<string,int> inputs,
        IReadOnlyDictionary<string,int> outputs,
        IReadOnlyDictionary<string,int> nextState,
        IReadOnlyDictionary<string,BigInteger> resetValues,
        string moduleName)
    {
        return Emit(graph,inputs,outputs,nextState,resetValues,
            new Dictionary<string,HardwareMemoryPort>(),moduleName);
    }

    /// <summary>
    /// Emit Core IR with initialized constant ROM and synchronous single-port
    /// RAM. RAM reads observe pre-edge contents; writes commit at the edge
    /// unless reset is asserted. Reset does not clear memory cells.
    /// </summary>
    public static string Emit(HardwareGraph graph,
        IReadOnlyDictionary<string,int> inputs,
        IReadOnlyDictionary<string,int> outputs,
        IReadOnlyDictionary<string,int> nextState,
        IReadOnlyDictionary<string,BigInteger> resetValues,
        IReadOnlyDictionary<string,HardwareMemoryPort> memories,
        string moduleName)
    {
        if(memories==null)throw new ArgumentNullException(nameof(memories));
        if(graph==null)throw new ArgumentNullException(nameof(graph));
        if(inputs==null)throw new ArgumentNullException(nameof(inputs));
        if(outputs==null)throw new ArgumentNullException(nameof(outputs));
        if(nextState==null)throw new ArgumentNullException(nameof(nextState));
        if(resetValues==null)throw new ArgumentNullException(nameof(resetValues));
        static void Identifier(string name)
        {
            if(string.IsNullOrWhiteSpace(name) ||
                !Regex.IsMatch(name,@"^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new ArgumentException("Invalid Verilog identifier: "+name);
        }
        Identifier(moduleName);
        var byId=graph.Nodes.ToDictionary(n=>n.Id);
        foreach(var name in inputs.Keys.Concat(outputs.Keys).Concat(nextState.Keys).Concat(memories.Keys))
            Identifier(name);
        foreach(var (name,id) in inputs)
            if(!byId.TryGetValue(id,out var n) || n.Operation!=HardwareOperation.Input)
                throw new ArgumentException("Invalid input node: "+name);
        var declaredInputIds=new HashSet<int>(inputs.Values);
        foreach(var node in graph.Nodes.Where(n=>n.Operation==HardwareOperation.Input))
            if(!declaredInputIds.Contains(node.Id))
                throw new ArgumentException("Undeclared Core IR input node "+node.Id);
        foreach(var (name,id) in nextState)
            if(inputs.TryGetValue(name,out var inputId) &&
                byId.ContainsKey(id) &&
                byId[inputId].Type.Width!=byId[id].Type.Width)
                throw new ArgumentException("Register next-state width mismatch: "+name);
        foreach(var id in outputs.Values.Concat(nextState.Values))
            if(!byId.ContainsKey(id))throw new ArgumentException("Unknown output/next-state node.");
        foreach(var name in resetValues.Keys)
            if(!inputs.ContainsKey(name) || !nextState.ContainsKey(name))
                throw new ArgumentException("Register must have both input and next-state: "+name);
        foreach(var name in nextState.Keys)
            if(!resetValues.ContainsKey(name))
                throw new ArgumentException("Next-state requires reset value: "+name);
        foreach(var (name,port) in memories)
        {
            if(port.Name!=name)throw new ArgumentException("Memory name mismatch: "+name);
            if(!byId.TryGetValue(port.AddressNode,out var addr) ||
                !byId.TryGetValue(port.WriteEnableNode,out var wen) ||
                !byId.TryGetValue(port.WriteDataNode,out var data) ||
                wen.Type.Width!=1 || data.Type.Width!=port.Width ||
                port.Initial.Count > (BigInteger.One<<addr.Type.Width))
                throw new ArgumentException("Invalid memory port: "+name);
        }
        foreach(var node in graph.Nodes.Where(n=>n.Operation==HardwareOperation.MemoryRead))
            if(node.MemoryName==null ||
                !memories.TryGetValue(node.MemoryName,out var ram) ||
                ram.Width!=node.Type.Width ||
                node.Operands.Count!=1 ||
                byId[node.Operands[0]].Type.Width != byId[ram.AddressNode].Type.Width)
                throw new ArgumentException("Missing or mismatched RAM for node "+node.Id);
        if(inputs.Keys.Intersect(outputs.Keys).Any())
            throw new ArgumentException("Input and output names must not overlap.");

        static string Range(int width)=>"["+(width-1)+":0]";
        static string Port(string name)=>"s_"+name;
        static string Node(int id)=>"n_"+id;
        static string Literal(int width,BigInteger value)=>width+"'d"+value;
        var sb=new StringBuilder();
        sb.AppendLine("// Generated by SharpHDL.RTL from validated Core IR.");
        sb.AppendLine("// Single rising-edge clock; synchronous active-high reset.");
        sb.AppendLine("module h_"+moduleName+"(");
        var ports=new List<string>{"input wire clk","input wire rst"};
        foreach(var (name,id) in inputs)
            if(!resetValues.ContainsKey(name))
                ports.Add("input wire "+Range(byId[id].Type.Width)+" "+Port(name));
        foreach(var (name,id) in outputs)
            ports.Add("output wire "+Range(byId[id].Type.Width)+" "+Port(name));
        sb.AppendLine("  "+string.Join(",\n  ",ports));
        sb.AppendLine(");");
        foreach(var (name,id) in inputs)
            if(resetValues.ContainsKey(name))
                sb.AppendLine("reg "+Range(byId[id].Type.Width)+" "+Port(name)+";");
        foreach(var (name,port) in memories)
        {
            sb.AppendLine("// "+name+": initialized synchronous read-before-write RAM.");
            string attribute=port.Implementation switch
            {
                HardwareMemoryImplementation.XilinxBlock=>"(* ram_style = \"block\" *)",
                HardwareMemoryImplementation.XilinxDistributed=>"(* ram_style = \"distributed\" *)",
                HardwareMemoryImplementation.IntelM10K=>"(* ramstyle = \"M10K\" *)",
                HardwareMemoryImplementation.IntelM20K=>"(* ramstyle = \"M20K\" *)",
                _=>""
            };
            if(attribute.Length!=0)sb.AppendLine(attribute);
            sb.AppendLine("reg "+Range(port.Width)+" mem_"+name+
                " [0:"+(port.Initial.Count-1)+"];");
            sb.AppendLine("initial begin");
            for(int i=0;i<port.Initial.Count;i++)
                sb.AppendLine("  mem_"+name+"["+i+"] = "+
                    Literal(port.Width,port.Initial[i])+";");
            sb.AppendLine("end");
        }
        foreach(var n in graph.Nodes)
        {
            if(n.Operation==HardwareOperation.Input)continue;
            if(n.Operation is HardwareOperation.MemoryWrite
                or HardwareOperation.Register or HardwareOperation.DspMultiplyUnsigned)
                throw new NotSupportedException("Native Core RTL backend does not yet support "+n.Operation);
            string A(int i)=>Node(n.Operands[i]);
            string rhs;
            switch(n.Operation)
            {
                case HardwareOperation.Constant:
                    rhs=Literal(n.Type.Width,n.ConstantValue!.Value);break;
                case HardwareOperation.Add:rhs=A(0)+" + "+A(1);break;
                case HardwareOperation.Subtract:rhs=A(0)+" - "+A(1);break;
                case HardwareOperation.Multiply:rhs=A(0)+" * "+A(1);break;
                case HardwareOperation.And:rhs=A(0)+" & "+A(1);break;
                case HardwareOperation.Or:rhs=A(0)+" | "+A(1);break;
                case HardwareOperation.Xor:rhs=A(0)+" ^ "+A(1);break;
                case HardwareOperation.Not:rhs="~"+A(0);break;
                case HardwareOperation.Select:rhs=A(0)+" ? "+A(1)+" : "+A(2);break;
                case HardwareOperation.ShiftLeft:rhs=A(0)+" << "+n.Offset;break;
                case HardwareOperation.ShiftRight:rhs=A(0)+" >> "+n.Offset;break;
                case HardwareOperation.ArithmeticShiftRight:
                    rhs="$signed("+A(0)+") >>> "+n.Offset;break;
                case HardwareOperation.Slice:rhs=A(0)+" >> "+n.Offset;break;
                case HardwareOperation.Resize:rhs=A(0);break;
                case HardwareOperation.SignExtend:rhs="$signed("+A(0)+")";break;
                case HardwareOperation.Concat:rhs="{"+A(0)+", "+A(1)+"}";break;
                case HardwareOperation.Equal:rhs=A(0)+" == "+A(1);break;
                case HardwareOperation.LessThan:rhs=A(0)+" < "+A(1);break;
                case HardwareOperation.SignedLessThan:
                    rhs="$signed("+A(0)+") < $signed("+A(1)+")";break;
                case HardwareOperation.Lookup:
                    {
                        var function="rom_"+Node(n.Id);
                        var addressWidth=byId[n.Operands[0]].Type.Width;
                        sb.AppendLine("function "+Range(n.Type.Width)+" "+function+";");
                        sb.AppendLine("  input "+Range(addressWidth)+" address;");
                        sb.AppendLine("  begin");
                        sb.AppendLine("    case (address)");
                        for(int i=0;i<n.Table!.Count;i++)
                            sb.AppendLine("      "+Literal(addressWidth,i)+": "+
                                function+" = "+Literal(n.Type.Width,n.Table[i])+";");
                        sb.AppendLine("      default: "+function+" = "+
                            Literal(n.Type.Width,0)+";");
                        sb.AppendLine("    endcase");
                        sb.AppendLine("  end");
                        sb.AppendLine("endfunction");
                        rhs=function+"("+A(0)+")";
                        break;
                    }
                case HardwareOperation.MemoryRead:
                    {
                        var ram=memories[n.MemoryName!];
                        var addrWidth=byId[n.Operands[0]].Type.Width;
                        rhs="({1'b0,"+A(0)+"} < "+
                            Literal(addrWidth+1,ram.Initial.Count)+") ? mem_"+
                            n.MemoryName+"["+A(0)+"] : "+Literal(n.Type.Width,0);
                        break;
                    }
                default:throw new NotSupportedException(
                    "Native Core RTL backend does not yet support "+n.Operation);
            }
            sb.AppendLine("wire "+Range(n.Type.Width)+" "+Node(n.Id)+";");
            sb.AppendLine("assign "+Node(n.Id)+" = "+rhs+";");
        }
        foreach(var (name,id) in inputs)
            sb.AppendLine("wire "+Range(byId[id].Type.Width)+" "+Node(id)+
                " = "+Port(name)+";");
        foreach(var (name,id) in outputs)
            sb.AppendLine("assign "+Port(name)+" = "+Node(id)+";");
        foreach(var (name,port) in memories)
        {
            var addrWidth=byId[port.AddressNode].Type.Width;
            sb.AppendLine("always @(posedge clk) begin");
            sb.AppendLine("  if (!rst && "+Node(port.WriteEnableNode)+
                " && ({1'b0,"+Node(port.AddressNode)+"} < "+
                Literal(addrWidth+1,port.Initial.Count)+"))");
            sb.AppendLine("    mem_"+name+"["+Node(port.AddressNode)+
                "] <= "+Node(port.WriteDataNode)+";");
            sb.AppendLine("end");
        }
        if(resetValues.Count>0)
        {
            sb.AppendLine("always @(posedge clk) begin");
            sb.AppendLine("  if (rst) begin");
            foreach(var (name,reset) in resetValues)
            {
                var width=byId[inputs[name]].Type.Width;
                if(reset<0 || reset >= (BigInteger.One<<width))
                    throw new ArgumentException("Invalid reset value for "+name);
                sb.AppendLine("    "+Port(name)+" <= "+Literal(width,reset)+";");
            }
            sb.AppendLine("  end else begin");
            foreach(var (name,id) in nextState)
                sb.AppendLine("    "+Port(name)+" <= "+Node(id)+";");
            sb.AppendLine("  end");
            sb.AppendLine("end");
        }
        sb.AppendLine("endmodule");
        return sb.ToString();
    }
}
