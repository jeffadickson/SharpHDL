using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SharpHDL.Documentation;

/// <summary>A requirement read from the real StrictDoc JSON exporter, not a replacement SDoc parser.</summary>
public sealed record RequirementEntry(string Uid,string Title);
/// <summary>Manually reviewed trace status and validated repository references; no automatic requirement closure.</summary>
public sealed record TraceEntry(string Uid,string Status,IReadOnlyList<string> Implementation,IReadOnlyList<string> Tests,IReadOnlyList<string> Evidence,string Limitations);

/// <summary>Bounded XML/JSON rendering and trace validation shared by the build driver and negative tests.</summary>
public static class DocumentationCatalog
{
    /// <summary>Read unique REQUIREMENT nodes from StrictDoc's authoritative JSON export, ignoring grammar metadata and sections.</summary>
    public static IReadOnlyList<RequirementEntry> ReadRequirements(string json)
    {
        using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=64});
        var entries=new SortedDictionary<string,RequirementEntry>(StringComparer.Ordinal);
        void Visit(JsonElement node)
        {
            if(node.ValueKind==JsonValueKind.Object)
            {
                if(node.TryGetProperty("_NODE_TYPE",out var kind)&&kind.ValueKind==JsonValueKind.String&&kind.GetString()=="REQUIREMENT")
                {
                    if(!node.TryGetProperty("UID",out var uid)||uid.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Missing or nontext requirement UID.");
                    string id=uid.GetString()!;
                    if(node.TryGetProperty("TITLE",out var heading)&&heading.ValueKind!=JsonValueKind.String)throw new InvalidDataException("Nontext requirement title.");
                    if(string.IsNullOrWhiteSpace(id)||!entries.TryAdd(id,new(id,node.TryGetProperty("TITLE",out var title)?title.GetString()??id:id)))
                        throw new InvalidDataException("Missing/duplicate requirement UID: "+id);
                    if(entries.Count>10000)throw new InvalidDataException("Requirement budget exceeded.");
                }
                foreach(var property in node.EnumerateObject())Visit(property.Value);
            }
            else if(node.ValueKind==JsonValueKind.Array)foreach(var child in node.EnumerateArray())Visit(child);
        }
        Visit(document.RootElement);
        if(entries.Count==0)throw new InvalidDataException("No authoritative requirement nodes found.");
        return Array.AsReadOnly(entries.Values.ToArray());
    }

    /// <summary>Resolve an existing nonsymlink file strictly inside the checked repository; reject traversal and absolute paths.</summary>
    public static string RepositoryFile(string repository,string relative)
    {
        if(!Directory.Exists(repository))throw new DirectoryNotFoundException(repository);
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains('\0'))throw new InvalidDataException("Expected a relative repository file.");
        string root=Path.GetFullPath(repository).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string full=Path.GetFullPath(relative,root);
        if(!full.StartsWith(root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||!File.Exists(full))
            throw new InvalidDataException("Repository reference is missing or escapes its root: "+relative);
        string current=full;
        while(!string.Equals(current.TrimEnd(Path.DirectorySeparatorChar),root.TrimEnd(Path.DirectorySeparatorChar),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
        {
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Symbolic references are unsupported: "+relative);
            current=Path.GetDirectoryName(current)??throw new InvalidDataException("Invalid reference ancestor.");
        }
        return full;
    }

    /// <summary>Validate manually assigned statuses, known UIDs and concrete source/test/evidence references. Unmapped requirements remain unmapped.</summary>
    public static IReadOnlyList<TraceEntry> ReadTrace(string repository,string json,IReadOnlyList<RequirementEntry> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});
        if(document.RootElement.GetProperty("schema").GetInt32()!=1)throw new InvalidDataException("Unsupported trace schema.");
        var known=requirements.Select(r=>r.Uid).ToHashSet(StringComparer.Ordinal);var used=new HashSet<string>(StringComparer.Ordinal);var rows=new List<TraceEntry>();
        foreach(var node in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            string uid=node.GetProperty("uid").GetString()??"",status=node.GetProperty("status").GetString()??"";
            if(!known.Contains(uid)||!used.Add(uid))throw new InvalidDataException("Unknown/duplicate trace UID: "+uid);
            if(status is not ("verified_bounded" or "partial" or "historical" or "draft" or "future"))throw new InvalidDataException("Unknown trace status.");
            string[] Files(string field)=>node.GetProperty(field).EnumerateArray().Select(p=>
            {string file=p.GetString()??throw new InvalidDataException("Null file reference.");RepositoryFile(repository,file);return file;}).ToArray();
            var implementation=Files("implementation");var evidence=Files("evidence");var tests=new List<string>();
            foreach(var test in node.GetProperty("tests").EnumerateArray())
            {
                string file=test.GetProperty("file").GetString()??"",method=test.GetProperty("method").GetString()??"";
                string path=RepositoryFile(repository,file);
                if(string.IsNullOrWhiteSpace(method)||!File.ReadAllText(path).Contains(method,StringComparison.Ordinal))throw new InvalidDataException("Trace test symbol not found: "+method);
                tests.Add(file+" :: "+method);
            }
            string limitations=node.GetProperty("limitations").GetString()??"";
            if(string.IsNullOrWhiteSpace(limitations)||status=="verified_bounded"&&(implementation.Length==0||tests.Count==0||evidence.Length==0))
                throw new InvalidDataException("Bounded verification needs source, tests, evidence and explicit scope limits.");
            rows.Add(new(uid,status,Array.AsReadOnly(implementation),Array.AsReadOnly(tests.ToArray()),Array.AsReadOnly(evidence),limitations));
        }
        return Array.AsReadOnly(rows.OrderBy(r=>r.Uid,StringComparer.Ordinal).ToArray());
    }

    /// <summary>Render compiler XML member IDs, summaries, parameters and returns; prohibit DTDs and external resolution.</summary>
    public static string RenderApi(string xml)
    {
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16_777_216});
        var document=XDocument.Load(reader);string name=document.Root?.Element("assembly")?.Element("name")?.Value??throw new InvalidDataException("Missing XML assembly name.");
        var members=document.Root!.Element("members")?.Elements("member").ToArray()??throw new InvalidDataException("Missing XML member catalog.");
        var output=new StringBuilder();output.AppendLine(name+" API").AppendLine(new string('=',name.Length+4)).AppendLine();
        output.AppendLine($"Compiler-emitted XML entries: {members.Length}. Undocumented public declarations are not represented; this is not complete API coverage.\n");
        string Flatten(XNode node)=>node switch
        {
            XText text=>text.Value,
            XElement e when e.Name.LocalName=="see"=>e.Attribute("cref")?.Value??e.Attribute("href")?.Value??e.Value,
            XElement e when e.Name.LocalName is "paramref" or "typeparamref"=>e.Attribute("name")?.Value??e.Value,
            XElement e=>string.Concat(e.Nodes().Select(Flatten)),_=>""
        };
        foreach(var member in members.OrderBy(m=>m.Attribute("name")?.Value,StringComparer.Ordinal))
        {
            string id=member.Attribute("name")?.Value??throw new InvalidDataException("Missing XML member ID.");
            output.AppendLine(".. code-block:: text\n").Append("   ").AppendLine(id).AppendLine();
            string Text(XElement node)=>Escape(Regex.Replace(string.Concat(node.Nodes().Select(Flatten)),@"\s+"," ").Trim());
            if(member.Element("summary") is {} summary)output.AppendLine(Text(summary)).AppendLine();
            foreach(var parameter in member.Elements("param"))output.Append("* ").Append(Escape(parameter.Attribute("name")?.Value??"parameter")).Append(": ").AppendLine(Text(parameter));
            if(member.Element("returns") is {} returns)output.AppendLine("\nReturns: "+Text(returns));
            output.AppendLine();
        }
        return output.ToString();
    }
    /// <summary>Render every authoritative requirement; missing ledger rows are explicitly unmapped and never promoted.</summary>
    public static string RenderTrace(IReadOnlyList<RequirementEntry> requirements,IReadOnlyList<TraceEntry> trace)
    {
        var byId=trace.ToDictionary(t=>t.Uid,StringComparer.Ordinal);var text=new StringBuilder("Requirements and evidence\n=========================\n\n");
        text.AppendLine("Statuses are manually reviewed bounded claims. File and test-name text validation does not resolve symbols, rerun tests or automatically close requirements. Unmapped, partial and historical entries remain open for current qualification.\n");
        foreach(var requirement in requirements)
        {
            byId.TryGetValue(requirement.Uid,out var row);string heading=requirement.Uid+" — "+requirement.Title;
            text.AppendLine(Escape(heading)).AppendLine(new string('-',heading.Length*2)).AppendLine();
            text.AppendLine("Status: "+(row?.Status??"unmapped")).AppendLine();
            if(row is null){text.AppendLine("No current implementation-to-test evidence mapping is asserted.\n");continue;}
            foreach(string item in row.Implementation.Concat(row.Tests).Concat(row.Evidence))
            {
                // Encode path segments independently; method text is a label, never a URL.
                string file=item.Split(" :: ",2,StringSplitOptions.None)[0];
                string url=string.Join("/",file.Replace('\\','/').Split('/').Select(Uri.EscapeDataString));
                text.AppendLine("* `"+Escape(item)+" <"+url+">`_");
            }
            text.AppendLine("\nScope/limitations: "+Escape(row.Limitations)).AppendLine();
        }
        return text.ToString();
    }
    private static string Escape(string value)=>value.Replace("\\","\\\\",StringComparison.Ordinal).Replace("*","\\*",StringComparison.Ordinal)
        .Replace("`","\\`",StringComparison.Ordinal).Replace("|","\\|",StringComparison.Ordinal).Replace("_","\\_",StringComparison.Ordinal);
}
