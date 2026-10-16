using System;
using System.Collections.Generic;
using System.Linq;
using SharpHDL.Core;
using SharpHDL.HLS;

namespace SharpHDL
{
    /// <summary>
    /// Instantiates an independently compiled Core elastic pipeline directly
    /// into the original Module and its existing ready/valid stream ports.
    /// The compatibility layer does not reschedule any operation.
    /// </summary>
    internal static class CorePipelineAttach
    {
        internal static Signal[] Attach(Module module,HwChannel input,HwChannel output,
            HlsElasticPipeline compiled,string prefix)
        {
            if(module==null||input==null||output==null||compiled==null)
                throw new ArgumentNullException(nameof(module));
            var expressions=new Dictionary<int,Expr>();
            var state=new Dictionary<string,Signal>(StringComparer.Ordinal);
            var bound=new Dictionary<string,Signal>(StringComparer.Ordinal)
            {
                [prefix+"_input_data"]=input.Data,
                [prefix+"_input_valid"]=input.Valid,
                [prefix+"_output_ready"]=output.Ready
            };
            foreach(var node in compiled.Graph.Nodes.Where(n=>n.Operation==HardwareOperation.Input))
            {
                if(string.IsNullOrWhiteSpace(node.Name))
                    throw new InvalidOperationException("Core IR input lacks name.");
                Signal signal;
                if(bound.TryGetValue(node.Name,out var external))
                    signal=external;
                else if(compiled.ResetValues.TryGetValue(node.Name,out var reset))
                {
                    signal=module.Register(node.Name,node.Type.Width,reset);
                    state.Add(node.Name,signal);
                }
                else throw new InvalidOperationException("Unexpected Core HLS input "+node.Name);
                if(signal.Width!=node.Type.Width)
                    throw new InvalidOperationException("Core HLS input width mismatch "+node.Name);
                expressions.Add(node.Id,signal);
            }
            foreach(var node in compiled.Graph.Nodes)
            {
                if(expressions.ContainsKey(node.Id))continue;
                Expr A(int i)=>expressions[node.Operands[i]];
                Expr result;
                switch(node.Operation)
                {
                    case HardwareOperation.Constant:
                        result=Expr.Const(node.Type.Width,node.ConstantValue!.Value);break;
                    case HardwareOperation.Add:result=A(0)+A(1);break;
                    case HardwareOperation.Subtract:result=A(0)-A(1);break;
                    case HardwareOperation.Multiply:result=A(0)*A(1);break;
                    case HardwareOperation.And:result=A(0)&A(1);break;
                    case HardwareOperation.Or:result=A(0)|A(1);break;
                    case HardwareOperation.Xor:result=A(0)^A(1);break;
                    case HardwareOperation.Not:result=~A(0);break;
                    case HardwareOperation.Select:result=Expr.Mux(A(0),A(1),A(2));break;
                    case HardwareOperation.ShiftLeft:result=A(0)<<node.Offset;break;
                    case HardwareOperation.ShiftRight:result=A(0)>>node.Offset;break;
                    case HardwareOperation.ArithmeticShiftRight:result=A(0).ArithmeticShift(node.Offset);break;
                    case HardwareOperation.Slice:
                        result=A(0).Slice(node.Offset+node.Type.Width-1,node.Offset);break;
                    case HardwareOperation.Resize:result=A(0).Resize(node.Type.Width);break;
                    case HardwareOperation.SignExtend:
                        result=A(0).SignExtend(node.Type.Width);break;
                    case HardwareOperation.Concat:result=Expr.Concat(A(0),A(1));break;
                    case HardwareOperation.Equal:result=A(0).Eq(A(1));break;
                    case HardwareOperation.LessThan:result=A(0).Lt(A(1));break;
                    case HardwareOperation.SignedLessThan:result=A(0).SignedLt(A(1));break;
                    default:throw new NotSupportedException("Unsupported Core HLS attach op "+node.Operation);
                }
                expressions.Add(node.Id,result);
            }
            foreach(var (name,id) in compiled.NextState)
                module.Next(state[name],expressions[id]);
            module.Drive(input.Ready,expressions[compiled.Outputs[prefix+"_input_ready"]]);
            module.Drive(output.Valid,expressions[compiled.Outputs[prefix+"_output_valid"]]);
            module.Drive(output.Data,expressions[compiled.Outputs[prefix+"_output_data"]]);
            return Enumerable.Range(0,compiled.Latency)
                .Select(i=>state[prefix+"_valid_"+i]).ToArray();
        }
    }
}
