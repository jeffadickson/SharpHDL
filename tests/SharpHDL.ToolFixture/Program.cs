using System.Text.Json;

// Dedicated subprocess fixture; no external shell or mock process implementation.
switch(args[0])
{
    case "echo":Console.Write(JsonSerializer.Serialize(args[1..]));Console.Error.Write("stderr evidence");return 0;
    case "fail":Console.Error.Write("expected failure");return 23;
    case "sleep":await Task.Delay(TimeSpan.FromMinutes(5));return 0;
    case "tree":
        var info=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        foreach(string arg in new[]{"exec","--runtimeconfig",args[1],typeof(ToolFixtureMarker).Assembly.Location,"sleep"})info.ArgumentList.Add(arg);
        using(var child=System.Diagnostics.Process.Start(info)??throw new InvalidOperationException("Child did not start."))
        {
            // Publish atomically: existence must mean the PID record is complete,
            // even when the test reader races the asynchronous write on Linux.
            await File.WriteAllTextAsync(args[2]+".pending",child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(args[2]+".pending",args[2]);await child.WaitForExitAsync();
        }
        return 0;
    case "flood":for(int i=0;i<10000;i++){Console.Write(new string('x',1000));Console.Error.Write(new string('y',1000));}return 0;
    default:throw new ArgumentException("Unknown fixture command.");
}

/// <summary>Locates the separately executable fixture assembly from integration tests.</summary>
public sealed class ToolFixtureMarker;
