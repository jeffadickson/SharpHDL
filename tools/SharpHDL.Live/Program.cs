using System.Numerics;
using SharpHDL;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8093");
var app=builder.Build();
var root=Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath,"../../docs/pipeline"));
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
var pending=new Queue<long>();
void Reset(){
    sim.Set(input.Valid,0);sim.Set(input.Data,0);sim.Set(output.Ready,0);
    sim.Tick(true);cycle=0;pending.Clear();sequence=0;
}
Reset();
object Snapshot(){
    var s=probe.Sample(sim,cycle);
    return new {cycle,graph=handle.DataflowGraph,inputReady=s.InputReady,outputValid=s.OutputValid,
      inputAccepted=s.InputAccepted,outputAccepted=s.OutputAccepted,inputStalled=s.InputStalled,
      outputStalled=s.OutputStalled,stageOccupied=s.StageOccupied,
      outputData=sim.Read(output.Data).ToString(),pendingTokens=pending.ToArray()};
}
app.MapGet("/api/state",()=>{lock(gate)return Results.Json(Snapshot());});
app.MapPost("/api/reset",()=>{lock(gate){Reset();return Results.Json(Snapshot());}});
app.MapPost("/api/step",(StepCommand cmd)=>{
    lock(gate){
      if(cmd.Data is <0 or >65535)return Results.BadRequest("Data must fit in 16 bits.");
      sim.Set(input.Valid,cmd.Valid?1:0);
      sim.Set(input.Data,cmd.Data);
      sim.Set(output.Ready,cmd.Ready?1:0);
      var before=probe.Sample(sim,cycle);
      if(before.OutputAccepted && pending.Count>0)pending.Dequeue();
      if(before.InputAccepted)pending.Enqueue(++sequence);
      sim.Tick();cycle++;
      return Results.Json(new {state=Snapshot(),transfer=new {before.InputAccepted,before.OutputAccepted,
       before.InputStalled,before.OutputStalled,cycle=cycle-1}});
    }
});
app.MapGet("/",()=>Results.Redirect("/index.html"));
app.Run();

public sealed record StepCommand(bool Valid,int Data,bool Ready);
