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
        DspPreferred,
        /// <summary>Unsigned radix-4 Booth recoding with balanced sum of signed partial products.</summary>
        BoothRadix4,
        /// <summary>Unsigned carry-save 3:2 compressor tree followed by one carry-propagate add.</summary>
        WallaceTree
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
        static Expr ShiftAdd(Expr a,Expr b,int width)
        {
            var partial=new List<Expr>();
            for(int bit=0;bit<Math.Min(width,b.Width);bit++)
                partial.Add(Expr.Mux(b.Slice(bit,bit),
                    a.Resize(width)<<bit,Expr.Const(width,0)));
            return BalancedSum(partial,width);
        }

        static Expr BalancedSum(List<Expr> partial,int width)
        {
            if(partial.Count==0)return Expr.Const(width,0);
            while(partial.Count>1)
            {
                var next=new List<Expr>();
                for(int i=0;i<partial.Count;i+=2)
                    next.Add(i+1<partial.Count?partial[i]+partial[i+1]:partial[i]);
                partial=next;
            }
            return partial[0];
        }

        static Expr BitOrZero(Expr x,int bit)
            =>bit<0||bit>=x.Width?Expr.Const(1,0):x.Slice(bit,bit);

        static Expr Booth(Expr a,Expr b,int width)
        {
            // Radix-4 Booth digits of a zero-extended unsigned multiplier.
            // One extra top group handles a high unsigned sign bit correctly.
            var partial=new List<Expr>();
            var operand=a.Resize(width);
            var zero=Expr.Const(width,0);
            for(int group=0;group<(Math.Min(width,b.Width)+2)/2;group++)
            {
                int shift=2*group;
                var hi=BitOrZero(b,shift+1);
                var mid=BitOrZero(b,shift);
                var low=BitOrZero(b,shift-1);
                // Simpler canonical radix-4 digit selection:
                // 001,010 -> +1; 011 -> +2; 100 -> -2; 101,110 -> -1.
                var code=Expr.Concat(hi,Expr.Concat(mid,low));
                var plusOne=code.Eq(Expr.Const(3,1)) | code.Eq(Expr.Const(3,2));
                var plusTwo=code.Eq(Expr.Const(3,3));
                var minusOne=code.Eq(Expr.Const(3,5)) | code.Eq(Expr.Const(3,6));
                var minusTwo=code.Eq(Expr.Const(3,4));
                var oneValue=Expr.Mux(plusOne,operand,
                    Expr.Mux(minusOne,zero-operand,zero));
                var twoValue=Expr.Mux(plusTwo,operand<<1,
                    Expr.Mux(minusTwo,zero-(operand<<1),zero));
                partial.Add((oneValue+twoValue)<<shift);
            }
            return BalancedSum(partial,width);
        }

        static Expr Wallace(Expr a,Expr b,int width)
        {
            // 3:2 carry-save compression, modulo 2^width. The last
            // two rows are summed with a single carry-propagate adder.
            var rows=new List<Expr>();
            for(int bit=0;bit<Math.Min(width,b.Width);bit++)
                rows.Add(Expr.Mux(b.Slice(bit,bit),
                    a.Resize(width)<<bit,Expr.Const(width,0)));
            while(rows.Count>2)
            {
                var next=new List<Expr>();
                for(int i=0;i<rows.Count;i+=3)
                {
                    if(i+2>=rows.Count)
                    {
                        for(int j=i;j<rows.Count;j++)next.Add(rows[j]);
                        break;
                    }
                    var x=rows[i];var y=rows[i+1];var z=rows[i+2];
                    next.Add(x ^ y ^ z);
                    next.Add(((x & y) | (x & z) | (y & z))<<1);
                }
                rows=next;
            }
            return BalancedSum(rows,width);
        }

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
                if(e.Op=="*" && (implementation==MultiplierImplementation.ShiftAdd ||
                   implementation==MultiplierImplementation.BoothRadix4 ||
                   implementation==MultiplierImplementation.WallaceTree))
                {
                    if(e.Width>32)
                        throw new NotSupportedException("ShiftAdd multiplier is limited to 32-bit results; select Operator or DspPreferred for wider multiplication.");
                    result=implementation==MultiplierImplementation.BoothRadix4
                        ? Booth(args[0],args[1],e.Width)
                        : implementation==MultiplierImplementation.WallaceTree
                        ? Wallace(args[0],args[1],e.Width)
                        : ShiftAdd(args[0],args[1],e.Width);
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
