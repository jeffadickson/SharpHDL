using SharpHDL;
namespace ThreeLanguages
{
    public static class CSharpPiece
    {
        // Overloaded + builds an 8-bit add node; it does not add CPU integers.
        public static Expr AddTwo(Expr b) { var a = b + Expr.Const(8,2); return a; }
        public static int HostAddTwo(int b) { return unchecked(b+2); }
        public static Module Build()
        {
            var m=new Module("AddTwo");var b=m.Input("b",8);var a=m.Output("a",8);
            m.Drive(a,AddTwo(b));return m;
        }
        public static Stream OneStage(Module m,Stream input)
        { return StreamBlocks.Map(m,input,"one_stage",8,b=>(b+Expr.Const(8,2))*Expr.Const(8,3)); }
        public static Stream TwoStages(Module m,Stream input)
        {
            var added=StreamBlocks.Map(m,input,"add_stage",8,b=>b+Expr.Const(8,2));
            var multiplied=StreamBlocks.Map(m,added,"multiply_stage",8,a=>a*Expr.Const(8,3));
            return multiplied;
        }
    }
}
