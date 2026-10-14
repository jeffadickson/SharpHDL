using System;

namespace SharpHDL
{
    /// <summary>Explicit stateless structural expression pipeline (single input token).</summary>
    public static class StructuralPipeline
    {
        public static PipelineHandle Attach(Module module, string name, HwChannel input, HwChannel output,
            Expr transform, PipelineOptions options = null)
        {
            if (module == null || input == null || output == null || transform == null)
                throw new ArgumentNullException("A module, channels and transform are required.");
            if (input.Data.Width != output.Data.Width || transform.Width != output.Data.Width)
                throw new ArgumentException("Input, output and expression widths must match.");
            return PipelineScheduler.Attach(module, name, input, output, transform, options);
        }
    }
}
