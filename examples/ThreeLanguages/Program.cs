using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Numerics;
using System.Collections.Generic;
using SharpHDL;
using Module=SharpHDL.Module;
using HStream=SharpHDL.Stream;
using ThreeLanguages;

static class Program
{
    static int checks;static string directory;
    static void Check(bool okay,string message){checks++;if(!okay)throw new Exception(message);}
    static void Write(string name,string text){File.WriteAllText(Path.Combine(directory,name),text);}
    static string Disassemble(MethodInfo method)
    {
        var codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>(ushort)o.Value);
        var bytes=method.GetMethodBody().GetILAsByteArray();var text=new StringBuilder(method.DeclaringType.FullName+"."+method.Name+"\n");
        int i=0;while(i<bytes.Length)
        {
            int offset=i;ushort value=bytes[i++];if(value==0xfe)value=(ushort)(0xfe00|bytes[i++]);var op=codes[value];string operand="";
            switch(op.OperandType)
            {
                case OperandType.InlineNone:break;
                case OperandType.ShortInlineI:operand=((sbyte)bytes[i++]).ToString();break;
                case OperandType.ShortInlineVar:operand=bytes[i++].ToString();break;
                case OperandType.InlineVar:operand=BitConverter.ToUInt16(bytes,i).ToString();i+=2;break;
                case OperandType.InlineI:operand=BitConverter.ToInt32(bytes,i).ToString();i+=4;break;
                case OperandType.InlineI8:operand=BitConverter.ToInt64(bytes,i).ToString();i+=8;break;
                case OperandType.ShortInlineR:operand=BitConverter.ToSingle(bytes,i).ToString();i+=4;break;
                case OperandType.InlineR:operand=BitConverter.ToDouble(bytes,i).ToString();i+=8;break;
                case OperandType.ShortInlineBrTarget:operand="IL_"+(i+1+(sbyte)bytes[i]).ToString("x4");i++;break;
                case OperandType.InlineBrTarget:operand="IL_"+(i+4+BitConverter.ToInt32(bytes,i)).ToString("x4");i+=4;break;
                case OperandType.InlineString:operand=method.Module.ResolveString(BitConverter.ToInt32(bytes,i));i+=4;break;
                case OperandType.InlineMethod:case OperandType.InlineField:case OperandType.InlineType:case OperandType.InlineTok:
                    var member=method.Module.ResolveMember(BitConverter.ToInt32(bytes,i));operand=(member.DeclaringType==null?"":member.DeclaringType.FullName+"::")+member;i+=4;break;
                default:throw new NotSupportedException("Disassembler operand "+op.OperandType);
            }
            text.AppendLine("IL_"+offset.ToString("x4")+": "+op.Name+" "+operand);
        }
        return text.ToString();
    }
    sealed class Beat { public int Data,Keep,User,Last; }
    sealed class Trace
    {
        public readonly Module M;public readonly Simulator Sim;readonly StringBuilder tb=new StringBuilder();readonly Signal[] outputs;
        public Trace(Module m,string stem)
        {
            M=m;Stem=stem;Sim=m.Simulate();Sim.Tick(true);outputs=m.Ports.Where(p=>p.IsOutput).ToArray();Write(stem+".v",m.Verilog());Write(stem+".graph.txt",m.DumpGraph());
            tb.AppendLine("module tb;reg clk=0,rst=1;");
            foreach(var p in m.Ports)tb.AppendLine((p.IsInput?"reg ":"wire ")+"["+(p.Width-1)+":0] "+p.Name+(p.IsInput?"=0;":";"));
            tb.Append("h_"+m.Name+" dut(.clk(clk),.rst(rst)");foreach(var p in m.Ports)tb.Append(",.s_"+p.Name+"("+p.Name+")");
            tb.AppendLine(");initial begin #1;clk=1;#1;clk=0;rst=0;");
        }
        public string Stem;
        public void Set(Signal s,int value){Sim.Set(s,value);tb.AppendLine(s.Name+"="+s.Width+"'d"+value+";");}
        public bool On(Expr e){return Sim.Read(e)!=0;}
        public void Step(){tb.AppendLine("#1;");foreach(var p in outputs)tb.AppendLine("if("+p.Name+" !== "+p.Width+"'d"+Sim.Read(p)+") $fatal(1,\""+Stem+" "+p.Name+"\");");
            tb.AppendLine("clk=1;#1;clk=0;");Sim.Tick();}
        public void Save(){tb.AppendLine("$display(\"PASS HDL "+Stem+"\");$finish;end endmodule");Write(Stem+"_tb.v",tb.ToString());}
    }
    static void SameHardware()
    {
        var modules=new[]{CSharpPiece.Build(),FSharpPiece.Build(),VisualBasicPiece.Build()};var labels=new[]{"CSharp","FSharp","VisualBasic"};
        var graph=modules[0].DumpGraph();var verilog=modules[0].Verilog();
        var il=new StringBuilder();var types=new[]{typeof(CSharpPiece),typeof(FSharpPiece),typeof(VisualBasicPiece)};
        for(int k=0;k<3;k++)
        {
            Check(modules[k].DumpGraph()==graph,"Language graphs differ");Check(modules[k].Verilog()==verilog,"Language RTL differs");
            var t=new Trace(modules[k],"AddTwo_"+labels[k]);var b=modules[k].Ports.Single(p=>p.Name=="b");var a=modules[k].Ports.Single(p=>p.Name=="a");
            for(int value=0;value<256;value++){t.Set(b,value);Check(t.Sim.Read(a)==((value+2)&255),"AddTwo result");t.Step();}t.Save();
            il.AppendLine(Disassemble(types[k].GetMethod("AddTwo")));il.AppendLine(Disassemble(types[k].GetMethod("HostAddTwo")));
        }
        Check(CSharpPiece.HostAddTwo(int.MaxValue)==int.MinValue+1,"C# unchecked host behavior");
        bool overflow=false;try{VisualBasicPiece.HostAddTwo(int.MaxValue);}catch(OverflowException){overflow=true;}Check(overflow,"VB checked host behavior");
        Write("actual-cil.txt",il.ToString());
        Write("equivalence.txt","Identical hardware graphs and Verilog text for C#, F#, VB. Exhaustive 256-input checks per language passed. This is executed elaboration, not CIL-to-RTL synthesis.\n");
    }
    static void Pipeline(int kind)
    {
        string name=kind==0?"MixedLanguagePipeline":kind==1?"OneStagePipeline":"TwoStagePipeline";
        var m=new Module(name);var input=HStream.Input(m,"input",8,8);var output=HStream.Output(m,"output",8,8);
        HStream result;
        if(kind==0)
        {
            var fsharp=FSharpPiece.AddStage(m,input);
            var buffered=StreamBlocks.SkidBuffer(m,fsharp,"csharp_skid");
            result=VisualBasicPiece.MultiplyStage(m,buffered);
        }
        else result=kind==1?CSharpPiece.OneStage(m,input):CSharpPiece.TwoStages(m,input);
        HStream.Connect(m,result,output);
        var t=new Trace(m,name);var queue=new Queue<Beat>();var rng=new Random(117);Beat offer=null,held=null;int sent=0,received=0,first=-1;
        int latency=kind==0?3:kind==1?1:2;
        for(int cycle=0;cycle<700;cycle++)
        {
            if(offer==null&&sent<160&&(cycle<20||rng.Next(4)!=0))offer=new Beat{Data=(sent*17)&255,Keep=sent%4==0?0:1,Last=sent%5==0?1:0,User=sent};
            t.Set(input.Valid,offer==null?0:1);if(offer!=null){t.Set(input.Data,offer.Data);t.Set(input.Keep,offer.Keep);t.Set(input.Last,offer.Last);t.Set(input.User,offer.User);}
            t.Set(output.Ready,cycle<20||cycle>550?1:cycle>=40&&cycle<70?0:rng.Next(2));
            var actual=new Beat{Data=(int)t.Sim.Read(output.Data),Keep=(int)t.Sim.Read(output.Keep),Last=(int)t.Sim.Read(output.Last),User=(int)t.Sim.Read(output.User)};
            if(held!=null)Check(t.On(output.Valid)&&Equal(actual,held),"Payload changed under stall");
            held=t.On(output.Valid)&&!t.On(output.Ready)?actual:null;
            if(t.On(output.Fire)){Check(queue.Count>0,"Unexpected output");Check(Equal(actual,queue.Dequeue()),"Pipeline output/order/sidebands");received++;if(first<0)first=cycle;}
            if(cycle>=latency&&cycle<20)Check(t.On(output.Fire),"Full-rate pipeline gap");
            if(t.On(input.Fire)){queue.Enqueue(new Beat{Data=((offer.Data+2)*3)&255,Keep=offer.Keep,Last=offer.Last,User=offer.User});offer=null;sent++;}
            t.Step();
        }
        Check(first==latency,"Pipeline latency");Check(sent==160&&received==160&&queue.Count==0,"Pipeline failed to drain");t.Save();
        Write(name+".latency.txt","Minimum latency "+latency+" cycles; 160 beats tested with stalls; one beat per cycle in the no-stall interval.\n");
    }
    static bool Equal(Beat a,Beat b){return a.Data==b.Data&&a.Keep==b.Keep&&a.Last==b.Last&&a.User==b.User;}
    static void Board(int expectedAddend=2)
    {
        Write("ClassroomCore.v",ClassroomCore.Build().Verilog());
        var m=ClassroomCore.Build(4);var t=new Trace(m,"ClassroomCore_fast");var buttons=m.Ports.Single(p=>p.Name=="buttons");var left=m.Ports.Single(p=>p.Name=="left_digit");var right=m.Ports.Single(p=>p.Name=="right_digit");var leds=m.Ports.Single(p=>p.Name=="leds");
        int oldButtons=0,b=0,count=0,divider=0;bool run=false,mode=false;
        int[] schedule={0,2,2,0,2,0,4,4,0,8,0,0,0,0,0,0,0,0,1,0,8,0};
        for(int cycle=0;cycle<200;cycle++)
        {
            int value=cycle<schedule.Length?schedule[cycle]:cycle%7==0?2:0;t.Set(buttons,value);
            Check(t.Sim.Read(left)==(mode?count>>4:b),"Classroom left digit");Check(t.Sim.Read(right)==(mode?count&15:(b+expectedAddend)&15),"Classroom right digit");Check(t.Sim.Read(leds)==(mode?count&15:b),"Classroom binary LEDs");
            int press=value&~oldButtons;bool tick=divider==3;divider=tick?0:divider+1;
            if((press&1)!=0){b=0;count=0;}else{if((press&2)!=0)b=(b+1)&15;if(run&&tick)count=(count+1)&255;}
            if((press&4)!=0)run=!run;if((press&8)!=0)mode=!mode;oldButtons=value;t.Step();
        }t.Save();
    }
    static void Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--classroom")
        {
            if(args.Length!=3)throw new ArgumentException("--classroom EXPECTED_ADDEND OUTPUT_DIRECTORY");
            int addend=int.Parse(args[1]);if(addend<0||addend>15)throw new ArgumentException("Expected addend must be 0..15.");
            directory=args[2];Directory.CreateDirectory(directory);Board(addend);
            Console.WriteLine("PASS: "+checks+" classroom checks against expected addend "+addend);return;
        }
        directory=args.Length==0?"generated/three-languages":args[0];Directory.CreateDirectory(directory);
        SameHardware();Pipeline(0);Pipeline(1);Pipeline(2);Board();
        string summary="PASS: "+checks+" three-language/pipeline/classroom assertions; actual CIL, matching graphs, RTL and benches emitted.";
        Console.WriteLine(summary);Write("verification.txt",summary+"\n");
    }
}
