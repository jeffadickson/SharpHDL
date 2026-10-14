using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;

namespace SharpHDL
{
    /// <summary>Best-effort Portable PDB sequence point mapping; no inferred source locations.</summary>
    public static class PortablePdbSourceMap
    {
        static readonly Regex OffsetPattern = new Regex(@"(?:^|_)IL_([0-9a-fA-F]{4,8})(?:_|$)", RegexOptions.Compiled);

        public static int? ParseIlOffset(string operationName)
        {
            var match = OffsetPattern.Match(operationName ?? "");
            return match.Success ? Convert.ToInt32(match.Groups[1].Value, 16) : (int?)null;
        }

        public static void Annotate(MethodInfo method, PipelineDataflowGraph graph)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var pdbPath = Path.ChangeExtension(method.Module.FullyQualifiedName, ".pdb");
            if (!File.Exists(pdbPath)) return;
            using (var stream = File.OpenRead(pdbPath))
            using (var provider = MetadataReaderProvider.FromPortablePdbStream(stream))
            {
                var reader = provider.GetMetadataReader();
                var methodRow = MetadataTokens.GetRowNumber(MetadataTokens.Handle(method.MetadataToken));
                var handle = MetadataTokens.MethodDebugInformationHandle(methodRow);
                if (handle.IsNil) return;
                var info = reader.GetMethodDebugInformation(handle);
                var points = info.GetSequencePoints()
                    .Where(p => !p.IsHidden && p.StartLine > 0)
                    .OrderBy(p => p.Offset).ToArray();
                if (points.Length == 0) return;
                foreach (var node in graph.Nodes)
                {
                    var offset = ParseIlOffset(node.Name);
                    if (!offset.HasValue) continue;
                    var match = points.LastOrDefault(p => p.Offset <= offset.Value);
                    if (match.StartLine == 0) continue;
                    var documentHandle = match.Document.IsNil ? info.Document : match.Document;
                    if (documentHandle.IsNil) continue;
                    node.SourceFile = reader.GetString(reader.GetDocument(documentHandle).Name);
                    node.SourceLine = match.StartLine;
                    node.SourceColumn = match.StartColumn;
                    node.IlOffset = offset;
                }
            }
        }
    }
}
