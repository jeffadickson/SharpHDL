using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpHDL
{
    /// <summary>
    /// Exact source-to-generated-RTL provenance for CIL-scheduled pipelines.
    /// Never invents a source location: requires Portable PDB sequence points,
    /// a named scheduled expression, and an exact emitted Verilog declaration.
    /// </summary>
    public static class HardwareSourceProvenance
    {
        static string Relative(string root, string file)
        {
            var fullRoot = Path.GetFullPath(root);
            var fullFile = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(fullRoot,file));
            var relative = Path.GetRelativePath(fullRoot,fullFile).Replace('\\','/');
            if (relative==".." || relative.StartsWith("../",StringComparison.Ordinal) ||
                Path.IsPathRooted(relative)) return null;
            return relative;
        }

        static string Sanitize(string name)
        {
            var safe=Regex.Replace(name,"[^A-Za-z0-9_]","_");
            return safe.Substring(0,Math.Min(64,safe.Length));
        }

        /// <summary>
        /// Writes Verilog and a version-1 JSON sidecar at rtlPath + ".source-map.json".
        /// Only mapped operations actually emitted as RTL wires are included.
        /// The returned JSON can be loaded by the SharpHDL VS Code extension.
        /// </summary>
        public static string Write(Module module, string rtlPath, string workspaceRoot)
        {
            if(module==null)throw new ArgumentNullException(nameof(module));
            if(string.IsNullOrWhiteSpace(rtlPath))throw new ArgumentException("RTL path required",nameof(rtlPath));
            if(string.IsNullOrWhiteSpace(workspaceRoot))throw new ArgumentException("Workspace root required",nameof(workspaceRoot));
            var relativeRtl=Relative(workspaceRoot,rtlPath);
            if(relativeRtl==null)throw new ArgumentException("RTL output must remain inside workspace",nameof(rtlPath));
            var fullRtl=Path.GetFullPath(Path.Combine(workspaceRoot,relativeRtl));
            var rtl=module.Verilog();
            var lines=rtl.Split('\n');
            var declarations=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            for(int i=0;i<lines.Length;i++)
            {
                // The writer declares a unique net once; don't mistake comments
                // or arbitrary textual occurrences for generated hardware nodes.
                var match=Regex.Match(lines[i],@"^wire\s+\[[0-9]+:0\]\s+(n_[A-Za-z0-9_]+);\s*$");
                if(!match.Success)continue;
                var name=match.Groups[1].Value;
                if(!declarations.TryGetValue(name,out var locations))
                    declarations[name]=locations=new List<int>();
                locations.Add(i+1);
            }
            var mappings=new List<object>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var graph in module.SourceGraphs)
            foreach(var node in graph.Nodes)
            {
                if(node.SourceFile==null || !node.SourceLine.HasValue ||
                   !node.IlOffset.HasValue || !node.Name.Contains("_IL_"))continue;
                var source=Relative(workspaceRoot,node.SourceFile);
                if(source==null)continue;
                var name="n_"+Sanitize(module.SourceGraphPrefixes[graph]+"_stage"+node.Stage+"_"+node.Name);
                if(!declarations.TryGetValue(name,out var locations) || locations.Count!=1)continue;
                var key=source+":"+node.SourceLine.Value+":"+relativeRtl+":"+locations[0];
                if(!seen.Add(key))continue;
                mappings.Add(new {
                    source=new {file=source,line=node.SourceLine.Value},
                    rtl=new {file=relativeRtl,line=locations[0]},
                    signals=new[]{name},
                    stage=node.Stage,ilOffset=node.IlOffset.Value
                });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fullRtl));
            File.WriteAllText(fullRtl,rtl);
            var json=JsonSerializer.Serialize(new {version=1,mappings},new JsonSerializerOptions{WriteIndented=true});
            File.WriteAllText(fullRtl+".source-map.json",json);
            return json;
        }
    }
}
