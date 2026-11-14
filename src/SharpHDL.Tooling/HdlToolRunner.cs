using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpHDL.Tooling;

/// <summary>Execution outcomes; only Success denotes a zero exit without truncated evidence.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ToolRunStatus>))]
public enum ToolRunStatus
{
    /// <summary>Exited zero with complete output.</summary>
    Success,
    /// <summary>Exited with a nonzero code.</summary>
    NonzeroExit,
    /// <summary>The wall-clock budget expired.</summary>
    TimedOut,
    /// <summary>The caller cancelled execution.</summary>
    Cancelled,
    /// <summary>The explicitly selected executable does not exist.</summary>
    MissingExecutable,
    /// <summary>The OS rejected process creation.</summary>
    FailedToStart,
    /// <summary>One output stream exceeded its retained-byte budget.</summary>
    OutputLimitExceeded,
    /// <summary>After launch, a process pipe or evidence stream could not be read or written.</summary>
    EvidenceIoFailure
}

/// <summary>Serialized invocation snapshot. Arguments are recorded verbatim; environment values are not recorded. Budgets are milliseconds and bytes per stream.</summary>
public sealed record ToolRunManifest(string Executable,string[] Arguments,string WorkingDirectory,
    string? ExecutableSha256,DateTime StartedUtc,double ElapsedMilliseconds,string Platform,string Runtime,
    ToolRunStatus Status,int? ExitCode,string? Error,long StdoutBytes,long StderrBytes,double TimeoutMilliseconds,long MaximumOutputBytes);

/// <summary>Shell-free external HDL-tool execution with bounded evidence and a hard cancellation policy.</summary>
public static class HdlToolRunner
{
    /// <summary>
    /// Run an absolute executable with distinct arguments and closed stdin. Retain stdout.bin,
    /// stderr.bin and manifest.json in a new directory whose parent must exist. Timeout includes
    /// output drainage. Cancellation kills the ordinary process tree; detached/reparented daemons
    /// are outside this contract. Output-limit truncation is a failure, never successful evidence.
    /// Pipe and evidence I/O failures after launch are reported when the manifest remains writable.
    /// Setup, cleanup and manifest publication failures propagate.
    /// </summary>
    public static Task<ToolRunManifest> RunAsync(string executable,IReadOnlyList<string> arguments,
        string workingDirectory,string artifactDirectory,TimeSpan timeout,CancellationToken cancellationToken=default,
        long maximumOutputBytes=1_048_576) =>
        RunCoreAsync(executable,arguments,workingDirectory,artifactDirectory,timeout,cancellationToken,maximumOutputBytes);

