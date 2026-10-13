using System;
using SharpHDL;

// Guided capstone. First run with pointsPerShot=1; then predict and try 2.
// shot is a synchronized, debounced level (or a teacher's simulated button).
// This module does not connect directly to physical pins or debounce a switch.
public static class ShotCounter
{
    public static Module Build(int pointsPerShot=1)
    {
        if(pointsPerShot<1 || pointsPerShot>2)throw new ArgumentOutOfRangeException("pointsPerShot");
        var m=new Module(pointsPerShot==1?"ShotCounter":"ShotCounterDouble");
        var shot=m.Input("shot",1);var clear=m.Input("clear",1);
        var previous=m.Register("previous_shot",1,0);
        m.Next(previous,shot);
        var newPress=(shot & ~previous).Named("new_shot_press");
        var count=m.Register("count",4,0);
        count.Describe("Four-bit shot score, modulo 16. Clear wins over a new press. Holding shot does not repeat.");
        // Guided edit: change pointsPerShot in the caller from 1 to 2.
        var nextScore=count+Expr.Const(4,pointsPerShot);
        m.Next(count,Expr.Mux(clear,Expr.Const(4,0),Expr.Mux(newPress,nextScore,count)));
        m.Drive(m.Output("score",4),count);
        return m;
    }
}
