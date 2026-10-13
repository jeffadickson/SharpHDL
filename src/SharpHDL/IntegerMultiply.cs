using System;

namespace SharpHDL
{
    public enum MultiplyArchitecture { Parallel, BitSerial }

    // Explicit elaboration directive: the choice creates different RTL, not a CPU setting.
    public static class IntegerMultiply
    {
        // Atomic input beat = {B[W-1:0], A[W-1:0]}; output is the full 2W-bit product.
        // TKEEP must be all ones: partial operands have no defined numeric meaning.
        public static Stream Build(Module m, Stream input, string name, int operandWidth,
            MultiplyArchitecture architecture, bool signed = false)
        {
            if(operandWidth < 4 || operandWidth > 2048 || operandWidth % 4 != 0)
                throw new ArgumentException("Operand width must be a multiple of four, from 4 to 2048.");
            if(input.Width != 2*operandWidth) throw new ArgumentException("Expected one packed {B,A} beat.");
            if(!Enum.IsDefined(typeof(MultiplyArchitecture),architecture))
                throw new ArgumentException("Unknown multiply architecture.");
            int w=operandWidth, p=2*w;
            input.Data.Describe(name+": atomic operands A ["+(w-1)+":0], B ["+(p-1)+":"+w+"]; "+(signed?"signed two's complement":"unsigned")+"; TKEEP must be all ones.");
            string description=name+": full "+p+"-bit "+(signed?"signed two's complement":"unsigned")+" product A*B; architecture "+architecture+". TKEEP all ones; TLAST/TUSER follow the accepted operand pair.";
            if(architecture==MultiplyArchitecture.Parallel)
            {
                var result=StreamBlocks.Map(m,input,name,p,x=>{
                    var a=x.Slice(w-1,0);var b=x.Slice(p-1,w);
                    return ((signed?a.SignExtend(p):a.Resize(p)) *
                            (signed?b.SignExtend(p):b.Resize(p))).Describe(description+
                            " One output register; multiplication is combinational, with synthesis-selected mapping.");
                });
                return result;
            }
            var output=Stream.Internal(m,name,p,input.UserWidth);
            output.Data.Describe(description+" One multiplier bit per compute cycle; no combinational multiply node.");
            var busy=m.Register(name+"_busy",1,0);
            var valid=m.Register(name+"_valid",1,0);
            var acc=m.Register(name+"_acc",p,0);
            var multiplicand=m.Register(name+"_shifted_a",p,0);
            var multiplier=m.Register(name+"_remaining_b",w,0);
            int cw=1;while((1<<cw)<w)cw++;
            var index=m.Register(name+"_bit_index",cw,0);
            var data=m.Register(name+"_product",p,0);
            var last=m.Register(name+"_last",1,0);
            var user=m.Register(name+"_user",input.UserWidth,0);
            acc.Describe("Partial product ["+(p-1)+":0], modulo 2^(2W); one reused add/sub datapath.");
            multiplicand.Describe("A extended to 2W bits and shifted left once per compute cycle.");
            multiplier.Describe("B raw bits shifted logically right; bit 0 selects this partial product.");
            index.Describe("Current multiplier bit, from 0 through W-1. Signed final bit has negative weight.");
            data.Describe(description+" Holds unchanged while TVALID && !TREADY.");
            var ready=~busy & (~valid | output.Ready);
            m.Drive(input.Ready,ready);
            var start=input.Fire;
            var final=index.Eq(Expr.Const(cw,w-1));
            var finish=busy & final;
            var aIn=input.Data.Slice(w-1,0);
            var bIn=input.Data.Slice(p-1,w);
            var selected=Expr.Mux(multiplier.Slice(0,0),multiplicand,Expr.Const(p,0));
            // A + ~B + 1 shares one addition chain for the signed final subtraction.
            Expr subtract=signed?final:Expr.Const(1,0);
            var sum=acc+Expr.Mux(subtract,~selected,selected)+subtract.Resize(p);
            m.Next(busy,Expr.Mux(start,Expr.Const(1,1),Expr.Mux(finish,Expr.Const(1,0),busy)));
            m.Next(valid,Expr.Mux(finish,Expr.Const(1,1),Expr.Mux(output.Fire,Expr.Const(1,0),valid)));
            m.Next(acc,Expr.Mux(start,Expr.Const(p,0),Expr.Mux(busy,sum,acc)));
            m.Next(multiplicand,Expr.Mux(start,signed?aIn.SignExtend(p):aIn.Resize(p),Expr.Mux(busy,multiplicand<<1,multiplicand)));
            m.Next(multiplier,Expr.Mux(start,bIn,Expr.Mux(busy,multiplier>>1,multiplier)));
            m.Next(index,Expr.Mux(start,Expr.Const(cw,0),Expr.Mux(busy,index+Expr.Const(cw,1),index)));
            m.NextWhen(data,finish,sum);
            m.NextWhen(last,start,input.Last);m.NextWhen(user,start,input.User);
            m.Drive(output.Valid,valid);m.Drive(output.Data,data);
            m.Drive(output.Keep,Expr.Const(p/8,Expr.Mask(p/8)));
            m.Drive(output.Last,last);m.Drive(output.User,user);
            return output;
        }
    }
}
