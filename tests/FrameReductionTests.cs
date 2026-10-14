using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Numerics;
using SharpHDL;
public static class FrameReductionTests
{
    sealed class Probe {
        readonly Simulator simulator;
        readonly Dictionary<string,Signal> ports;
        public Probe(Module module) { ports=module.Signals.ToDictionary(s=>s.Name);simulator=module.Simulate(); }
        public void Set(string name,BigInteger value) {simulator.Set(ports[name],value);}
        public BigInteger Read(string name) {return simulator.Read(ports[name]);}
        public void Tick(bool reset=false) {simulator.Tick(reset);}
    }
    static int checks;
    static void Check(bool condition,string message) {checks++;if(!condition)throw new Exception(message);}
    static void Reject(Action f) {checks++;try{f();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
    // Reference retains the actual frame, then computes statistics independently.
    static AxisBeat Reference(List<AxisBeat> frame,FrameReductionOptions o) {
        var samples=frame.Where(b=>b.Keep==Expr.Mask(o.SampleWidth/8)).Select(b=>Expr.SignedValue(b.Data,o.SampleWidth)).ToArray();
        BigInteger sum=0,energy=0;int flags=samples.Length==0?1:0;
        foreach(var x in samples) {
            var step=Expr.SignedValue(sum,o.SumWidth)+x;
            if(step< -(BigInteger.One<<(o.SumWidth-1))||step>=(BigInteger.One<<(o.SumWidth-1)))flags|=2;
            sum+=x;energy+=x*x;
        }
        if(energy>Expr.Mask(o.EnergyWidth))flags|=4;
        if(samples.Length>Expr.Mask(o.CountWidth))flags|=8;
        if(samples.Length!=frame.Count)flags|=16;
        BigInteger min=samples.Length==0?0:samples.Min(),max=samples.Length==0?0:samples.Max();
        BigInteger data=(sum&Expr.Mask(o.SumWidth))|((energy&Expr.Mask(o.EnergyWidth))<<o.EnergyOffset)|
            (BigInteger.Min(samples.Length,Expr.Mask(o.CountWidth))<<o.CountOffset)|
            ((min&Expr.Mask(o.SampleWidth))<<o.MinimumOffset)|((max&Expr.Mask(o.SampleWidth))<<o.MaximumOffset)|((BigInteger)flags<<o.FlagsOffset);
        return new AxisBeat(data,Expr.Mask(o.ResultWidth/8),true,frame.Last().User);
    }
    static void Same(AxisBeat actual,AxisBeat expected,string label) {
        Check(actual.Data==expected.Data,label+" data");Check(actual.Keep==expected.Keep,label+" keep");
        Check(actual.Last==expected.Last,label+" last");Check(actual.User==expected.User,label+" user");
    }
    static List<AxisBeat> Stimulus(int width) {
        var result=new List<AxisBeat>();
        Action<long[],bool> frame=(values,valid)=>{
            for(int j=0;j<values.Length;j++)result.Add(new AxisBeat(new BigInteger(values[j])&Expr.Mask(width),valid?Expr.Mask(width/8):0,j==values.Length-1,j%256));
        };
        frame(new long[]{0},false); // empty frame with a closing invalid beat
        frame(new long[]{1,-2,3},true);
        frame(new long[]{-32768,-32768,-32768,-32768,-32768},true);
        frame(Enumerable.Repeat(32767L,320).ToArray(),true); // sum/energy/count overflow
        frame(new long[]{-1},true);frame(new long[]{1},true);frame(new long[]{0},true);
        var random=new Random(61822);
        for(int f=0;f<500;f++) {
            int length=1+random.Next(40);
            for(int j=0;j<length;j++)result.Add(new AxisBeat(random.Next(65536),random.Next(7)==0?random.Next(3):3,j==length-1,random.Next(256)));
        }
        return result;
    }
    public static void Run() {
        checks=0;
        Reject(()=>new FrameReductionOptions(7));Reject(()=>new FrameReductionOptions(16,15));
        Reject(()=>new FrameReductionOptions(16,16,31));Reject(()=>new FrameReductionOptions(16,16,32,0));
        var options=new FrameReductionOptions(16,16,32,8);var beats=Stimulus(16);
        var reference=new List<AxisBeat>();var frame=new List<AxisBeat>();
        foreach(var b in beats){frame.Add(b);if(b.Last){reference.Add(Reference(frame,options));frame.Clear();}}
        var native=NativeFrameReducer.Reduce(beats,options,8).ToArray();
        Check(native.Length==reference.Count,"native frame count");
        for(int i=0;i<native.Length;i++)Same(native[i],reference[i],"native frame "+i);
        Reject(()=>options.Decode(new AxisBeat(0,0,true,0)));
        Reject(()=>options.Decode(new AxisBeat(BigInteger.One<<(options.FlagsOffset+5),Expr.Mask(options.ResultWidth/8),true,0)));
        Reject(()=>options.Decode(new AxisBeat(0,Expr.Mask(options.ResultWidth/8),false,0)));
        var empty=options.Decode(native[0]);Check(empty.Empty&&empty.InvalidSample&&empty.Count==0,"empty frame");
        var overflow=options.Decode(native[3]);Check(overflow.SumOverflow&&overflow.EnergyOverflow&&overflow.CountOverflow,"all overflow flags");
        var reset=new NativeFrameReducer(options,8);
        reset.Push(new AxisBeat(7,3,false,1));reset.Reset();
        Same(reset.Push(new AxisBeat(9,3,true,2)).Value,Reference(new List<AxisBeat>{new AxisBeat(9,3,true,2)},options),"native reset");
        Reject(()=>NativeFrameReducer.Reduce(new[]{new AxisBeat(1,3,false,0)},options).ToArray());
        Reject(()=>reset.Push(new AxisBeat(65536,3,true,0)));
        Check(!NativeFrameReducer.Reduce(new AxisBeat[0],options).Any(),"empty input has no frame");
        // Widths above 64 exercise packed results and full-width signed minimum squares.
        foreach(int width in new[]{8,24,32,64}) {
            var wideOptions=new FrameReductionOptions(width,2*width,2*width+2,3);
            var minimum=BigInteger.One<<(width-1);
            var input=new[]{new AxisBeat(minimum,Expr.Mask(width/8),false,0),new AxisBeat(minimum,Expr.Mask(width/8),true,1)};
            var expected=Reference(input.ToList(),wideOptions);
            Same(NativeFrameReducer.Reduce(input,wideOptions).Single(),expected,"wide native");
            var m=FrameStatisticsExample.Build(wideOptions);var sim=new Probe(m);sim.Tick(true);
            sim.Set("m_axis_tready",1);
            foreach(var b in input) {Set(sim,b);sim.Tick();}
            Same(Read(sim),expected,"wide graph");
        }
        Directory.CreateDirectory("generated/frame-reduction");
        Directory.CreateDirectory("build/frame-reduction");
        Replay(beats,options);
        NumericalReports();
        Console.WriteLine("PASS frame reduction: "+checks+" checks");
    }
    static void Set(Probe sim,AxisBeat b) {
        sim.Set("s_axis_tdata",b.Data);sim.Set("s_axis_tkeep",b.Keep);sim.Set("s_axis_tlast",b.Last?1:0);sim.Set("s_axis_tuser",b.User);sim.Set("s_axis_tvalid",1);
    }
    static AxisBeat Read(Probe sim) {return new AxisBeat(sim.Read("m_axis_tdata"),sim.Read("m_axis_tkeep"),sim.Read("m_axis_tlast")!=0,sim.Read("m_axis_tuser"));}
    static void Replay(List<AxisBeat> beats,FrameReductionOptions options) {
        var module=FrameStatisticsExample.Build(options);var sim=new Probe(module);sim.Tick(true);
        File.WriteAllText("generated/frame-reduction/statistics.v",module.Verilog());
        var tb=new StringBuilder("`timescale 1ns/1ps\nmodule tb; reg clk=0,rst=0; reg [15:0] s_axis_tdata=0; reg [1:0] s_axis_tkeep=0; reg s_axis_tlast=0,s_axis_tvalid=0; reg [7:0] s_axis_tuser=0; wire s_axis_tready; reg m_axis_tready=0; wire ["+(options.ResultWidth-1)+":0] m_axis_tdata; wire ["+(options.ResultWidth/8-1)+":0] m_axis_tkeep; wire m_axis_tlast,m_axis_tvalid; wire [7:0] m_axis_tuser;\nh_FrameStatistics dut(.clk(clk),.rst(rst)");
        foreach(var n in new[]{"s_axis_tdata","s_axis_tkeep","s_axis_tlast","s_axis_tvalid","s_axis_tuser","s_axis_tready","m_axis_tdata","m_axis_tkeep","m_axis_tlast","m_axis_tvalid","m_axis_tuser","m_axis_tready"})tb.Append(",.s_"+n+"("+n+")");
        tb.AppendLine("); initial begin rst=1; #5; clk=1; #5; clk=0;rst=0;");
        var queue=new Queue<AxisBeat>();var frame=new List<AxisBeat>();var random=new Random(7712);
        int index=0,received=0,discardedResults=0,discardedSamples=0,stalls=0,resetCount=0;
        bool pendingReset=false;bool offered=false;AxisBeat beat=default(AxisBeat);AxisBeat? held=null;
        int cycles=0;
        for(;cycles<30000;cycles++) {
            bool dropPending=!pendingReset&&cycles>6000&&sim.Read("m_axis_tvalid")!=0;
            bool resetting=cycles==513||cycles==1031||cycles==4007||dropPending;
            if(dropPending)pendingReset=true;
            if(!offered&&index<beats.Count&&random.Next(5)!=0){beat=beats[index];offered=true;}
            bool ready=cycles%113<71&&random.Next(4)!=0;
            Set(sim,beat);sim.Set("s_axis_tvalid",offered?1:0);sim.Set("m_axis_tready",ready?1:0);
            tb.AppendLine("s_axis_tdata=16'd"+beat.Data+";s_axis_tkeep=2'd"+beat.Keep+";s_axis_tlast="+(beat.Last?1:0)+";s_axis_tuser=8'd"+beat.User+";s_axis_tvalid="+(offered?1:0)+";m_axis_tready="+(ready?1:0)+";rst="+(resetting?1:0)+";#5;");
            bool valid=sim.Read("m_axis_tvalid")!=0;bool inputReady=sim.Read("s_axis_tready")!=0;
            if(resetting){discardedResults+=queue.Count;discardedSamples+=frame.Count;queue.Clear();frame.Clear();held=null;resetCount++;}
            else {
                if(held.HasValue){Check(valid,"valid held");Same(Read(sim),held.Value,"stalled graph");}
                if(valid){Check(queue.Count>0,"no unexpected result");Same(Read(sim),queue.Peek(),"graph result");}
                if(valid&&ready){queue.Dequeue();received++;}
                if(offered&&inputReady){frame.Add(beat);if(beat.Last){queue.Enqueue(Reference(frame,options));frame.Clear();}index++;offered=false;}
                held=valid&&!ready?(AxisBeat?)Read(sim):null;if(held.HasValue)stalls++;
                tb.AppendLine("if(m_axis_tvalid!==1'd"+(valid?1:0)+" || s_axis_tready!==1'd"+(inputReady?1:0)+") $fatal(1,\"handshake cycle "+cycles+"\");");
                if(valid) {
                    var expected=Read(sim); // graph was checked against independent frame oracle above
                    tb.AppendLine("if(m_axis_tdata!=="+options.ResultWidth+"'d"+expected.Data+" || m_axis_tkeep!=="+(options.ResultWidth/8)+"'d"+expected.Keep+" || m_axis_tlast!==1'b1 || m_axis_tuser!==8'd"+expected.User+") $fatal(1,\"result cycle "+cycles+"\");");
                }
            }
            sim.Tick(resetting);tb.AppendLine("clk=1;#5;clk=0;");
            if(resetting) {Check(sim.Read("m_axis_tvalid")==0,"reset clears output");tb.AppendLine("if(m_axis_tvalid!==1'b0) $fatal(1,\"reset output\");");}
            if(index==beats.Count&&queue.Count==0&&!offered&&sim.Read("m_axis_tvalid")==0)break;
        }
        Check(index==beats.Count&&queue.Count==0&&frame.Count==0,"all frames drained");Check(received>400&&stalls>100&&resetCount==4&&discardedResults>0&&discardedSamples>0,"coverage");
        tb.AppendLine("$display(\"PASS frame RTL replay: "+(cycles+1)+" cycles, "+received+" results, "+stalls+" stalled cycles\");$finish;end endmodule");
        File.WriteAllText("build/frame-reduction/statistics_tb.v",tb.ToString());
        File.WriteAllText("generated/frame-reduction/coverage.txt","Cycles: "+(cycles+1)+"\nInput beats accepted: "+index+"\nResults delivered: "+received+"\nResults discarded by reset: "+discardedResults+"\nPartial-frame beats discarded by reset: "+discardedSamples+"\nStalled output cycles: "+stalls+"\nResets: "+resetCount+"\n");
    }
    static void NumericalReports() {
        var report=new NumericalErrorReport();
        for(int encoded=0;encoded<65536;encoded++) {
            int x=encoded<32768?encoded:encoded-65536;
            long calibrated=Math.Max(-32768,Math.Min(32767,2*x-10));
            var actual=MixedScience.Energy(MixedScience.Calibrate(new AxisBeat(encoded,3,false,0)));
            report.Add(calibrated*calibrated,(double)actual.Data);
        }
        Check(report.TotalCount==65536&&report.MaximumAbsoluteError==0&&report.RootMeanSquareError==0,"exact numerical report");
        File.WriteAllText("generated/frame-reduction/numerical-error.md",report.Markdown("Calibrated sample energy: exhaustive signed 16-bit domain")+"\nReference: clamp(2*x-10, -32768, 32767)^2 for every signed 16-bit input. All results fit exactly in double. This is native-model evidence; RTL evidence is the separate cycle replay and mixed AXI regression.\n");
        var quantizer=new Module("ScientificQuantizer");
        var qi=quantizer.Input("sample",16);var qo=quantizer.Output("rounded",16);
        quantizer.Drive(qo,FixedArithmetic.Convert(qi,12,16,8,Rounding.NearestEven,Overflow.Saturate));
        var qs=quantizer.Simulate();qs.Tick(true);var quantization=new NumericalErrorReport();
        for(int raw=0;raw<65536;raw++) {
            int signed=raw<32768?raw:raw-65536;
            qs.Set(qi,raw);var rounded=Expr.SignedValue(qs.Read(qo),16);
            Check(rounded==(long)Math.Round(signed/16.0,MidpointRounding.ToEven),"nearest-even graph reference");
            quantization.Add(signed/4096.0,(double)rounded/256.0);
        }
        Check(quantization.MaximumAbsoluteError==1.0/512,"half-LSB rounding bound");
        File.WriteAllText("generated/frame-reduction/quantization-error.md",quantization.Markdown("Fixed-point conversion: 12 to 8 fractional bits")+"\nExhaustive graph evaluation of all signed 16-bit encodings with 12 fractional bits, converted to signed 16-bit with 8 fractional bits, nearest-even rounding and saturation. This domain does not reach saturation. Maximum absolute error is exactly half an output LSB (1/512). RTL numerical operators have separate regression tests; this report describes graph evaluation.\n");
        var known=new NumericalErrorReport();known.Add(0,2);known.Add(4,3);known.Add(double.NaN,1);
        Check(known.MaximumAbsoluteError==2&&known.MaximumRelativeError==0.25,"known report maxima");
        Check(Math.Abs(known.RootMeanSquareError-Math.Sqrt(2.5))<1e-14,"known RMS");
        Check(known.ZeroReferenceCount==1&&known.NonFinitePairCount==1,"special report counts");
        var large=new NumericalErrorReport();large.Add(0,1e200);large.Add(0,1e200);
        Check(large.RootMeanSquareError==1e200,"scaled RMS avoids intermediate square overflow");
        var impossible=new NumericalErrorReport();impossible.Add(-double.MaxValue,double.MaxValue);
        Check(impossible.UnrepresentableDifferenceCount==1&&double.IsPositiveInfinity(impossible.RootMeanSquareError),"overflow difference counted");
        Check(double.IsNaN(new NumericalErrorReport().RootMeanSquareError),"empty RMS undefined");
    }
}
