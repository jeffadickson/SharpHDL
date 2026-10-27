using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;

namespace SharpHDL
{
    /// <summary>Best-effort Portable PDB sequence point mapping; no inferred source locations.</summary>
    public static partial class PortablePdbSourceMap
    {
        static readonly Regex OffsetPattern = new Regex(@"(?:^|_)IL_([0-9a-fA-F]{4,8})(?:_|$)", RegexOptions.Compiled);

        /// <summary>Parse a nonnegative IL offset from an operation name; invalid/overflowing hints return null.</summary>
        public static int? ParseIlOffset(string operationName)
        {
            var match = OffsetPattern.Match(operationName ?? "");
            return match.Success && int.TryParse(match.Groups[1].Value,NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,out int offset) && offset>=0 ? offset : (int?)null;
        }

        /// <summary>Replace node annotations with matched visible sequence points, clearing stale or hidden locations.</summary>
        public static void Annotate(MethodInfo method, PipelineDataflowGraph graph)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var symbols=Read(method);
            foreach(var node in graph.Nodes)
            {
                var offset=ParseIlOffset(node.Name);
                var source=offset.HasValue?symbols.FindSource(offset.Value):null;
                // Annotation may be repeated: clear stale locations in newly hidden/unmapped regions.
                node.SourceFile=source?.File;node.SourceLine=source?.StartLine;
                node.SourceColumn=source?.StartColumn;node.IlOffset=offset;
            }
        }
    }
}
