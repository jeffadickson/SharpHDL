namespace SharpHDL.Benchmarking;

/// <summary>SharpHDL profile switches layered over BenchmarkDotNet's own argument parser.</summary>
public sealed class BaselineRunOptions
{
    private BaselineRunOptions(bool qualified,bool native,string[] arguments)
    { Qualified=qualified;IncludeNativeAot=native;Arguments=Array.AsReadOnly(arguments); }
    /// <summary>Whether to use the controlled observation workload profile.</summary>
    public bool Qualified { get; }
    /// <summary>Explicit opt-in only; no native job is selected by default.</summary>
    public bool IncludeNativeAot { get; }
    /// <summary>Unmodified BenchmarkDotNet arguments, excluding SharpHDL's switches.</summary>
    public IReadOnlyList<string> Arguments { get; }
    /// <summary>Two APIs times two RAM sizes, optionally repeated for Native AOT.</summary>
    public int ExpectedMeasurementCount=>IncludeNativeAot?8:4;
    /// <summary>Reject ambiguous profile switches while preserving BenchmarkDotNet's argument order.</summary>
    public static BaselineRunOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if(arguments.Any(a=>a is null))throw new ArgumentException("Benchmark arguments cannot contain null.",nameof(arguments));
        int qualified=arguments.Count(a=>a=="--qualified-baseline"),native=arguments.Count(a=>a=="--include-native-aot");
        if(qualified>1||native>1)throw new ArgumentException("Benchmark profile switches cannot be repeated.",nameof(arguments));
        if(native!=0&&qualified==0)throw new ArgumentException("--include-native-aot requires --qualified-baseline.",nameof(arguments));
        return new(qualified!=0,native!=0,arguments.Where(a=>a is not ("--qualified-baseline" or "--include-native-aot")).ToArray());
    }
}
