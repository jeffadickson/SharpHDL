using System.Text.Json;
using SharpHDL.Tooling;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Real subprocess tests for transport, bounded pipes, deadline and retained failures.</summary>
public sealed class HdlToolRunnerTests
{
    private static string DirectoryForRun()
    {
        string parent=Path.Combine(Path.GetTempPath(),"sharphdl-tools");
        if(!Directory.Exists(Path.GetTempPath()))throw new DirectoryNotFoundException();
        if(!Directory.Exists(parent))Directory.CreateDirectory(parent);
        return Path.Combine(parent,Guid.NewGuid().ToString("N"));
    }
    private static string[] Arguments(params string[] args)=>new[]{"exec","--runtimeconfig",
        Path.Combine(AppContext.BaseDirectory,"SharpHDL.UnitTests.runtimeconfig.json"),typeof(ToolFixtureMarker).Assembly.Location}.Concat(args).ToArray();
    private static string Dotnet=>HdlToolRunner.FindExecutable("dotnet")??throw new FileNotFoundException("dotnet");

    /// <summary>FsCheck generates/shrinks bounded printable argv; shell punctuation is never interpreted.</summary>
    [FsCheck.Xunit.Property(MaxTest=20,Replay="1145655947,296144285")]
    public async Task<bool> Generated_arguments_round_trip(string[] values)
    {
        string[] bounded=(values??[]).Take(8).Select(s=>new string((s??"").Where(c=>c is >= ' ' and <= '~').Take(128).ToArray())).ToArray();
        string path=DirectoryForRun();var result=await HdlToolRunner.RunAsync(Dotnet,Arguments(new[]{"echo"}.Concat(bounded).ToArray()),
            AppContext.BaseDirectory,path,TimeSpan.FromSeconds(20));
        string captured=await File.ReadAllTextAsync(Path.Combine(path,"stdout.bin"));
        return result.Status==ToolRunStatus.Success&&bounded.SequenceEqual(JsonSerializer.Deserialize<string[]>(captured)!);
    }

