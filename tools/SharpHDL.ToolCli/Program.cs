using SharpHDL.Tooling;
using System.Globalization;

// Structured argv transport: no command string, shell, or shell escaping layer.
if(args.Length<5||args[0]!="run")throw new ArgumentException("Usage: run <new-artifact-directory> <working-directory> <timeout-ms> <executable-or-name> [arguments...]");
string executable=Path.IsPathFullyQualified(args[4])?args[4]:HdlToolRunner.FindExecutable(args[4])??Path.GetFullPath(args[4]);
using var cancelled=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancelled.Cancel();};
var result=await HdlToolRunner.RunAsync(executable,args[5..],args[2],args[1],
    TimeSpan.FromMilliseconds(int.Parse(args[3],CultureInfo.InvariantCulture)),cancelled.Token);
Console.WriteLine($"{result.Status}: {executable}; evidence {Path.GetFullPath(args[1])}");
return result.Status switch{ToolRunStatus.Success=>0,ToolRunStatus.TimedOut=>124,ToolRunStatus.Cancelled=>130,
    ToolRunStatus.MissingExecutable=>127,ToolRunStatus.OutputLimitExceeded=>125,_=>result.ExitCode is >0 and <124?result.ExitCode.Value:1};
