using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SharpFuzz;
using SharpHDL.Documentation;

// SharpFuzz/AFL++ provides generation, shrinking/replay corpus and coverage feedback.
// Malformed text must reject explicitly; valid generated text must round-trip.
Fuzzer.OutOfProcess.Run(stream=>
{
    byte[] bytes=new byte[4096];int count=stream.ReadAtLeast(bytes,1,false);
    string text=Encoding.UTF8.GetString(bytes,0,count);
    try { DocumentationCatalog.ReadRequirements(text); }
    catch(JsonException) { /* Expected malformed JSON rejection. */ }
    catch(InvalidDataException) { /* Expected schema/budget rejection. */ }
    try { DocumentationCatalog.RenderApi(text); }
    catch(XmlException) { /* Expected malformed XML / prohibited DTD rejection. */ }
    catch(InvalidDataException) { /* Expected missing compiler catalog rejection. */ }
    string uid="FUZZ-"+Convert.ToHexString(bytes.AsSpan(0,Math.Min(count,32)));
    string json=JsonSerializer.Serialize(new[]{new Dictionary<string,string>{{"_NODE_TYPE","REQUIREMENT"},{"UID",uid},{"TITLE",text}}});
    var row=DocumentationCatalog.ReadRequirements(json);
    if(row.Count!=1||row[0].Uid!=uid||row[0].Title!=text)throw new InvalidOperationException("Requirement round-trip mismatch.");
    var xml=new XDocument(new XElement("doc",new XElement("assembly",new XElement("name","Fuzz")),new XElement("members",new XElement("member",new XAttribute("name",uid),new XElement("summary",uid)))));
    if(!DocumentationCatalog.RenderApi(xml.ToString()).Contains(uid,StringComparison.Ordinal))throw new InvalidOperationException("XML member was lost.");
});
