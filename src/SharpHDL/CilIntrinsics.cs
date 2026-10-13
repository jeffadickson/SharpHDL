using System;
using System.Reflection;
namespace SharpHDL
{
    public static partial class CilCompiler
    {
        static Expr BitsCall(MethodInfo m,Expr[] args)
        {
            int w=PackedLayout.Width(m.DeclaringType);Expr a=Resize(Resize(args[0],w),32),b=args.Length>1?Resize(Resize(args[1],w),32):null;Expr r;
            switch(m.Name)
            {
                case "op_Implicit":case "op_Explicit":r=a;break;
                case "op_Addition":r=Bin("add",a,b);break;case "op_Subtraction":r=Bin("sub",a,b);break;case "op_Multiply":r=Bin("mul",a,b);break;
                case "op_BitwiseAnd":r=Bin("and",a,b);break;case "op_BitwiseOr":r=Bin("or",a,b);break;case "op_ExclusiveOr":r=Bin("xor",a,b);break;case "op_OnesComplement":r=Unary("not",a);break;
                case "op_LeftShift":r=Shift(a,args[1],"shl");break;case "op_RightShift":r=Shift(a,args[1],"shr.un");break;
                case "op_LessThan":return Resize(Test("lt",a,b,true),32);case "op_GreaterThan":return Resize(Test("gt",a,b,true),32);
                case "op_LessThanOrEqual":return Resize(Not(Test("gt",a,b,true)),32);case "op_GreaterThanOrEqual":return Resize(Not(Test("lt",a,b,true)),32);
                default:throw new NotSupportedException("Unsupported bit-vector intrinsic: "+m.Name);
            }
            return Resize(Resize(r,w),32);
        }
    }
}
