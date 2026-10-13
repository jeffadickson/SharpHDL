using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SharpHDL;
using Module=SharpHDL.Module;
class NetNaming
{
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Main()
 {
  var m=new Module("Names");var a=m.Input("a",8);
  var x=(a+Expr.Const(8,2)).Named("effective address");
  Check(object.ReferenceEquals(x,x.Named("effective address")),"Named must preserve identity");
  var y=(a+Expr.Const(8,3)).Named("effective-address");
  var z=(a+Expr.Const(8,4)).Named("effective_address_2");
  Expr[] nodes={x,y,z,(a+Expr.Const(8,5)).Named("module"),(a+Expr.Const(8,6)).Named("2/\nunsafe"),(a+Expr.Const(8,7)).Named(new string('a',100)),(a+Expr.Const(8,8)).Named(new string('a',100)),x.Slice(3,0),a+Expr.Const(8,9)};
  for(int i=0;i<nodes.Length;i++)m.Drive(m.Output("o"+i,nodes[i].Width),nodes[i]);
  var v=m.Verilog();Check(v==m.Verilog(),"deterministic emission");
  var names=Regex.Matches(v,@"wire \[\d+:0\] (n_\w+);").Cast<Match>().Select(t=>t.Groups[1].Value).ToArray();
  Check(names.Length==9&&names.Distinct().Count()==9,"unique names including suffix collisions");
  Check(v.Contains("n_effective_address_2_2")&&v.Contains("n_module")&&v.Contains("n_2__unsafe"),"sanitize and collision suffixes");
  Check(v.Contains("n_bits_3_0_")&&v.Contains("n_add_"),"operation fallback");
  Check(v.Contains("wire [3:0] n_bits_3_0_")&&v.Contains("= n_effective_address >> 0;"),"slice truncation preserved");
  Check(names.All(n=>n.Length<=70),"bounded identifier length");
  foreach(var bad in new[]{null,"","  "}) {bool rejected=false;try{x.Named(bad);}catch(ArgumentException){rejected=true;}Check(rejected,"empty hints rejected");}
  File.WriteAllText("build/tinyrisc/names.v",v);
  File.WriteAllText("build/tinyrisc/names_tb.v","module tb; reg [7:0] a; wire [7:0] o0,o1,o2,o3,o4,o5,o6,o8; wire [3:0] o7; integer i; h_Names dut(.clk(1'b0),.rst(1'b0),.s_a(a),.s_o0(o0),.s_o1(o1),.s_o2(o2),.s_o3(o3),.s_o4(o4),.s_o5(o5),.s_o6(o6),.s_o7(o7),.s_o8(o8)); initial begin for(i=0;i<256;i=i+1) begin a=i; #1; if(o0!==((i+2)&255)||o1!==((i+3)&255)||o2!==((i+4)&255)||o3!==((i+5)&255)||o4!==((i+6)&255)||o5!==((i+7)&255)||o6!==((i+8)&255)||o7!==((i+2)&15)||o8!==((i+9)&255)) $fatal(1,\"naming altered arithmetic\"); end $display(\"PASS naming RTL: 256 inputs, wrap and slice\"); $finish; end endmodule");
  Console.WriteLine("PASS net naming metadata, deterministic identifiers, collisions and widths");
 }
}
