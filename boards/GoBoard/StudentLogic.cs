using SharpHDL;

// This is the learner's editable file. Begin by changing 2 to 3.
public static class StudentLogic
{
    public static Expr AddTwo(Expr b)
    {
        var a = b + Expr.Const(b.Width, 2);
        return a;
    }
}
