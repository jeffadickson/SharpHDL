using System;
using System.Globalization;
using System.Text;
namespace SharpHDL
{
    /// <summary>Streaming double-reference error statistics. Non-finite pairs are counted,
    /// never silently included as successful samples. Relative error excludes zero references.</summary>
    public sealed class NumericalErrorReport
    {
        public long TotalCount { get; private set; }
        public long FinitePairCount { get; private set; }
        public long NonFinitePairCount { get; private set; }
        public long ZeroReferenceCount { get; private set; }
        public long UnrepresentableDifferenceCount { get; private set; }
        public double MaximumAbsoluteError { get; private set; }
        public double MaximumRelativeError { get; private set; }
        double scale,scaledSquares;
        public double RootMeanSquareError { get {
            if(FinitePairCount==0)return double.NaN;
            if(UnrepresentableDifferenceCount>0)return double.PositiveInfinity;
            return scale==0?0:scale*Math.Sqrt(scaledSquares/FinitePairCount);
        } }
        static bool Finite(double value) {return !double.IsNaN(value)&&!double.IsInfinity(value);}
        public void Add(double reference,double actual) {
            TotalCount++;
            if(!Finite(reference)||!Finite(actual)){NonFinitePairCount++;return;}
            FinitePairCount++;
            double error=Math.Abs(actual-reference);
            if(!Finite(error))UnrepresentableDifferenceCount++;
            MaximumAbsoluteError=Math.Max(MaximumAbsoluteError,error);
            if(reference==0)ZeroReferenceCount++;
            else MaximumRelativeError=Math.Max(MaximumRelativeError,error/Math.Abs(reference));
            if(Finite(error)&&error!=0) {
                if(error>scale){double ratio=scale/error;scaledSquares=1+scaledSquares*ratio*ratio;scale=error;}
                else{double ratio=error/scale;scaledSquares+=ratio*ratio;}
            }
        }
        public string Markdown(string title) {
            if(title==null)throw new ArgumentNullException(nameof(title));
            Func<double,string> number=x=>x.ToString("G17",CultureInfo.InvariantCulture);
            var b=new StringBuilder("# "+title.Replace("\n"," ").Replace("\r"," ")+"\n\nMetric | Value\n--- | ---:\n");
            b.AppendLine("Total pairs | "+TotalCount);b.AppendLine("Finite pairs | "+FinitePairCount);
            b.AppendLine("Non-finite pairs (excluded) | "+NonFinitePairCount);
            b.AppendLine("Zero references (relative error excluded) | "+ZeroReferenceCount);
            b.AppendLine("Finite pairs with unrepresentable difference | "+UnrepresentableDifferenceCount);
            b.AppendLine("Maximum absolute error | "+(FinitePairCount==0?"N/A":number(MaximumAbsoluteError)));
            b.AppendLine("Maximum relative error | "+(FinitePairCount==ZeroReferenceCount?"N/A":number(MaximumRelativeError)));
            b.AppendLine("RMS absolute error | "+number(RootMeanSquareError));
            b.AppendLine("\nRelative error uses abs(actual-reference)/abs(reference). Zero references are included in absolute/RMS error. Non-finite pairs are counted separately; a zero finite-sample error does not excuse them. This report characterizes tested samples, not a proof over an untested domain.");
            return b.ToString();
        }
    }
}
