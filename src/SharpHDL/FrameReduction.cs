using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>Explicit widths and packed result layout for signed frame statistics.</summary>
    public sealed class FrameReductionOptions
    {
        public int SampleWidth { get; private set; }
        public int SumWidth { get; private set; }
        public int EnergyWidth { get; private set; }
        public int CountWidth { get; private set; }
        public int EnergyOffset { get { return SumWidth; } }
        public int CountOffset { get { return EnergyOffset+EnergyWidth; } }
        public int MinimumOffset { get { return CountOffset+CountWidth; } }
        public int MaximumOffset { get { return MinimumOffset+SampleWidth; } }
        public int FlagsOffset { get { return MaximumOffset+SampleWidth; } }
        public int ResultWidth { get { return ((FlagsOffset+8+7)/8)*8; } }
        public FrameReductionOptions(int sampleWidth=16,int sumWidth=64,int energyWidth=64,int countWidth=32)
        {
            if(sampleWidth<8||sampleWidth>64||sampleWidth%8!=0)throw new ArgumentException("Samples must be byte-aligned, 8..64 bits.");
            if(sumWidth<sampleWidth||sumWidth>256||energyWidth<2*sampleWidth||energyWidth>256||countWidth<1||countWidth>64)
                throw new ArgumentException("Sum width must be sampleWidth..256; energy width 2*sampleWidth..256; count width 1..64.");
            SampleWidth=sampleWidth;SumWidth=sumWidth;EnergyWidth=energyWidth;CountWidth=countWidth;
        }
        public string Layout {
            get { return "Frame result: signed SUM ["+(SumWidth-1)+":0], unsigned ENERGY ["+(CountOffset-1)+":"+EnergyOffset+
                "], saturating COUNT ["+(MinimumOffset-1)+":"+CountOffset+"], signed MIN ["+(MaximumOffset-1)+":"+MinimumOffset+
                "], signed MAX ["+(FlagsOffset-1)+":"+MaximumOffset+"], FLAGS ["+(FlagsOffset+7)+":"+FlagsOffset+
                "]; flag bits 0=empty, 1=sum overflow, 2=energy overflow, 3=count overflow, 4=invalid sample; other flags and high padding zero."; }
        }
        public FrameStatistics Decode(AxisBeat result) {
            if(result.Data<0||result.Data>Expr.Mask(ResultWidth))throw new ArgumentException("Result data width mismatch.");
            if(result.Keep!=Expr.Mask(ResultWidth/8)||!result.Last||result.User<0)
                throw new ArgumentException("A frame result must be a complete LAST beat with nonnegative metadata.");
            if((result.Data>>(FlagsOffset+5))!=0)throw new ArgumentException("Reserved result flags and padding must be zero.");
            var bits=result.Data;
            return new FrameStatistics(Expr.SignedValue(bits,SumWidth),
                (bits>>EnergyOffset)&Expr.Mask(EnergyWidth),(bits>>CountOffset)&Expr.Mask(CountWidth),
                Expr.SignedValue(bits>>MinimumOffset,SampleWidth),Expr.SignedValue(bits>>MaximumOffset,SampleWidth),
                (int)((bits>>FlagsOffset)&255),result.User);
        }
        internal AxisBeat Encode(FrameStatistics r) {
            var bits=(r.Sum&Expr.Mask(SumWidth))|(r.Energy<<EnergyOffset)|(r.Count<<CountOffset)|
                ((r.Minimum&Expr.Mask(SampleWidth))<<MinimumOffset)|((r.Maximum&Expr.Mask(SampleWidth))<<MaximumOffset)|
                ((BigInteger)r.Flags<<FlagsOffset);
            return new AxisBeat(bits,Expr.Mask(ResultWidth/8),true,r.User);
        }
    }
    public struct FrameStatistics
    {
        public BigInteger Sum { get; private set; }
        public BigInteger Energy { get; private set; }
        public BigInteger Count { get; private set; }
        public BigInteger Minimum { get; private set; }
        public BigInteger Maximum { get; private set; }
        public int Flags { get; private set; }
        public BigInteger User { get; private set; }
        public bool Empty { get { return (Flags&1)!=0; } }
        public bool SumOverflow { get { return (Flags&2)!=0; } }
        public bool EnergyOverflow { get { return (Flags&4)!=0; } }
        public bool CountOverflow { get { return (Flags&8)!=0; } }
        public bool InvalidSample { get { return (Flags&16)!=0; } }
        internal FrameStatistics(BigInteger sum,BigInteger energy,BigInteger count,BigInteger min,BigInteger max,int flags,BigInteger user)
        { Sum=sum;Energy=energy;Count=count;Minimum=min;Maximum=max;Flags=flags;User=user; }
    }
    /// <summary>Native functional frame state. Reset explicitly discards an incomplete frame.
    /// Push returns a packed result only on LAST, even when that closing sample is invalid.</summary>
    public sealed class NativeFrameReducer
    {
        readonly FrameReductionOptions options;
        readonly AxisFormat format;
        BigInteger sum,energy,count,min,max;
        int flags;
        bool any;
        public bool InFrame { get; private set; }
        public NativeFrameReducer(FrameReductionOptions options,int userWidth=1) {
            this.options=options??throw new ArgumentNullException(nameof(options));format=new AxisFormat(options.SampleWidth,userWidth);
        }
        public void Reset() {sum=energy=count=min=max=0;flags=0;any=false;InFrame=false;}
        public AxisBeat? Push(AxisBeat beat) {
            format.Check(beat);InFrame=true;
            if(beat.Keep==Expr.Mask(options.SampleWidth/8)) {
                var x=Expr.SignedValue(beat.Data,options.SampleWidth);
                var nextSum=sum+x;var bound=BigInteger.One<<(options.SumWidth-1);
                if(nextSum< -bound||nextSum>=bound)flags|=2;
                sum=Expr.SignedValue(nextSum,options.SumWidth);
                var nextEnergy=energy+x*x;
                if(nextEnergy>Expr.Mask(options.EnergyWidth))flags|=4;
                energy=nextEnergy&Expr.Mask(options.EnergyWidth);
                if(count==Expr.Mask(options.CountWidth))flags|=8;else count++;
                if(!any||x<min)min=x;if(!any||x>max)max=x;any=true;
            } else flags|=16;
            if(!beat.Last)return null;
            var result=options.Encode(new FrameStatistics(sum,energy,count,min,max,flags|(!any?1:0),beat.User));
            Reset();return result;
        }
        /// <summary>Each enumeration has fresh state. Incomplete trailing frames are rejected.</summary>
        public static IEnumerable<AxisBeat> Reduce(IEnumerable<AxisBeat> input,FrameReductionOptions options,int userWidth=1) {
            if(input==null)throw new ArgumentNullException(nameof(input));
            var reducer=new NativeFrameReducer(options,userWidth);
            foreach(var beat in input) {var result=reducer.Push(beat);if(result.HasValue)yield return result.Value;}
            if(reducer.InFrame)throw new InvalidOperationException("Input ended before TLAST; incomplete frame has no result.");
        }
    }
    public static class FrameReduction
    {
        /// <summary>One result per TLAST. Complete samples contribute; invalid samples still close frames.
        /// Signed sum and unsigned energy wrap with sticky overflow; count saturates with sticky overflow.
        /// Output tag is the closing beat's USER. No automatic grouping by tag.</summary>
        public static Stream Build(Module m,Stream input,string name,FrameReductionOptions options)
        {
            if(m==null||input==null||options==null)throw new ArgumentNullException();
            if(input.Width!=options.SampleWidth)throw new ArgumentException("Input sample width mismatch.");
            int sw=options.SumWidth,ew=options.EnergyWidth,cw=options.CountWidth,w=options.SampleWidth;
            var output=Stream.Internal(m,name,options.ResultWidth,input.UserWidth);
            var sum=m.Register(name+"_sum",sw,0);var energy=m.Register(name+"_energy",ew,0);
            var count=m.Register(name+"_count",cw,0);var min=m.Register(name+"_minimum",w,0);var max=m.Register(name+"_maximum",w,0);
            var any=m.Register(name+"_has_sample",1,0);var flags=m.Register(name+"_flags",5,0);
            var valid=m.Register(name+"_result_valid",1,0);var data=m.Register(name+"_result",options.ResultWidth,0);
            var user=m.Register(name+"_result_user",input.UserWidth,0);
            data.Describe(options.Layout);output.Data.Describe(options.Layout);
            sum.Describe("Signed frame sum; wraps at declared width, with sticky overflow flag.");
            energy.Describe("Unsigned sum of sample squares; wraps at declared width, with sticky overflow flag.");
            count.Describe("Number of complete samples; saturates at all ones, with sticky overflow flag.");
            flags.Describe("Sticky frame flags: bit1 signed-sum overflow, bit2 energy overflow, bit3 count overflow, bit4 invalid sample; bit0 computed at frame close.");
            user.Describe("TUSER from the accepted closing (TLAST) beat. Frames are not grouped by tag.");
            var available=~valid|output.Ready;
            m.Drive(input.Ready,available);
            var fire=input.Fire;var close=fire&input.Last;
            var complete=input.Keep.Eq(Expr.Const(input.Keep.Width,Expr.Mask(input.Keep.Width)));
            var sample=input.Data;
            var extendedSum=sum.SignExtend(sw+1)+sample.SignExtend(sw+1);
            var sumValue=extendedSum.Resize(sw);
            var sumOverflow=extendedSum.Slice(sw,sw)^extendedSum.Slice(sw-1,sw-1);
            var wide=sample.SignExtend(ew);var square=wide*wide;
            var extendedEnergy=energy.Resize(ew+1)+square.Resize(ew+1);
            var countFull=count.Eq(Expr.Const(cw,Expr.Mask(cw)));
            var nextSum=Expr.Mux(complete,sumValue,sum);
            var nextEnergy=Expr.Mux(complete,extendedEnergy.Resize(ew),energy);
            var nextCount=Expr.Mux(complete&~countFull,count+Expr.Const(cw,1),count);
            var nextMin=Expr.Mux(complete&(~any|sample.SignedLt(min)),sample,min);
            var nextMax=Expr.Mux(complete&(~any|max.SignedLt(sample)),sample,max);
            var nextAny=any|complete;
            var eventFlags=Expr.Concat(~complete,Expr.Concat(complete&countFull,Expr.Concat(complete&extendedEnergy.Slice(ew,ew),Expr.Concat(complete&sumOverflow,Expr.Const(1,0)))));
            var nextFlags=flags|eventFlags;
            // Clear accumulation after publishing, so accepted back-to-back frames are independent.
            Action<Signal,Expr> accumulate=(r,next)=>m.NextWhen(r,fire,Expr.Mux(input.Last,Expr.Const(r.Width,0),next));
            accumulate(sum,nextSum);accumulate(energy,nextEnergy);accumulate(count,nextCount);
            accumulate(min,nextMin);accumulate(max,nextMax);accumulate(any,nextAny);accumulate(flags,nextFlags);
            var finalFlags=(nextFlags|(~nextAny).Resize(5)).Resize(8);
            var packed=Expr.Concat(finalFlags,Expr.Concat(nextMax,Expr.Concat(nextMin,Expr.Concat(nextCount,Expr.Concat(nextEnergy,nextSum))))).Resize(options.ResultWidth);
            m.NextWhen(data,close,packed);m.NextWhen(user,close,input.User);
            m.NextWhen(valid,available,close);
            m.Drive(output.Data,data);m.Drive(output.User,user);m.Drive(output.Valid,valid);
            m.Drive(output.Keep,Expr.Const(options.ResultWidth/8,Expr.Mask(options.ResultWidth/8)));
            m.Drive(output.Last,Expr.Const(1,1));
            return output;
        }
    }
}
