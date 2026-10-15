using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.HLS;

/// <summary>
/// Conservative constant folding of canonical Core IR. Preserves node IDs,
/// widths and names so source maps and downstream port references stay valid.
/// This pass does not perform resource sharing or alter sequential boundaries.
/// </summary>
public static class HlsConstantFolder
{
    /// <summary>
    /// Return a newly validated graph with fully constant, side-effect-free
    /// expressions replaced by width-masked literals. RAM reads and registers
    /// are never folded. Original input graph is not modified.
    /// </summary>
    public static HardwareGraph Fold(HardwareGraph graph)
    {
        if(graph==null)throw new ArgumentNullException(nameof(graph));
        var constants=new Dictionary<int,BigInteger>();
        var result=new List<HardwareNode>();
        foreach(var node in graph.Nodes)
        {
            if(node.Operation==HardwareOperation.Constant)
            {
                constants.Add(node.Id,node.ConstantValue!.Value);
                result.Add(node);
                continue;
            }
            if(node.Operation==HardwareOperation.Input ||
               node.Operation==HardwareOperation.Register ||
               node.Operation==HardwareOperation.MemoryRead ||
               node.Operation==HardwareOperation.MemoryWrite ||
               node.Operands.Any(id=>!constants.ContainsKey(id)))
            {
                result.Add(node);
                continue;
            }
            var mask=(BigInteger.One<<node.Type.Width)-1;
            BigInteger V(int index)=>constants[node.Operands[index]];
            BigInteger Signed(int index)
            {
                var operand=graph.Nodes.First(n=>n.Id==node.Operands[index]);
                var bits=operand.Type.Width;
                var value=V(index);
                return (value&(BigInteger.One<<(bits-1)))!=0
                    ? value-(BigInteger.One<<bits):value;
            }
            BigInteger value;
            switch(node.Operation)
            {
                case HardwareOperation.Add:value=V(0)+V(1);break;
                case HardwareOperation.Subtract:value=V(0)-V(1);break;
                case HardwareOperation.Multiply:
                case HardwareOperation.DspMultiplyUnsigned:value=V(0)*V(1);break;
                case HardwareOperation.And:value=V(0)&V(1);break;
                case HardwareOperation.Or:value=V(0)|V(1);break;
                case HardwareOperation.Xor:value=V(0)^V(1);break;
                case HardwareOperation.Not:value=~V(0);break;
                case HardwareOperation.Select:value=V(0)!=0?V(1):V(2);break;
                case HardwareOperation.ShiftLeft:value=V(0)<<node.Offset;break;
                case HardwareOperation.ShiftRight:
                case HardwareOperation.Slice:value=V(0)>>node.Offset;break;
                case HardwareOperation.ArithmeticShiftRight:
                    value=Signed(0)>>node.Offset;break;
                case HardwareOperation.Resize:value=V(0);break;
                case HardwareOperation.SignExtend:value=Signed(0);break;
                case HardwareOperation.Concat:
                    var lowerWidth=graph.Nodes.First(n=>n.Id==node.Operands[1]).Type.Width;
                    value=(V(0)<<lowerWidth)|V(1);break;
                case HardwareOperation.Equal:value=V(0)==V(1)?1:0;break;
                case HardwareOperation.LessThan:value=V(0)<V(1)?1:0;break;
                case HardwareOperation.SignedLessThan:value=Signed(0)<Signed(1)?1:0;break;
                case HardwareOperation.Lookup:
                    value=V(0)<node.Table!.Count?node.Table[(int)V(0)]:0;break;
                default:
                    result.Add(node);
                    continue;
            }
            value &= mask;
            constants.Add(node.Id,value);
            result.Add(node with {
                Operation=HardwareOperation.Constant,
                Operands=Array.Empty<int>(),
                ConstantValue=value,
                Offset=0,
                Table=null,
                MemoryName=null
            });
        }
        return new HardwareGraph(result);
    }
}
