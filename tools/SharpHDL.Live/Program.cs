using System.Numerics;
using SharpHDL;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8093");
var app=builder.Build();
var search=new DirectoryInfo(app.Environment.ContentRootPath);
while(search!=null && !Directory.Exists(Path.Combine(search.FullName,"docs","pipeline")))
    search=search.Parent;
if(search==null)throw new DirectoryNotFoundException("Cannot locate SharpHDL docs/pipeline from the working directory.");
var root=Path.Combine(search.FullName,"docs","pipeline");
app.UseStaticFiles(new StaticFileOptions{FileProvider=new Microsoft.Extensions.FileProviders.PhysicalFileProvider(root)});

var m=new Module("BrowserPipeline");
var input=HwStream<ushort>.Input(m,"input");
var output=HwStream<ushort>.Output(m,"output");
var expression=((input.Data+Expr.Const(16,3)).Named("bias")*Expr.Const(16,5)).Named("gain");
var handle=StructuralPipeline.Attach(m,"pipeline",input,output,expression,new PipelineOptions{LogicLevelsPerStage=1});
var sim=m.Simulate();
var probe=new PipelineSignalProbe(handle,input.Valid,input.Ready,output.Valid,output.Ready);
var gate=new object();
long cycle=0;
var sequence=0L;
long?[] stageTokens=new long?[handle.Latency];
var pending=new Queue<long>();
// Bounded in-process checkpoint history. Each entry precedes one hardware edge.
const int MaxRewindEdges=128;
var rewind=new Stack<LiveCheckpoint>();
LiveCheckpoint Capture()=>new(sim.CaptureSnapshot(),cycle,sequence,
    (long?[])stageTokens.Clone(),pending.ToArray());
void Restore(LiveCheckpoint checkpoint){
    sim.RestoreSnapshot(checkpoint.Simulator);
    cycle=checkpoint.Cycle;sequence=checkpoint.Sequence;
    stageTokens=(long?[])checkpoint.StageTokens.Clone();
    pending.Clear();foreach(var id in checkpoint.Pending)pending.Enqueue(id);
}
void Reset(){
    sim.Set(input.Valid,0);sim.Set(input.Data,0);sim.Set(output.Ready,0);
    sim.Tick(true);cycle=0;pending.Clear();sequence=0;Array.Clear(stageTokens,0,stageTokens.Length);rewind.Clear();
}
Reset();
object Snapshot(){
    var s=probe.Sample(sim,cycle);
    return new {cycle,graph=handle.DataflowGraph,inputReady=s.InputReady,outputValid=s.OutputValid,
      inputAccepted=s.InputAccepted,outputAccepted=s.OutputAccepted,inputStalled=s.InputStalled,
      outputStalled=s.OutputStalled,stageOccupied=s.StageOccupied,
      outputData=sim.Read(output.Data).ToString(),pendingTokens=pending.ToArray(),stageTokenIds=stageTokens,canStepBack=rewind.Count>0,rewindDepth=rewind.Count};
}
app.MapGet("/api/state",()=>{lock(gate)return Results.Json(Snapshot());});
app.MapPost("/api/reset",()=>{lock(gate){Reset();return Results.Json(Snapshot());}});
app.MapPost("/api/stepBack",()=>{lock(gate){
    if(rewind.Count==0)return Results.Conflict("No checkpoint available for rewind.");
    Restore(rewind.Pop());
    return Results.Json(new {state=Snapshot()});
}});
app.MapPost("/api/step",(StepCommand cmd)=>{
    lock(gate){
      if(cmd.Data is <0 or >65535)return Results.BadRequest("Data must fit in 16 bits.");
      var checkpoint=Capture();
      sim.Set(input.Valid,cmd.Valid?1:0);
      sim.Set(input.Data,cmd.Data);
      sim.Set(output.Ready,cmd.Ready?1:0);
      var before=probe.Sample(sim,cycle);
      var prior=(long?[])stageTokens.Clone();
      var occupied=before.StageOccupied.ToArray();
      var ready=new bool[handle.Latency];
      for(int i=ready.Length-1;i>=0;i--)
          ready[i]=!occupied[i] || (i==ready.Length-1?before.OutputReady:ready[i+1]);
      if(before.OutputAccepted){
          if(pending.Count==0 || prior[prior.Length-1]!=pending.Dequeue())
              throw new InvalidOperationException("Output token tracking diverged from simulator.");
      }
      long? incoming=null;
      if(before.InputAccepted){incoming=++sequence;pending.Enqueue(incoming.Value);}
      for(int i=ready.Length-1;i>=0;i--){
          if(!ready[i])continue;
          stageTokens[i]=i==0?(before.InputValid?incoming:null)
              :(occupied[i-1]?prior[i-1]:null);
      }
      sim.Tick();cycle++;
      for(int i=0;i<stageTokens.Length;i++)
          if((stageTokens[i].HasValue)!=(sim.Read(handle.StageValid[i])!=BigInteger.Zero))
              throw new InvalidOperationException("Stage token tracking diverged at stage "+(i+1));
      if(rewind.Count==MaxRewindEdges){
          var history=rewind.Reverse().Skip(1).ToArray();
          rewind.Clear();foreach(var saved in history)rewind.Push(saved);
      }
      rewind.Push(checkpoint);
      return Results.Json(new {state=Snapshot(),transfer=new {before.InputAccepted,before.OutputAccepted,
       before.InputStalled,before.OutputStalled,cycle=cycle-1}});
    }
});
app.MapGet("/",()=>Results.Redirect("/index.html"));
app.Run();

public sealed record StepCommand(bool Valid,int Data,bool Ready);

// Server-only snapshot. Neither the browser nor VS Code can mutate hardware state directly.
public sealed record LiveCheckpoint(SimulatorSnapshot Simulator,long Cycle,long Sequence,
    long?[] StageTokens,long[] Pending);
