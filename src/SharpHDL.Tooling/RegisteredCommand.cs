namespace SharpHDL.Tooling;

/// <summary>A typed command registration shared by console and protocol adapters.</summary>
/// <param name="Descriptor">Stable discovery metadata.</param>
/// <param name="InvokeAsync">The validated operation, including cancellation and retained outcomes.</param>
public sealed record RegisteredCommand<TRequest, TResult>(
    ToolCommandDescriptor Descriptor,
    Func<TRequest, CancellationToken, Task<TResult>> InvokeAsync);
