using SharpHDL;
public static class CounterExample
{
    public static Module Build()
    {
        var m = new Module("Counter");
        var enable = m.Input("enable", 1);
        var count = m.Register("count", 8, 0);
        var output = m.Output("value", 8);
        m.NextWhen(count, enable, count + Expr.Const(8, 1));
        m.Drive(output, count);
        return m;
    }
}