    /// <summary>Whitespace, quotes, Unicode and shell metacharacters are individual literal arguments.</summary>
    [Fact]
    public async Task Arguments_and_both_streams_are_retained_without_shell_interpretation()
    {
        string path=DirectoryForRun();string[] values=["a b","\"quote\"","; $(echo attack) & | >","λ",""];
        var result=await HdlToolRunner.RunAsync(Dotnet,Arguments(new[]{"echo"}.Concat(values).ToArray()),AppContext.BaseDirectory,path,TimeSpan.FromSeconds(20));
        Assert.Equal(ToolRunStatus.Success,result.Status);Assert.Equal(0,result.ExitCode);
        Assert.Equal(values,JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(Path.Combine(path,"stdout.bin"))));
        Assert.Equal("stderr evidence",await File.ReadAllTextAsync(Path.Combine(path,"stderr.bin")));
        Assert.Equal(64,result.ExecutableSha256!.Length);Assert.True(File.Exists(Path.Combine(path,"manifest.json")));
        using var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(path,"manifest.json")));
        Assert.Equal(20000,manifest.RootElement.GetProperty("TimeoutMilliseconds").GetDouble());
        Assert.Equal(1048576,manifest.RootElement.GetProperty("MaximumOutputBytes").GetInt64());
        await Assert.ThrowsAsync<IOException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,path,TimeSpan.FromSeconds(1)));
        string empty=DirectoryForRun();Directory.CreateDirectory(empty);
        await Assert.ThrowsAsync<IOException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,empty,TimeSpan.FromSeconds(1)));
    }
    /// <summary>Failures cannot be converted into success by output capture.</summary>
    [Theory][InlineData("fail",ToolRunStatus.NonzeroExit)][InlineData("flood",ToolRunStatus.OutputLimitExceeded)]
    public async Task Failures_and_output_limits_are_explicit(string mode,ToolRunStatus status)
    {
        string path=DirectoryForRun();var result=await HdlToolRunner.RunAsync(Dotnet,Arguments(mode),AppContext.BaseDirectory,path,TimeSpan.FromSeconds(20),maximumOutputBytes:1024);
        Assert.Equal(status,result.Status);Assert.InRange(result.StdoutBytes,0,1024);Assert.InRange(result.StderrBytes,0,1024);
        if(mode=="fail")Assert.Equal(23,result.ExitCode);
    }
    /// <summary>Deadline/caller cancellation terminate a real sleeping process and retain distinct statuses.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Cancellation_and_timeout_are_distinct(bool caller)
    {
        using var token=new CancellationTokenSource();if(caller)token.CancelAfter(500);
        var result=await HdlToolRunner.RunAsync(Dotnet,Arguments("sleep"),AppContext.BaseDirectory,DirectoryForRun(),
            caller?TimeSpan.FromSeconds(20):TimeSpan.FromMilliseconds(500),token.Token);
        Assert.Equal(caller?ToolRunStatus.Cancelled:ToolRunStatus.TimedOut,result.Status);
        Assert.NotNull(result.ExitCode);
    }
    /// <summary>Hard cancellation kills an ordinary descendant, not merely the immediate process.</summary>
    [Fact]
    public async Task Cancellation_terminates_an_ordinary_process_tree()
    {
        string path=DirectoryForRun(),pidFile=Path.Combine(path,"child.pid");
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var task=HdlToolRunner.RunAsync(Dotnet,Arguments("tree",Path.Combine(AppContext.BaseDirectory,"SharpHDL.UnitTests.runtimeconfig.json"),pidFile),
            AppContext.BaseDirectory,path,TimeSpan.FromSeconds(30),cancellation.Token);
        int pid=0;
        try
        {
            for(int attempt=0;attempt<1000&&!File.Exists(pidFile);attempt++)await Task.Delay(10);
            Assert.True(File.Exists(pidFile));pid=int.Parse(await File.ReadAllTextAsync(pidFile));
            cancellation.Cancel();Assert.Equal(ToolRunStatus.Cancelled,(await task).Status);
            for(int attempt=0;attempt<100&&Alive(pid);attempt++)await Task.Delay(10);
            Assert.False(Alive(pid));
        }
        finally
        {
            cancellation.Cancel();await task;
            if(pid!=0&&Alive(pid))System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree:true);
        }
        static bool Alive(int id)
        {try{using var process=System.Diagnostics.Process.GetProcessById(id);return !process.HasExited;}catch(ArgumentException){return false;}}
    }
    /// <summary>Missing tools and malformed requests are distinguishable; no implicit fallback executable.</summary>
    [Fact]
    public async Task Missing_and_invalid_requests_fail_closed()
    {
        Assert.Null(HdlToolRunner.FindExecutable("sharphdl-no-such-tool-9f443"));
        await Assert.ThrowsAsync<ArgumentException>(()=>HdlToolRunner.RunAsync("relative",[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(()=>HdlToolRunner.RunAsync(Dotnet,["a\0b"],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1)));
        string path=DirectoryForRun();var result=await HdlToolRunner.RunAsync(Path.Combine(Path.GetTempPath(),"absent-9f443"),[],AppContext.BaseDirectory,path,TimeSpan.FromSeconds(1));
        Assert.Equal(ToolRunStatus.MissingExecutable,result.Status);Assert.Null(result.ExitCode);
    }
    /// <summary>Pre-cancellation does not launch; OS format/permission failures remain explicit.</summary>
    [Fact]
    public async Task Precancelled_and_nonexecutable_files_do_not_succeed()
    {
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        var result=await HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1),cancelled.Token);
        Assert.Equal(ToolRunStatus.Cancelled,result.Status);Assert.Null(result.ExitCode);
        string file=DirectoryForRun()+".txt";await File.WriteAllTextAsync(file,"Not a native executable.");
        result=await HdlToolRunner.RunAsync(file,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(5));
        Assert.Equal(ToolRunStatus.FailedToStart,result.Status);Assert.NotEmpty(result.Error!);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1),maximumOutputBytes:0));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(()=>HdlToolRunner.RunAsync(Dotnet,[],DirectoryForRun(),DirectoryForRun(),TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(()=>HdlToolRunner.FindExecutable("a/b"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromHours(1)+TimeSpan.FromTicks(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1),maximumOutputBytes:67_108_865));
        await Assert.ThrowsAsync<ArgumentException>(()=>HdlToolRunner.RunAsync(Dotnet,[null!],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(()=>HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,Path.Combine(DirectoryForRun(),"child"),TimeSpan.FromSeconds(1)));
        result=await HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromHours(1),cancelled.Token,67_108_864);
        Assert.Equal(ToolRunStatus.Cancelled,result.Status);
        result=await HdlToolRunner.RunAsync(Dotnet,[],AppContext.BaseDirectory,DirectoryForRun(),TimeSpan.FromSeconds(1),cancelled.Token,1);
        Assert.Equal(ToolRunStatus.Cancelled,result.Status);
    }
}
