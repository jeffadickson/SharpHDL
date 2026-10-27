namespace SharpHDL.SymbolFixtures;

/// <summary>Embedded-symbol fixture, independent of a sidecar PDB.</summary>
public static class EmbeddedKernel
{
    /// <summary>Small real C# method with an embedded Portable PDB sequence point.</summary>
    public static uint AddSeven(uint value)=>unchecked(value+7);
}
