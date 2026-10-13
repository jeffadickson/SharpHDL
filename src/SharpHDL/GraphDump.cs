using System.Collections.Generic;
using System.Text;
namespace SharpHDL
{
    // Human-readable dump of the actual elaborated graph (not a reconstructed AST).
    internal sealed class GraphDump
    {
        readonly Dictionary<Expr,string> names=new Dictionary<Expr,string>();
        readonly StringBuilder nodes=new StringBuilder();
        int serial;
        string Node(Expr e)
        {
            var signal=e as Signal;
            if(signal!=null)return signal.Name;
            string name;
            if(names.TryGetValue(e,out name))return name;
            var children=new List<string>();
            foreach(var child in e.Args)children.Add(Node(child));
            name="e"+serial++;names[e]=name;
            nodes.Append(name+" : bits["+e.Width+"] "+e.Op);
            if(e.Op=="const")nodes.Append(" "+e.Literal);
            if(children.Count>0)nodes.Append(" ("+string.Join(", ",children)+")");
            if(e.Op=="slice"||e.Op==">>"||e.Op=="<<"||e.Op=="asr")nodes.Append(" offset="+e.Offset);
            if(e.Op=="lookup")nodes.Append(" entries="+e.Table.Length);
            if(e.Op=="memory")nodes.Append(" ram="+e.Memory.Name);
            nodes.AppendLine();return name;
        }
        internal static string Write(Module m)
        {
            var dump=new GraphDump();var result=new StringBuilder("module "+m.Name+"\n");
            var connections=new StringBuilder();
            foreach(var ram in m.Memories)
            {
                result.AppendLine("ram "+ram.Name+" : words["+ram.Depth+"] bits["+ram.Width+"]; synchronous read-before-write; reset retains cells; init="+string.Join(",",ram.Initial));
                connections.AppendLine(ram.Name+".write <- address "+dump.Node(ram.Address)+", enable "+dump.Node(ram.WriteEnable)+", data "+dump.Node(ram.WriteData));
            }
            foreach(var s in m.Signals)
            {
                result.Append(s.Kind+" "+s.Name+" : bits["+s.Width+"]");
                if(s.Kind=="reg")result.Append(" reset="+s.ResetValue);
                result.AppendLine();
                if(s.Driver!=null)connections.AppendLine(s.Name+(s.Kind=="reg"?".next":"")+" <- "+dump.Node(s.Driver));
            }
            result.AppendLine("expressions:");result.Append(dump.nodes);
            result.AppendLine("connections:");result.Append(connections);return result.ToString();
        }
    }
}
