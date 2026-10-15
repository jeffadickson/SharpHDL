using System;

namespace SharpHDL
{
    /// <summary>
    /// A resource-shared, unsigned, one-bit-per-clock shift/add multiplier.
    /// Unlike a combinational HLS expression this unit has explicit ready/valid
    /// transaction boundaries. It is not an II=1 pipeline.
    /// </summary>
    public static class IterativeMultiplier
    {
        /// <summary>Ports for a non-overlapping multicycle multiplication unit.</summary>
        public sealed class Ports
        {
            public Signal A { get; internal set; }
            public Signal B { get; internal set; }
            public Signal InputValid { get; internal set; }
            public Signal InputReady { get; internal set; }
            public Signal Result { get; internal set; }
            public Signal OutputValid { get; internal set; }
            public Signal OutputReady { get; internal set; }
            public int CyclesPerProduct { get; internal set; }
        }

        /// <summary>
        /// Creates a multiplier using one width-bit accumulator/adder and
        /// shift registers. Result is modulo 2^width. A transaction takes
        /// width computation edges after its acceptance edge. The result
        /// remains stable under downstream backpressure.
        /// </summary>
        public static Ports Attach(Module module,string name,int width)
        {
            if(module==null)throw new ArgumentNullException(nameof(module));
            if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Name required.",nameof(name));
            if(width<1||width>32)throw new ArgumentOutOfRangeException(nameof(width));
            var a=module.Input(name+"_a",width);
            var b=module.Input(name+"_b",width);
            var iv=module.Input(name+"_input_valid",1);
            var ir=module.Output(name+"_input_ready",1);
            var result=module.Output(name+"_result",width);
            var ov=module.Output(name+"_output_valid",1);
            var or=module.Input(name+"_output_ready",1);
            var busy=module.Register(name+"_busy",1,0);
            var done=module.Register(name+"_done",1,0);
            var x=module.Register(name+"_shifted_a",width,0);
            var y=module.Register(name+"_remaining_b",width,0);
            var acc=module.Register(name+"_accumulator",width,0);
            int cw=1;while((1L<<cw)<=width)cw++;
            var count=module.Register(name+"_bit_index",cw,0);
            var ready=~busy & ~done;
            var accept=iv & ready;
            var finish=busy & count.Eq(Expr.Const(cw,width-1));
            var step=busy;
            module.Drive(ir,ready);
            module.Drive(result,acc);
            module.Drive(ov,done);
            module.Next(busy,Expr.Mux(accept,Expr.Const(1,1),
                Expr.Mux(finish,Expr.Const(1,0),busy)));
            module.Next(done,Expr.Mux(finish,Expr.Const(1,1),
                Expr.Mux(done & or,Expr.Const(1,0),done)));
            module.Next(x,Expr.Mux(accept,a,Expr.Mux(step,x<<1,x)));
            module.Next(y,Expr.Mux(accept,b,Expr.Mux(step,y>>1,y)));
            module.Next(acc,Expr.Mux(accept,Expr.Const(width,0),
                Expr.Mux(step,acc+Expr.Mux(y.Slice(0,0),x,Expr.Const(width,0)),acc)));
            module.Next(count,Expr.Mux(accept,Expr.Const(cw,0),
                Expr.Mux(step,count+Expr.Const(cw,1),count)));
            return new Ports { A=a,B=b,InputValid=iv,InputReady=ir,Result=result,
                OutputValid=ov,OutputReady=or,CyclesPerProduct=width+1 };
        }
    }
}
