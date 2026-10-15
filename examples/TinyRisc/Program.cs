using System;using System.IO;using System.Linq;using System.Numerics;using System.Text;using SharpHDL;using Module=SharpHDL.Module;
class Program
{
 static int checks,retirements,cycles;static void Check(bool ok,string msg){checks++;if(!ok)throw new Exception(msg);}
 static ushort I(int op,int d=0,int a=0,int b=0){return (ushort)((op<<12)|(d<<9)|(a<<6)|b);}
 // Independent architectural interpreter: host integer arithmetic, no Expr or hardware control equations.
 class Model
 {
  public ushort[] R=new ushort[8],Mem=new ushort[256];public int Pc,Led;public bool Halt,Fault;public int Tx=-1,Rx;
  public void Reset(){R=new ushort[8];Pc=Led=0;Halt=Fault=false;Tx=-1;Rx=0;}
  public void Execute(ushort[] rom,int status)
  {
   Tx=-1;Rx=0;if(Pc>=rom.Length){Fault=Halt=true;return;}
   int w=rom[Pc],op=w>>12,d=(w>>9)&7,a=(w>>6)&7,b=(w>>3)&7,im=w&63;if(im>=32)im-=64;
   int n=(Pc+1)&65535,v=0;bool write=false;int address=(R[a]+im)&65535;
   if(op<=6){if((w&7)!=0){Fault=Halt=true;return;}write=true;switch(op){case 0:v=R[a]+R[b];break;case 1:v=R[a]-R[b];break;case 2:v=R[a]&R[b];break;case 3:v=R[a]|R[b];break;case 4:v=R[a]^R[b];break;case 5:v=R[a]<<(R[b]&15);break;case 6:v=R[a]>>(R[b]&15);break;}}
   else switch(op){case 7:v=R[a]+im;write=true;break;case 8:v=w&511;if(v>=256)v-=512;write=true;break;
    case 9:write=true;if(address<256)v=Mem[address];else if(address==0xff00)v=Led;else if(address==0xff01)v=42;else if(address==0xff03){v=165;Rx=1;}else if(address==0xff04)v=status;else Fault=true;break;
    case 10:if(address<256)Mem[address]=R[d];else if(address==0xff00)Led=R[d];else if(address==0xff02)Tx=R[d]&255;else Fault=true;break;
    case 11:if(R[d]==R[a])n=(n+im)&65535;break;case 12:if(R[d]!=R[a])n=(n+im)&65535;break;
    case 13:if((w&63)!=0)Fault=true;else{v=n;n=R[a];write=true;}break;
    case 15:if(w==0xf000)Halt=true;else if(w!=0xf001)Fault=true;break;default:Fault=true;break;}
   if(Fault){Halt=true;return;}if(write&&d!=0)R[d]=(ushort)v;Pc=n;
  }
 }
 class Bench
 {
  public Module M;public Simulator S;public StringBuilder T=new StringBuilder();public Signal[] Ports;public int StepLevel;
  public Bench(ushort[] rom,string name){M=TinyRisc.Build(rom,name);S=M.Simulate();Ports=M.Ports.ToArray();T.AppendLine("`timescale 1ns/1ps\nmodule tb;reg clk=0,rst=0;");foreach(var p in Ports)T.AppendLine((p.IsInput?"reg ":"wire ")+"["+(p.Width-1)+":0] s_"+p.Name+(p.IsInput?"=0":"")+";");T.Append("h_"+name+" dut(.clk(clk),.rst(rst)");foreach(var p in Ports)T.Append(",.s_"+p.Name+"(s_"+p.Name+")");T.AppendLine(");initial begin");}
  public Signal P(string n){return Ports.Single(p=>p.Name==n);}public int Read(string n){return (int)S.Read(P(n));}
  public void Set(string n,int v){S.Set(P(n),v);T.AppendLine("s_"+n+"="+v+";");}
  public void Tick(bool reset=false){S.Tick(reset);T.AppendLine("rst="+(reset?1:0)+";#1;clk=1;#1;");foreach(var p in Ports.Where(p=>p.IsOutput))T.AppendLine("if(s_"+p.Name+"!=="+p.Width+"'d"+S.Read(p)+")$fatal(1,\""+M.Name+" "+p.Name+"\");");T.AppendLine("clk=0;rst=0;");cycles++;}
  public void Save(){var path="generated/tinyrisc/"+M.Name;File.WriteAllText(path+".v",M.Verilog());File.WriteAllText(path+".graph.txt",M.DumpGraph());T.AppendLine("$display(\"PASS HDL "+M.Name+"\");$finish;end endmodule");File.WriteAllText(path+"_tb.v",T.ToString());}
 }
 static void Run(ushort[] rom,string name,bool stepping=false,bool twice=false,string expectedTx=null)
 {
  var t=new Bench(rom,name);var model=new Model();int tx=0,rx=0;var txBytes=new System.Collections.Generic.List<byte>();
  for(int pass=0;pass<(twice?2:1);pass++)
  {
   t.Tick(true);model.Reset();t.Set("switches",42);t.Set("rx_data",165);t.Set("step",0);t.Set("run",stepping?0:1);
   int count=0;bool outstanding=false;int pause=0;
   while(!model.Halt&&count<4000)
   {
    int rvalid=count%5==0?1:0,ready=count%3==0?1:0;t.Set("rx_valid",rvalid);t.Set("tx_ready",ready);
    if(stepping){if(!outstanding&&pause==0){t.Set("step",1);outstanding=true;}else if(pause==1)t.Set("step",0);}
    int sent=t.Read("tx_valid")&ready,got=t.Read("rx_ready")&rvalid,data=t.Read("tx_data"),oldpc=model.Pc;
    t.Tick();count++;
    if(t.Read("retired")!=0||t.Read("fault")!=0)
    {
     model.Execute(rom,rvalid|(ready<<1));Check(t.Read("fault")== (model.Fault?1:0),name+" fault");
     if(!model.Fault){Check(t.Read("retired")==1&&t.Read("last_pc")==oldpc,name+" retirement");retirements++;}
     Check(t.Read("pc")==model.Pc,name+" PC");Check(t.Read("leds")==model.Led,name+" LEDs");Check(t.Read("halted")== (model.Halt?1:0),name+" halt");
     for(int i=0;i<8;i++)Check(t.Read("r"+i)==model.R[i],name+" R"+i);
     Check(sent==(model.Tx>=0?1:0)&&got==model.Rx,name+" peripheral transfer");if(sent!=0)Check(data==model.Tx,name+" tx byte");
     if(sent!=0)txBytes.Add((byte)data);tx+=sent;rx+=got;if(stepping){outstanding=false;pause=6;}
    }
    else {Check(sent==0&&got==0,name+" no extra peripheral effect");if(stepping&&!outstanding){Check(t.Read("pc")==model.Pc,name+" held step must not repeat");pause--;}}
   }
   Check(model.Halt,name+" progress limit");for(int j=0;j<4;j++){t.Tick();Check(t.Read("halted")==1&&t.Read("retired")==0,name+" halted hold");}
  }
  if(expectedTx!=null)Check(Encoding.ASCII.GetString(txBytes.ToArray())==expectedTx,name+" UART byte sequence; got "+Encoding.ASCII.GetString(txBytes.ToArray()));
  t.Save();
 }
 static void RunCompiledHello(ushort[] rom)
 {
  var m=TinyRisc.Build(rom,"CompiledHello");
  var reference=m.Simulate();
  var jit=new CompiledSequentialSimulator(m);
  var ports=m.Ports.ToArray();
  Signal Port(string name)=>ports.Single(p=>p.Name==name);
  void SetBoth(string name,int value){var p=Port(name);reference.Set(p,value);jit.Set(p,value);}
  void Compare(int edge){
   foreach(var p in ports.Where(p=>p.IsOutput))
    if(reference.Read(p)!=jit.Read(p))
      throw new Exception("Compiled TinyRisc mismatch edge "+edge+" port "+p.Name+
        " interpreted="+reference.Read(p)+" compiled="+jit.Read(p));
  }
  var bytes=new System.Collections.Generic.List<byte>();
  reference.Tick(true);jit.Tick(true);
  SetBoth("run",1);SetBoth("step",0);SetBoth("switches",0);
  SetBoth("rx_valid",0);SetBoth("rx_data",0);
  for(int edge=0;edge<20000;edge++)
  {
   // Repeatable stalls, including sustained not-ready periods.
   int ready=(edge%13<5)?0:1;
   SetBoth("tx_ready",ready);
   int accepted=ready!=0 && reference.Read(Port("tx_valid"))!=0?1:0;
   int octet=(int)reference.Read(Port("tx_data"));
   reference.Tick();jit.Tick();Compare(edge);
   if(accepted!=0)bytes.Add((byte)octet);
   if(reference.Read(Port("halted"))!=0)
   {
    if(reference.Read(Port("fault"))!=0)throw new Exception("TinyRisc hardware fault");
    var message=Encoding.ASCII.GetString(bytes.ToArray());
    Check(message=="Hello, world!\r\n","Compiled UART payload was "+message);
    Console.WriteLine("PASS compiled vs interpreter TinyRisc: "+(edge+1)+
      " clock edges, "+bytes.Count+" UART bytes; no fault");
    return;
   }
  }
  throw new Exception("Compiled TinyRisc did not halt by 20000 edges");
 }
 static void BenchmarkCompiledCpu(ushort[] rom)
 {
  var m=TinyRisc.Build(rom,"BenchmarkTinyRisc");
  File.WriteAllText("generated/tinyrisc/BenchmarkTinyRisc.v",m.Verilog());
  var reference=m.Simulate();
  var stopwatch=System.Diagnostics.Stopwatch.StartNew();
  var jit=new CompiledSequentialSimulator(m);
  stopwatch.Stop();var compileMs=stopwatch.Elapsed.TotalMilliseconds;
  var ports=m.Ports.ToDictionary(p=>p.Name,p=>p);
  void Input(Simulator a,CompiledSequentialSimulator b,string name,int value)
  {a.Set(ports[name],value);b.Set(ports[name],value);}
  reference.Tick(true);jit.Tick(true);
  foreach(var p in new[]{"run","step","switches","rx_valid","rx_data"})
    Input(reference,jit,p,p=="run"?1:0);
  const int edges=10000;
  // Periodically reset to execute active CPU instructions throughout timing.
  // Counts reset cycles; this is not 10k independent program completions.
  Input(reference,jit,"tx_ready",1);
  for(int i=0;i<100;i++){reference.Tick();jit.Tick();}
  var sw=System.Diagnostics.Stopwatch.StartNew();
  for(int i=0;i<edges;i++)reference.Tick(i%128==0);
  sw.Stop();double interpreted=sw.Elapsed.TotalSeconds;
  sw.Restart();
  for(int i=0;i<edges;i++)jit.Tick(i%128==0);
  sw.Stop();double compiled=sw.Elapsed.TotalSeconds;
  foreach(var p in ports.Values.Where(p=>p.IsOutput))
    Check(reference.Read(p)==jit.Read(p),"CPU benchmark mismatch "+p.Name);
  Console.WriteLine("BENCH TinyRisc repeating ROM: compiled "+(edges/compiled).ToString("F0")+
    " edges/sec; interpreter "+(edges/interpreted).ToString("F0")+
    " edges/sec; ratio "+(interpreted/compiled).ToString("F3")+
    "x; JIT construction "+compileMs.ToString("F2")+" ms");
 }
 static void Main(string[] args)
 {
  Directory.CreateDirectory("generated/tinyrisc");var demo=File.ReadAllLines("generated/tinyrisc/count.hex").Select(x=>Convert.ToUInt16(x,16)).ToArray();Run(demo,"CountRun");Run(demo,"CountStep",true);
  var hello=File.ReadAllLines("generated/tinyrisc/hello_world.hex").Select(x=>Convert.ToUInt16(x,16)).ToArray();Run(hello,"HelloWorld",expectedTx:"Hello, world!\r\n");
  if(args.Contains("--compiled-tinyrisc")) RunCompiledHello(hello);
  if(args.Contains("--benchmark-compiled-tinyrisc")) BenchmarkCompiledCpu(hello);
  var random=new Random(93);var code=new System.Collections.Generic.List<ushort>();for(int i=1;i<8;i++)code.Add(I(8,i,0,random.Next(512)));for(int i=0;i<180;i++){int op=random.Next(9);code.Add(op<=6?I(op,random.Next(8),random.Next(8),random.Next(8)<<3):op==7?I(op,random.Next(8),random.Next(8),random.Next(64)):I(op,random.Next(8),0,random.Next(512)));}code.Add(0xf000);Run(code.ToArray(),"RandomAlu");
  Run(new[]{I(8,1,0,256),I(9,2,1,1),I(10,2,1,2),I(9,3,1,3),I(9,4,1,4),I(10,3,1,0),(ushort)0xf000},"PeripheralStalls",true);
  Run(new[]{I(8,1,0,3),I(13,1,1),I(8,2,0,99),I(0,3,1,0),(ushort)0xf000},"JumpAlias");
  Run(new[]{I(9,1,0,0),I(7,1,1,1),I(10,1,0,0),(ushort)0xf000},"ResetRetainsRam",false,true);
  Run(new ushort[]{0xe000},"IllegalOpcode");Run(new ushort[]{0x0001},"IllegalEncoding");Run(new[]{I(8,1,0,256),I(9,2,1,5)},"BadAddress");Run(new ushort[]{0xf001},"RomBounds");
  Run(new[]{I(8,1,0,1),I(11,1,0,1),I(11,1,1,1),(ushort)0xe000,(ushort)0xf000},"Branches");
  string report="PASS "+checks+" independent checks; "+retirements+" retired instructions; "+cycles+" traced edges; 12 RTL benches.\n";File.WriteAllText("generated/tinyrisc/verification.txt",report);Console.Write(report);
 }
}
