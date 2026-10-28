using System.Reflection;
using System.Reflection.Emit;

namespace SharpHDL.Verification;

/// <summary>Direct IL fixtures avoid C# optimizer removal or folding of individual conversions.</summary>
public static class NarrowCilFixture
{
    private static readonly IReadOnlyDictionary<string,MethodInfo> methods=new Dictionary<string,MethodInfo>
    {
        ["conv.i1"]=Create(OpCodes.Conv_I1),["conv.u1"]=Create(OpCodes.Conv_U1),
        ["conv.i2"]=Create(OpCodes.Conv_I2),["conv.u2"]=Create(OpCodes.Conv_U2)
    };
    /// <summary>Read a stable raw-IL method with one UInt32 input and result.</summary>
    public static MethodInfo Method(string opcode)=>methods[opcode];
    /// <summary>Create negative fixtures as well as valid methods; narrow locals remain outside the profile.</summary>
    public static MethodInfo Create(OpCode opcode,bool narrowLocal=false,bool omitOperand=false)
    {
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Narrow"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var type=assembly.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Convert",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),[typeof(uint)]);
        var il=method.GetILGenerator();
        if(narrowLocal)il.DeclareLocal(typeof(byte));
        if(!omitOperand)il.Emit(OpCodes.Ldarg_0);
        il.Emit(opcode);
        if(narrowLocal){il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldloc_0);}
        il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
        return type.CreateType()!.GetMethod("Convert")!;
    }
    /// <summary>Independent modular arithmetic model, not a call into either hardware evaluator.</summary>
    public static uint Expected(uint value,string opcode)
    {
        int width=opcode.EndsWith('1')?8:16;
        uint modulus=1u<<width,low=value%(modulus);
        return opcode is "conv.i1" or "conv.i2" && low>=modulus/2?unchecked(low-modulus):low;
    }
}
