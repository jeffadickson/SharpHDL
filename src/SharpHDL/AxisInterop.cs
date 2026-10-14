using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpHDL
{
    /// <summary>One AXI4-Stream beat. Payload and metadata travel together.</summary>
    public struct AxisBeat
    {
        public BigInteger Data { get; private set; }
        public BigInteger Keep { get; private set; }
        public bool Last { get; private set; }
        public BigInteger User { get; private set; }
        public AxisBeat(BigInteger data, BigInteger keep, bool last, BigInteger user)
        { Data=data; Keep=keep; Last=last; User=user; }
    }

    public sealed class AxisFormat
    {
        public int DataWidth { get; private set; }
        public int UserWidth { get; private set; }
        public AxisFormat(int dataWidth,int userWidth=1)
        {
            if(dataWidth<8 || dataWidth>4096 || dataWidth%8!=0 || userWidth<1 || userWidth>4096)
                throw new ArgumentException("AXI format requires byte-aligned data (8..4096 bits) and 1..4096 user bits.");
            DataWidth=dataWidth;UserWidth=userWidth;
        }
        internal bool Same(AxisFormat other) { return other!=null && DataWidth==other.DataWidth && UserWidth==other.UserWidth; }
        internal void Check(AxisBeat beat)
        {
            if(beat.Data<0 || beat.Data>Expr.Mask(DataWidth) || beat.Keep<0 || beat.Keep>Expr.Mask(DataWidth/8) || beat.User<0 || beat.User>Expr.Mask(UserWidth))
                throw new ArgumentException("Native AXI beat does not fit its declared format.");
        }
    }

    /// <summary>A single-input/single-output RTL component with an optional one-beat functional model.
    /// External RTL must implement aclk, active-low aresetn, and all six stream fields.
    /// The declaration is a contract; the external file is checked by the RTL compiler.</summary>
    public sealed class AxisComponent
    {
        public string ModuleName { get; private set; }
        public string InputPrefix { get; private set; }
        public string OutputPrefix { get; private set; }
        public AxisFormat InputFormat { get; private set; }
        public AxisFormat OutputFormat { get; private set; }
        public string SourceFile { get; private set; }
        internal string GeneratedSource;
        internal string[] Definitions;
        readonly Dictionary<string,long> parameters=new Dictionary<string,long>();
        readonly Func<AxisBeat,AxisBeat> native;
        internal static string Id(string name)
        {
            if(name==null || !Regex.IsMatch(name,@"\A[A-Za-z_][A-Za-z0-9_]*\z"))throw new ArgumentException("Expected a simple RTL identifier.");
            return "\\"+name+" "; // Escaping also handles Verilog keywords.
        }
        AxisComponent(string module,string input,string output,AxisFormat inputFormat,AxisFormat outputFormat,Func<AxisBeat,AxisBeat> model)
        {
            Id(module);Id(input);Id(output);
            if(input==output)throw new ArgumentException("Input and output stream prefixes must differ.");
            ModuleName=module;Definitions=new[]{module};InputPrefix=input;OutputPrefix=output;
            InputFormat=inputFormat??throw new ArgumentNullException(nameof(inputFormat));
            OutputFormat=outputFormat??throw new ArgumentNullException(nameof(outputFormat));native=model;
        }
        public static AxisComponent External(string module,string sourceFile,string input,string output,AxisFormat inputFormat,AxisFormat outputFormat,Func<AxisBeat,AxisBeat> nativeModel=null)
        {
            if(string.IsNullOrWhiteSpace(sourceFile))throw new ArgumentException("An external source file is required.");
            return new AxisComponent(module,input,output,inputFormat,outputFormat,nativeModel){SourceFile=sourceFile};
        }
        public AxisComponent Parameter(string name,long value)
        {
            Id(name);if(GeneratedSource!=null)throw new InvalidOperationException("Generated SharpHDL modules do not expose RTL parameters.");
            if(parameters.ContainsKey(name))throw new ArgumentException("Duplicate parameter.");
            parameters.Add(name,value);return this;
        }
        public static AxisComponent FromSharp(Module module,string input,string output,Func<AxisBeat,AxisBeat> nativeModel=null)
        {
            if(module==null)throw new ArgumentNullException(nameof(module));
            var ports=module.Signals.Where(s=>s.Kind=="input"||s.Kind=="output").ToList();
            var expected=new HashSet<string>();
            Func<string,bool,AxisFormat> format=(prefix,isInput)=>{
                Id(prefix);
                var data=ports.SingleOrDefault(s=>s.Name==prefix+"_tdata");
                var user=ports.SingleOrDefault(s=>s.Name==prefix+"_tuser");
                if(data==null||user==null)throw new ArgumentException("Missing AXI data or user port.");
                var f=new AxisFormat(data.Width,user.Width);
                foreach(var field in Fields(f)) {
                    var name=prefix+"_"+field.Key; expected.Add(name);
                    var p=ports.SingleOrDefault(s=>s.Name==name);
                    string direction=(isInput != (field.Key=="tready"))?"input":"output";
                    if(p==null||p.Width!=field.Value||p.Kind!=direction)throw new ArgumentException("AXI port shape/direction mismatch: "+name);
                }
                return f;
            };
            var inf=format(input,true);var outf=format(output,false);
            if(ports.Any(p=>!expected.Contains(p.Name)))throw new ArgumentException("This composition API requires exactly one input and output stream; extra scalar ports need a wrapper.");
            return new AxisComponent("h_"+module.Name+"_axi",input,output,inf,outf,nativeModel){Definitions=new[]{"h_"+module.Name,"h_"+module.Name+"_axi"},GeneratedSource=module.Verilog()+"\n"+Axi4Stream.Wrapper(module)};
        }
        internal static KeyValuePair<string,int>[] Fields(AxisFormat f) {
            return new[]{new KeyValuePair<string,int>("tdata",f.DataWidth),new KeyValuePair<string,int>("tkeep",f.DataWidth/8),
                new KeyValuePair<string,int>("tlast",1),new KeyValuePair<string,int>("tuser",f.UserWidth),
                new KeyValuePair<string,int>("tvalid",1),new KeyValuePair<string,int>("tready",1)};
        }
        internal AxisBeat Apply(AxisBeat beat) {
            RequireNative();InputFormat.Check(beat);var result=native(beat);OutputFormat.Check(result);return result;
        }
        internal void RequireNative() { if(native==null)throw new InvalidOperationException("No native model for "+ModuleName+"; run mixed RTL simulation instead."); }
        internal string Instantiate(string instance,string input,string output) {
            var b=new StringBuilder(Id(ModuleName));
            if(parameters.Count>0)b.Append("#("+string.Join(", ",parameters.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>"."+Id(p.Key)+"("+p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)+")"))+") ");
            b.Append(Id(instance)+"(.aclk(aclk), .aresetn(aresetn)");
            foreach(var field in Fields(InputFormat))b.Append(", ."+Id(InputPrefix+"_"+field.Key)+"("+input+"_"+field.Key+")");
            foreach(var field in Fields(OutputFormat))b.Append(", ."+Id(OutputPrefix+"_"+field.Key)+"("+output+"_"+field.Key+")");
            return b.AppendLine(");").ToString();
        }
    }

    /// <summary>Single-clock linear composition; no implicit width converters, buffers or CDC.
    /// Native Run is a stateless one-input/one-output functional model, not a clock simulator.</summary>
    public sealed class AxisPipeline
    {
        readonly List<AxisComponent> stages=new List<AxisComponent>();
        readonly string name;
        public AxisPipeline(string name) { AxisComponent.Id(name);this.name=name; }
        public AxisPipeline Add(AxisComponent stage) {
            if(stage==null)throw new ArgumentNullException(nameof(stage));
            if(stages.Count>0 && !stages.Last().OutputFormat.Same(stage.InputFormat))throw new ArgumentException("Stream shape mismatch; insert an explicit converter.");
            if(stage.Definitions.Contains(name))throw new ArgumentException("Top and component module names collide.");
            foreach(var existing in stages.Where(s=>s.Definitions.Intersect(stage.Definitions).Any()))
                if(existing.GeneratedSource!=stage.GeneratedSource || existing.SourceFile!=stage.SourceFile)throw new ArgumentException("Conflicting module definitions.");
            stages.Add(stage);return this;
        }
        void Check() { if(stages.Count==0)throw new InvalidOperationException("Empty AXI pipeline."); }
        public string[] ExternalSources { get { return stages.Where(s=>s.SourceFile!=null).Select(s=>s.SourceFile).Distinct().ToArray(); } }
        public string Verilog() {
            Check();var b=new StringBuilder("// Mixed SharpHDL/Verilog AXI4-Stream pipeline. All components share aclk/aresetn.\n");
            b.AppendLine("// Assert active-low reset through a rising clock edge; release synchronously to aclk.\n// No CDC or reset synchronizer is inserted. No implicit buffering or width conversion.");
            b.AppendLine("module "+AxisComponent.Id(name)+"(input wire aclk, input wire aresetn");
            Action<string,AxisFormat,bool> port=(prefix,f,input)=>{
                foreach(var field in AxisComponent.Fields(f)) {
                    bool incoming=input!=(field.Key=="tready");
                    b.AppendLine("// "+prefix+" "+Meaning(field.Key));
                    b.AppendLine(", "+(incoming?"input":"output")+" wire ["+(field.Value-1)+":0] "+prefix+"_"+field.Key);
                }
            };
            port("s_axis",stages[0].InputFormat,true);port("m_axis",stages.Last().OutputFormat,false);b.AppendLine(");");
            for(int i=0;i<stages.Count-1;i++)foreach(var field in AxisComponent.Fields(stages[i].OutputFormat)) {
                b.AppendLine("// Link "+i+": "+Meaning(field.Key));
                b.AppendLine("wire ["+(field.Value-1)+":0] link"+i+"_"+field.Key+";");
            }
            for(int i=0;i<stages.Count;i++)b.Append(stages[i].Instantiate("stage"+i,i==0?"s_axis":"link"+(i-1),i==stages.Count-1?"m_axis":"link"+i));
            b.AppendLine("endmodule");
            foreach(var stage in stages.GroupBy(s=>s.ModuleName).Select(g=>g.First()))b.AppendLine(stage.GeneratedSource);
            return b.ToString().TrimEnd()+"\n";
        }
        static string Meaning(string field) {
            switch(field) {
                case "tdata":return "TDATA: payload; byte lane 0 is bits [7:0].";
                case "tkeep":return "TKEEP: bit i qualifies payload byte lane i.";
                case "tlast":return "TLAST: final beat of frame; stalls with payload.";
                case "tuser":return "TUSER: application metadata; stalls with payload.";
                case "tvalid":return "TVALID: hold asserted and keep payload stable until handshake.";
                default:return "TREADY: reverse flow; transfer at rising edge when VALID and READY are high.";
            }
        }
        public void Run(HwStream<AxisBeat> input,HwStream<AxisBeat> output) {
            Check();if(input==null||output==null)throw new ArgumentNullException();
            foreach(var stage in stages)stage.RequireNative(); // fail before consuming any input
            NativeProcess.RunUntilInputEnds(()=>{while(true){var beat=input.Read();foreach(var stage in stages)beat=stage.Apply(beat);output.Write(beat);}});
        }
    }
}
