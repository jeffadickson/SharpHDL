using System;
using System.Collections.Generic;

namespace SharpHDL.Examples;

/// <summary>
/// Cycle-stepped, unsigned four-tap FIR transaction reference.
/// This is an independent software oracle, not synthesized RTL.
/// Arithmetic wraps modulo 2^16.
/// </summary>
public sealed class AxiFirReference
{
    private readonly ushort[] history = new ushort[3];
    private readonly Queue<(ushort Data, bool Last)> pending = new();
    private readonly int capacity;

    /// <summary>Create a bounded reference with a fixed output queue capacity.</summary>
    public AxiFirReference(int capacity = 4)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    /// <summary>Number of accepted samples not yet emitted.</summary>
    public int Pending => pending.Count;

    /// <summary>Whether the current cycle can accept an input sample.</summary>
    public bool InputReady => pending.Count < capacity;

    /// <summary>Whether the current cycle offers an output sample.</summary>
    public bool OutputValid => pending.Count != 0;

    /// <summary>Current output, stable until the consumer accepts it.</summary>
    public (ushort Data, bool Last) Output =>
        pending.Count != 0 ? pending.Peek() :
        throw new InvalidOperationException("No output is valid.");

    /// <summary>
    /// Advance one clock edge. Output consumption precedes input acceptance,
    /// allowing a full queue to accept a replacement on the same edge.
    /// Coefficients are sampled only when an input handshake occurs.
    /// </summary>
    public void Tick(bool inputValid, ushort inputData, bool inputLast,
        bool outputReady, ReadOnlySpan<ushort> coefficients)
    {
        if (coefficients.Length != 4)
            throw new ArgumentException("Exactly four coefficients required.", nameof(coefficients));

        bool consume = OutputValid && outputReady;
        bool accept = inputValid && (InputReady || consume);
        if (consume) pending.Dequeue();
        if (!accept) return;

        ulong sum = (ulong)inputData * coefficients[0]
                  + (ulong)history[0] * coefficients[1]
                  + (ulong)history[1] * coefficients[2]
                  + (ulong)history[2] * coefficients[3];
        pending.Enqueue(((ushort)sum, inputLast));
        history[2] = history[1];
        history[1] = history[0];
        history[0] = inputData;
    }

    /// <summary>Discard pending transfers and logically clear sample history.</summary>
    public void Reset()
    {
        pending.Clear();
        Array.Clear(history, 0, history.Length);
    }
}
