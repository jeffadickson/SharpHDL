using System;
using System.Numerics;
using SharpHDL;

// Teacher-facing board-independent classroom logic. Buttons are already debounced.
public static class ClassroomCore
{
    public static Module Build(int clocksPerTick=25000000)
    {
        if(clocksPerTick<2)throw new ArgumentOutOfRangeException("clocksPerTick");
        var m=new Module("ClassroomCore");var buttons=m.Input("buttons",4);
        buttons.Describe("Debounced active-high buttons [3:0]: bit0 clear, bit1 increment B, bit2 run/pause, bit3 arithmetic/counter display.");
        var previous=m.Register("previous_buttons",4,0);m.Next(previous,buttons);
        var press=buttons & ~previous;
        var b=m.Register("b",4,0);m.Next(b,Expr.Mux(press.Slice(0,0),Expr.Const(4,0),Expr.Mux(press.Slice(1,1),b+Expr.Const(4,1),b)));
        var run=m.Register("run",1,0);m.NextWhen(run,press.Slice(2,2),~run);
        var mode=m.Register("counter_mode",1,0);m.NextWhen(mode,press.Slice(3,3),~mode);
        int dw=1;while((BigInteger.One<<dw)<clocksPerTick)dw++;
        var divider=m.Register("divider",dw,0);var tick=divider.Eq(Expr.Const(dw,clocksPerTick-1));
        m.Next(divider,Expr.Mux(tick,Expr.Const(dw,0),divider+Expr.Const(dw,1)));
        var count=m.Register("count",8,0);
        m.Next(count,Expr.Mux(press.Slice(0,0),Expr.Const(8,0),Expr.Mux(run & tick,count+Expr.Const(8,1),count)));
        // The child's first edit: change the constant 2, then predict overflow.
        var a=StudentLogic.AddTwo(b);
        var left=m.Output("left_digit",4);var right=m.Output("right_digit",4);var leds=m.Output("leds",4);
        left.Describe("Hex digit: B in arithmetic mode; counter bits [7:4] in counter mode.");
        right.Describe("Hex digit: (B+2) modulo 16 in arithmetic mode; counter bits [3:0] in counter mode.");
        leds.Describe("Four binary LEDs: bit0 is the least significant bit; B or counter low nibble.");
        m.Drive(left,Expr.Mux(mode,count.Slice(7,4),b));m.Drive(right,Expr.Mux(mode,count.Slice(3,0),a));m.Drive(leds,Expr.Mux(mode,count.Slice(3,0),b));
        return m;
    }
}