    internal static async Task<ToolRunManifest> RunCoreAsync(string executable,IReadOnlyList<string> arguments,
        string workingDirectory,string artifactDirectory,TimeSpan timeout,CancellationToken cancellationToken,
        long maximumOutputBytes,Func<ReadOnlyMemory<byte>,CancellationToken,ValueTask>? writeStdout=null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] argv=arguments.ToArray();
        if(!Path.IsPathFullyQualified(executable)||argv.Any(a=>a is null||a.Contains('\0'))||executable.Contains('\0'))
            throw new ArgumentException("Use an absolute executable and NUL-free arguments.");
        if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromHours(1))throw new ArgumentOutOfRangeException(nameof(timeout));
        if(maximumOutputBytes is <1 or >67_108_864)throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
        if(!Directory.Exists(workingDirectory))throw new DirectoryNotFoundException(workingDirectory);
        artifactDirectory=Path.GetFullPath(artifactDirectory);
        if(!Directory.Exists(Path.GetDirectoryName(artifactDirectory)))throw new DirectoryNotFoundException("Missing artifact parent.");
        if(Path.Exists(artifactDirectory))throw new IOException("Evidence directory must be new.");
        Directory.CreateDirectory(artifactDirectory);
        using var stdout=new FileStream(Path.Combine(artifactDirectory,"stdout.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,bufferSize:1,useAsync:true);
        using var stderr=new FileStream(Path.Combine(artifactDirectory,"stderr.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read,bufferSize:1,useAsync:true);
        var timer=Stopwatch.StartNew();DateTime started=DateTime.UtcNow;
        ToolRunStatus status=ToolRunStatus.Success;int? exit=null;string? error=null,hash=null;long outCount=0,errCount=0;
        using var deadline=new CancellationTokenSource(timeout);
        using var overflow=new CancellationTokenSource();
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,deadline.Token,overflow.Token);
        if(!File.Exists(executable)){status=ToolRunStatus.MissingExecutable;error="Executable not found.";}
        else if(cancellationToken.IsCancellationRequested)status=ToolRunStatus.Cancelled;
        else
        {
            try
            {
                await using var image=File.OpenRead(executable);
                hash=Convert.ToHexString(await SHA256.HashDataAsync(image,linked.Token));
            }
            catch(OperationCanceledException) when(linked.IsCancellationRequested)
            {status=cancellationToken.IsCancellationRequested?ToolRunStatus.Cancelled:ToolRunStatus.TimedOut;}
            using var process=new Process {StartInfo=new ProcessStartInfo(executable){WorkingDirectory=Path.GetFullPath(workingDirectory),
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            foreach(string arg in argv)process.StartInfo.ArgumentList.Add(arg);
            bool running=false;
            try{if(status==ToolRunStatus.Success)running=process.Start();}
            catch(System.ComponentModel.Win32Exception ex){status=ToolRunStatus.FailedToStart;error=ex.Message;}
            if(running)
            {
                var pipeFailure=new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                // Drain both pipes concurrently. Retain raw bytes and continue only until the
                // first bounded-output violation; never buffer arbitrarily large output in RAM.
                async Task Pump(Stream input,Stream output,bool isOut)
                {
                    try
                    {
                        byte[] buffer=new byte[8192];long count=0;
                        while(true)
                        {
                            int read=await input.ReadAsync(buffer,linked.Token);if(read==0)break;
                            int retained=(int)Math.Min(read,maximumOutputBytes-count);
                            if(retained>0)
                            {
                                var bytes=buffer.AsMemory(0,retained);
                                if(isOut&&writeStdout is not null)await writeStdout(bytes,linked.Token);
                                else await output.WriteAsync(bytes,linked.Token);
                            }
                            count+=retained;if(isOut)outCount=count;else errCount=count;
                            if(retained<read){overflow.Cancel();break;}
                        }
                    }
                    catch(Exception failure) when(failure is not OperationCanceledException || !linked.IsCancellationRequested)
                    {
                        pipeFailure.TrySetResult(failure);
                        throw;
                    }
                }

                Task completion=Task.CompletedTask;
                try
                {
                    completion=process.WaitForExitAsync();
                    process.StandardInput.Close();
                    completion=Task.WhenAll(completion,Pump(process.StandardOutput.BaseStream,stdout,true),
                        Pump(process.StandardError.BaseStream,stderr,false));
                    var first=await Task.WhenAny(completion,pipeFailure.Task).WaitAsync(linked.Token);
                    if(first==pipeFailure.Task)ExceptionDispatchInfo.Throw(await pipeFailure.Task);
                    await completion;
                }
                catch(Exception failure)
                {
                    bool cancelled=failure is OperationCanceledException&&linked.IsCancellationRequested;
                    linked.Cancel();
                    // A faulted pump cannot wait for normal child exit. Kill first, then
                    // observe both pipes and process exit with a bounded cleanup budget.
                    var cleanupErrors=new List<Exception>();
                    try
                    {
                        if(!process.HasExited)process.Kill(entireProcessTree:true);
                    }
                    catch(InvalidOperationException) when(process.HasExited){ /* Exit raced teardown. */ }
                    catch(Exception cleanup){cleanupErrors.Add(cleanup);}
                    try{await completion.WaitAsync(TimeSpan.FromSeconds(10));}
                    catch(Exception) when(completion.IsFaulted||completion.IsCanceled){ /* Classified below. */ }
                    catch(Exception cleanup){cleanupErrors.Add(cleanup);}

                    var failures=completion.Exception?.Flatten().InnerExceptions.ToList()??[];
                    if(!cancelled&&!failures.Contains(failure))failures.Add(failure);
                    if(cleanupErrors.Count!=0)
                        throw new AggregateException("Tool execution and cleanup failed.",failures.Append(failure).Concat(cleanupErrors).Distinct());
                    if(failures.Count!=0)
                    {
                        if(failures.Any(e=>e is not (IOException or UnauthorizedAccessException)))
                            throw new AggregateException("Unexpected tool execution failure.",failures);
                        status=ToolRunStatus.EvidenceIoFailure;
                        error=string.Join(Environment.NewLine,failures.Select(e=>e.Message));
                    }
                    else
                    {
                        status=overflow.IsCancellationRequested?ToolRunStatus.OutputLimitExceeded:
                            cancellationToken.IsCancellationRequested?ToolRunStatus.Cancelled:ToolRunStatus.TimedOut;
                    }
                }
                exit=process.ExitCode;
                if(status==ToolRunStatus.Success&&overflow.IsCancellationRequested)status=ToolRunStatus.OutputLimitExceeded;
                if(status==ToolRunStatus.Success&&exit!=0)status=ToolRunStatus.NonzeroExit;
            }
            else if(status==ToolRunStatus.Success)throw new InvalidOperationException("Process.Start returned false.");
        }
        foreach(var stream in new[]{stdout,stderr})
        {
            try{await stream.FlushAsync();}
            catch(Exception failure) when(failure is IOException or UnauthorizedAccessException)
            {
                status=ToolRunStatus.EvidenceIoFailure;
                error=error is null?failure.Message:error+Environment.NewLine+failure.Message;
            }
        }
        var manifest=new ToolRunManifest(executable,argv,Path.GetFullPath(workingDirectory),hash,started,timer.Elapsed.TotalMilliseconds,
            RuntimeInformation.OSDescription,Environment.Version.ToString(),status,exit,error,outCount,errCount,timeout.TotalMilliseconds,maximumOutputBytes);
        string pending=Path.Combine(artifactDirectory,"manifest.json.pending");
        await using (var record=new FileStream(pending,FileMode.CreateNew))
            await JsonSerializer.SerializeAsync(record,manifest,ToolJsonContext.Default.ToolRunManifest);
        // Observers can discover retained cancellation evidence before the
        // command response. Existence must imply a closed, complete record.
        File.Move(pending,Path.Combine(artifactDirectory,"manifest.json"));
        return manifest;
    }

    /// <summary>Resolve a simple executable name using PATH; returns an absolute existing path or null. Shell scripts are not implicitly interpreted.</summary>
    public static string? FindExecutable(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||Path.IsPathRooted(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)
            throw new ArgumentException("Expected a simple executable name.",nameof(name));
        foreach(string entry in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
        {
            if(string.IsNullOrWhiteSpace(entry)||!Directory.Exists(entry))continue;
            string file=Path.Combine(entry,OperatingSystem.IsWindows()&&!name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name+".exe":name);
            if(File.Exists(file))return Path.GetFullPath(file);
        }
        return null;
    }
}

[JsonSourceGenerationOptions(WriteIndented=true)]
[JsonSerializable(typeof(ToolRunManifest))]
internal partial class ToolJsonContext:JsonSerializerContext;
