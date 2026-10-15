using System;
using System.Collections.Generic;
using System.Linq;
using SharpHDL.Core;
using SharpHDL.HLS;

namespace SharpHDL
{
    /// <summary>
    /// Compatibility bridge from a compiled Core-IR elastic pipeline to the
    /// existing RTL emitter. The HLS compiler itself has no legacy dependency.
    /// Once a Core-native RTL backend exists, this bridge can be retired.
    /// </summary>
    public static class HlsPipelineCompatibility
    {
        /// <summary>
        /// Materialize an independent HLS Core graph as a legacy Module for
        /// Verilog emission and existing RTL verification. All pipeline
        /// registers, next-state muxes and ready/valid control are preserved.
        /// </summary>
        public static Module ToModule(this HlsElasticPipeline compiled,string name)
        {
            if(compiled==null)throw new ArgumentNullException(nameof(compiled));
            var module=new Module(name);
            var expressions=new Dictionary<int,Expr>();
            var signals=new Dictionary<string,Signal>(StringComparer.Ordinal);
            foreach(var node in compiled.Graph.Nodes.Where(n=>n.Operation==HardwareOperation.Input))
            {
                if(string.IsNullOrWhiteSpace(node.Name))
                    throw new InvalidOperationException("Core pipeline input lacks a signal name.");
                Signal signal;
                if(compiled.ResetValues.TryGetValue(node.Name,out var reset))
                    signal=module.Register(node.Name,node.Type.Width,reset);
                else
                    signal=module.Input(node.Name,node.Type.Width);
                signals.Add(node.Name,signal);
                expressions.Add(node.Id,signal);
            }
            foreach(var node in compiled.Graph.Nodes)
            {
                if(expressions.ContainsKey(node.Id))continue;
                Expr A(int i)=>expressions[node.Operands[i]];
                Expr value;
                switch(node.Operation)
                {
                    case HardwareOperation.Constant:
                        value=Expr.Const(node.Type.Width,node.ConstantValue!.Value);break;
                    case HardwareOperation.Add:value=A(0)+A(1);break;
                    case HardwareOperation.Subtract:value=A(0)-A(1);break;
                    case HardwareOperation.Multiply:value=A(0)*A(1);break;
                    case HardwareOperation.And:value=A(0)&A(1);break;
                    case HardwareOperation.Or:value=A(0)|A(1);break;
                    case HardwareOperation.Xor:value=A(0)^A(1);break;
                    case HardwareOperation.Not:value=~A(0);break;
                    case HardwareOperation.Select:value=Expr.Mux(A(0),A(1),A(2));break;
                    case HardwareOperation.ShiftLeft:value=A(0)<<node.Offset;break;
                    case HardwareOperation.ShiftRight:value=A(0)>>node.Offset;break;
                    case HardwareOperation.ArithmeticShiftRight:
                        value=A(0).ArithmeticShift(node.Offset);break;
                    case HardwareOperation.Slice:
                        value=A(0).Slice(node.Offset+node.Type.Width-1,node.Offset);break;
                    case HardwareOperation.Resize:value=A(0).Resize(node.Type.Width);break;
                    case HardwareOperation.SignExtend:
                        value=A(0).SignExtend(node.Type.Width);break;
                    case HardwareOperation.Concat:value=Expr.Concat(A(0),A(1));break;
                    case HardwareOperation.Equal:value=A(0).Eq(A(1));break;
                    case HardwareOperation.LessThan:value=A(0).Lt(A(1));break;
                    case HardwareOperation.SignedLessThan:value=A(0).SignedLt(A(1));break;
                    case HardwareOperation.Lookup:
                        value=Expr.Lookup(node.Type.Width,A(0),node.Table!);break;
                    default:throw new NotSupportedException(
                        "Core-to-Verilog compatibility does not support "+node.Operation);
                }
                expressions.Add(node.Id,value);
            }
            foreach(var (port,id) in compiled.Outputs)
            {
                var output=module.Output(port,compiled.Graph.Nodes.First(n=>n.Id==id).Type.Width);
                module.Drive(output,expressions[id]);
            }
            foreach(var (register,id) in compiled.NextState)
                module.Next(signals[register],expressions[id]);
            module.Validate();
            return module;
        }
    }
}
