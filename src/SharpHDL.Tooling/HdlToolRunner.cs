using System.Diagnostics;
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
    OutputLimitExceeded
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
    /// IO/cleanup failures propagate. The token is reported in the manifest rather than thrown.
    /// </summary>
    public static async Task<ToolRunManifest> RunAsync(string executable,IReadOnlyList<string> arguments,
        string workingDirectory,string artifactDirectory,TimeSpan timeout,CancellationToken cancellationToken=default,
        long maximumOutputBytes=1_048_576)
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
        using var stdout=new FileStream(Path.Combine(artifactDirectory,"stdout.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        using var stderr=new FileStream(Path.Combine(artifactDirectory,"stderr.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
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
                process.StandardInput.Close();
                // Drain both pipes concurrently. Retain raw bytes and continue only until the
                // first bounded-output violation; never buffer arbitrarily large output in RAM.
                async Task Pump(Stream input,Stream output,bool isOut)
                {
                    byte[] buffer=new byte[8192];long count=0;
                    while(true)
                    {
                        int read=await input.ReadAsync(buffer,linked.Token);if(read==0)break;
                        int retained=(int)Math.Min(read,maximumOutputBytes-count);
                        if(retained>0)await output.WriteAsync(buffer.AsMemory(0,retained),CancellationToken.None);
                        count+=retained;if(isOut)outCount=count;else errCount=count;
                        if(retained<read){overflow.Cancel();break;}
                    }
                }
                var drains=Task.WhenAll(Pump(process.StandardOutput.BaseStream,stdout,true),Pump(process.StandardError.BaseStream,stderr,false));
                try{await Task.WhenAll(process.WaitForExitAsync(linked.Token),drains).WaitAsync(linked.Token);}
                catch(OperationCanceledException) when(linked.IsCancellationRequested)
                {
                    status=overflow.IsCancellationRequested?ToolRunStatus.OutputLimitExceeded:
                        cancellationToken.IsCancellationRequested?ToolRunStatus.Cancelled:ToolRunStatus.TimedOut;
                    if(!process.HasExited)
                    {
                        try{process.Kill(entireProcessTree:true);}
                        catch(InvalidOperationException) when(process.HasExited){ /* Exit raced cancellation. */ }
                    }
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    try{await drains.WaitAsync(TimeSpan.FromSeconds(10));}
                    catch(OperationCanceledException) when(linked.IsCancellationRequested){ /* Both pumps are observed after cancellation. */ }
                }
                exit=process.ExitCode;
                if(status==ToolRunStatus.Success&&exit!=0)status=ToolRunStatus.NonzeroExit;
            }
            else if(status==ToolRunStatus.Success)throw new InvalidOperationException("Process.Start returned false.");
        }
        await stdout.FlushAsync();await stderr.FlushAsync();
        var manifest=new ToolRunManifest(executable,argv,Path.GetFullPath(workingDirectory),hash,started,timer.Elapsed.TotalMilliseconds,
            RuntimeInformation.OSDescription,Environment.Version.ToString(),status,exit,error,outCount,errCount,timeout.TotalMilliseconds,maximumOutputBytes);
        await using var record= new FileStream(Path.Combine(artifactDirectory,"manifest.json"),FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(record,manifest,ToolJsonContext.Default.ToolRunManifest);
        return manifest;
    }

    /// <summary>Resolve a simple executable name using PATH; returns an absolute existing path or null. Shell scripts are not implicitly interpreted.</summary>
    public static string? FindExecutable(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny([Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar])>=0)
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
