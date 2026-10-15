using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpHDL
{
    /// <summary>
    /// Selects the *combinational* implementation of multiplication in an HLS
    /// stream pipeline. This is not a promise of a particular physical DSP count.
    /// </summary>
    public enum MultiplierImplementation
    {
        /// <summary>Use the ordinary Verilog multiply operator.</summary>
        Auto,
        /// <summary>Keep an ordinary Verilog multiply operator, with no DSP hint.</summary>
        Operator,
        /// <summary>Use a balanced combinational shift/add partial-product tree.</summary>
        ShiftAdd,
        /// <summary>Emit a vendor-specific DSP preference on the multiply net.</summary>
        DspPreferred
    }

    /// <summary>
    /// Method-level HLS synthesis directive, analogous to a pragma.
    /// Applies to multiplication nodes in the annotated pipeline method.
    /// An explicit PipelineOptions.MultiplierImplementation overrides it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method,AllowMultiple=false)]
    public sealed class HlsMultiplierAttribute : Attribute
    {
        /// <summary>The requested implementation.</summary>
        public MultiplierImplementation Implementation { get; }
        public HlsMultiplierAttribute(MultiplierImplementation implementation)
        {
            if(!Enum.IsDefined(typeof(MultiplierImplementation),implementation))
                throw new ArgumentOutOfRangeException(nameof(implementation));
            Implementation=implementation;
        }
    }

    internal static class MultiplierLowering
    {
        /// <summary>
        /// Rewrites a symbolic expression DAG before pipeline scheduling.
        /// ShiftAdd implements modulo-2^width unsigned multiplication.
        /// All partial products are combinational; the existing elastic
        /// scheduler determines the register boundaries.
        /// </summary>
        internal static Expr Lower(Expr root,MultiplierImplementation implementation)
        {
            if(implementation==MultiplierImplementation.Auto ||
               implementation==MultiplierImplementation.Operator)return root;
            if(!Enum.IsDefined(typeof(MultiplierImplementation),implementation))
                throw new ArgumentOutOfRangeException(nameof(implementation));
            var cache=new Dictionary<Expr,Expr>();
            Func<Expr,Expr> visit=null;
            visit=e=>
            {
                Expr existing;
                if(cache.TryGetValue(e,out existing))return existing;
                if(e is Signal || e.Op=="const")return cache[e]=e;
                var args=e.Args.Select(visit).ToArray();
                Expr result;
                if(e.Op=="*" && implementation==MultiplierImplementation.ShiftAdd)
                {
                    if(e.Width>32)
                        throw new NotSupportedException("ShiftAdd multiplier is limited to 32-bit results; select Operator or DspPreferred for wider multiplication.");
                    var partial=new List<Expr>();
                    for(int bit=0;bit<Math.Min(e.Width,args[1].Width);bit++)
                    {
                        var selected=Expr.Mux(args[1].Slice(bit,bit),
                            args[0].Resize(e.Width)<<bit,Expr.Const(e.Width,0));
                        partial.Add(selected);
                    }
                    while(partial.Count>1)
                    {
                        var next=new List<Expr>();
                        for(int i=0;i<partial.Count;i+=2)
                            next.Add(i+1<partial.Count?partial[i]+partial[i+1]:partial[i]);
                        partial=next;
                    }
                    result=partial[0];
                }
                else
                {
                    result=args.SequenceEqual(e.Args)?e:new Expr(e.Width,e.Op,args)
                    { Literal=e.Literal,Offset=e.Offset,Table=e.Table,Memory=e.Memory };
                    if(e.Op=="*" && implementation==MultiplierImplementation.DspPreferred)
                    {
                        if(ReferenceEquals(result,e))
                            result=new Expr(e.Width,e.Op,args){Literal=e.Literal,Offset=e.Offset};
                        result.MultiplierHint="yes";
                    }
                }
                if(!ReferenceEquals(result,e))
                {
                    if(e.NameHint!=null)result.Named(e.NameHint);
                    if(e.Description!=null)result.Describe(e.Description);
                }
                return cache[e]=result;
            };
            return visit(root);
        }
    }
}
