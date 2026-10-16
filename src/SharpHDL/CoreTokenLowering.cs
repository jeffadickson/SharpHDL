using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL
{
    /// <summary>
    /// Bridges the production CIL frontend's stateless token expression into
    /// canonical Core IR without adding a synthetic output port to Module.
    /// Only the supplied token signal may appear in the expression cone.
    /// </summary>
    internal static class CoreTokenLowering
    {
        internal static (HardwareGraph Graph,int InputId,int OutputId) Lower(Expr value,Signal token)
        {
            if(value==null||token==null)throw new ArgumentNullException(value==null?nameof(value):nameof(token));
            var nodes=new List<HardwareNode>();
            var ids=new Dictionary<Expr,int>();
            int Visit(Expr e)
            {
                if(ids.TryGetValue(e,out var known))return known;
                if(nodes.Count>=4096)throw new NotSupportedException("Core token graph exceeds 4096 nodes.");
                if(e is Signal && !ReferenceEquals(e,token))
                    throw new NotSupportedException("Stateless Core HLS only permits the current input token.");
                HardwareOperation op;
                switch(e.Op)
                {
                    case "signal":op=HardwareOperation.Input;break;
                    case "const":op=HardwareOperation.Constant;break;
                    case "+":op=HardwareOperation.Add;break;
                    case "-":op=HardwareOperation.Subtract;break;
                    case "*":op=HardwareOperation.Multiply;break;
                    case "&":op=HardwareOperation.And;break;
                    case "|":op=HardwareOperation.Or;break;
                    case "^":op=HardwareOperation.Xor;break;
                    case "~":op=HardwareOperation.Not;break;
                    case "mux":op=HardwareOperation.Select;break;
                    case "<<":op=HardwareOperation.ShiftLeft;break;
                    case ">>":op=HardwareOperation.ShiftRight;break;
                    case "asr":op=HardwareOperation.ArithmeticShiftRight;break;
                    case "slice":op=HardwareOperation.Slice;break;
                    case "resize":op=HardwareOperation.Resize;break;
                    case "signextend":op=HardwareOperation.SignExtend;break;
                    case "concat":op=HardwareOperation.Concat;break;
                    case "==":op=HardwareOperation.Equal;break;
                    case "<":op=HardwareOperation.LessThan;break;
                    case "slt":op=HardwareOperation.SignedLessThan;break;
                    default:throw new NotSupportedException("Core HLS token operation unsupported: "+e.Op);
                }
                var args=e.Args.Select(Visit).ToArray();
                int id=nodes.Count;
                nodes.Add(new HardwareNode(id,op,new HardwareType(e.Width),args,
                    op==HardwareOperation.Constant?e.Literal:null,
                    e==token?token.Name:e.NameHint,e.Offset));
                ids.Add(e,id);
                return id;
            }
            int output=Visit(value);
            int input=Visit(token);
            return (new HardwareGraph(nodes),input,output);
        }
    }
}
