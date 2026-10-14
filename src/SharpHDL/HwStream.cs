using System;
using System.Numerics;
namespace SharpHDL
{
    /// <summary>A single-producer, single-consumer ready/valid channel. No implicit storage.</summary>
    public abstract class HwChannel
    {
        public Signal Data { get; internal set; }
        public Signal Valid { get; internal set; }
        public Signal Ready { get; internal set; }
        public Type TokenType { get; internal set; }
        public Expr Fire { get { return Valid & Ready; } }
    }
    public sealed class HwStream<T> : HwChannel where T : struct
    {
        private HwStream() { }
        static HwStream<T> Create(Module m,string name,int direction)
        {
            int width=PackedLayout.Width(typeof(T));
            Func<string,int,Signal> f=(n,w)=>direction==1?m.Input(n,w):direction==2?m.Output(n,w):m.Wire(n,w);
            Func<string,int,Signal> r=(n,w)=>direction==1?m.Output(n,w):direction==2?m.Input(n,w):m.Wire(n,w);
            var s=new HwStream<T>{TokenType=typeof(T),Data=f(name+"_data",width),Valid=f(name+"_valid",1),Ready=r(name+"_ready",1)};
            s.Data.Describe(name+": typed token; "+PackedLayout.Describe(typeof(T))+"; stable while valid=1 and ready=0.");
            s.Valid.Describe(name+": producer offers a token; holds until rising-edge valid AND ready transfer.");
            s.Ready.Describe(name+": consumer acceptance; flows opposite data. Synchronous reset discards buffered tokens.");
            return s;
        }
        public static HwStream<T> Input(Module m,string name) { return Create(m,name,1); }
        public static HwStream<T> Output(Module m,string name) { return Create(m,name,2); }
        public static HwStream<T> Internal(Module m,string name) { return Create(m,name,0); }
        public T Read() { throw new InvalidOperationException("Hardware intrinsic: compile this method with CilCompiler.AttachProcess."); }
        public void Write(T value) { throw new InvalidOperationException("Hardware intrinsic: compile this method with CilCompiler.AttachProcess."); }
        public static void Connect(Module m,HwStream<T> source,HwStream<T> sink)
        { m.Drive(sink.Data,source.Data);m.Drive(sink.Valid,source.Valid);m.Drive(source.Ready,sink.Ready); }
        /// <summary>Explicit register FIFO, depth 1..256. Allows simultaneous push/pop at full occupancy.</summary>
        public static HwStream<T> Fifo(Module m,HwStream<T> input,string name,int depth)
        {
            if(depth<1||depth>256)throw new ArgumentOutOfRangeException("depth");
            var output=Internal(m,name);int w=1;while((1<<w)<=depth)w++;
            var count=m.Register(name+"_occupancy",w,0);count.Describe(name+": number of buffered tokens, range 0.."+depth+"; reset empties FIFO.");
            var valid=~count.Eq(Expr.Const(w,0));var pop=valid & output.Ready;
            var room=count.Lt(Expr.Const(w,depth))|pop;var push=input.Valid & room;
            m.Drive(input.Ready,room);m.Drive(output.Valid,valid);
            m.Next(count,Expr.Mux(push^pop,Expr.Mux(push,count+Expr.Const(w,1),count-Expr.Const(w,1)),count));
            var slots=new Signal[depth];for(int i=0;i<depth;i++)slots[i]=m.Register(name+"_token"+i,input.Data.Width,0);
            var index=Expr.Mux(pop,count-Expr.Const(w,1),count);
            for(int i=0;i<depth;i++)
            {
                Expr shifted=i+1<depth?Expr.Mux(pop,slots[i+1],slots[i]):slots[i];
                m.Next(slots[i],Expr.Mux(push & index.Eq(Expr.Const(w,i)),input.Data,shifted));
            }
            m.Drive(output.Data,slots[0]);return output;
        }
        /// <summary>Export scalar tokens as full AXI beats. Every token is a one-beat packet.</summary>
        public static void ToAxi(Module m,HwStream<T> source,Stream sink)
        {
            if(source.Data.Width!=sink.Width)throw new ArgumentException("AXI payload width must match the byte-aligned token width.");
            m.Drive(sink.Data,source.Data);m.Drive(sink.Valid,source.Valid);m.Drive(source.Ready,sink.Ready);
            m.Drive(sink.Keep,Expr.Const(sink.Keep.Width,Expr.Mask(sink.Keep.Width)));
            m.Drive(sink.Last,Expr.Const(1,1));m.Drive(sink.User,Expr.Const(sink.User.Width,0));
            sink.Data.Describe(source.Data.Annotation);sink.Last.Describe("Scalar adapter: every token is a complete one-beat packet (TLAST=1).");
        }
        /// <summary>Import scalar AXI beats only when the caller explicitly accepts dropping metadata.</summary>
        public static void FromAxi(Module m,Stream source,HwStream<T> sink,bool discardSidebands)
        {
            if(!discardSidebands)throw new ArgumentException("Scalar conversion discards TKEEP/TLAST/TUSER; explicit consent required. Use Stream for packet-preserving paths.");
            if(source.Width!=sink.Data.Width)throw new ArgumentException("AXI payload width mismatch.");
            m.Drive(sink.Data,source.Data);m.Drive(sink.Valid,source.Valid);m.Drive(source.Ready,sink.Ready);
        }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HardwareProcessAttribute : Attribute { }
    public sealed class ProcessHandle
    {
        public Signal ProgramCounter { get; internal set; }
        public Signal Halted { get; internal set; }
        public Signal Fault { get; internal set; }
        public Signal AssertionFailed { get; internal set; }
        public Signal WaitingForInput { get; internal set; }
        public Signal WaitingForOutput { get; internal set; }
    }
}
