using System.Reflection;
using System.Reflection.Emit;

namespace SharpHDL.Verification;

/// <summary>Direct IL fixtures avoid C# optimizer removal or folding of individual conversions.</summary>
public static class NarrowCilFixture
{
    /// <summary>The bounded checked opcode family; native-sized and 64-bit conversions are excluded.</summary>
    public static IReadOnlyList<OpCode> CheckedOpcodes { get; }=Array.AsReadOnly(new[]{
        OpCodes.Conv_Ovf_I1,OpCodes.Conv_Ovf_U1,OpCodes.Conv_Ovf_I2,OpCodes.Conv_Ovf_U2,OpCodes.Conv_Ovf_I4,OpCodes.Conv_Ovf_U4,
        OpCodes.Conv_Ovf_I1_Un,OpCodes.Conv_Ovf_U1_Un,OpCodes.Conv_Ovf_I2_Un,OpCodes.Conv_Ovf_U2_Un,OpCodes.Conv_Ovf_I4_Un,OpCodes.Conv_Ovf_U4_Un});

    /// <summary>A single RTL fixture selects all checked operations, retaining exact faulting IL offsets.</summary>
    public static MethodInfo CheckedDispatch(out int[] offsets)
    {
        var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CheckedDispatch"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var type=assembly.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var method=type.DefineMethod("Convert",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),[typeof(uint),typeof(uint)]);
        var il=method.GetILGenerator();var labels=CheckedOpcodes.Select(_=>il.DefineLabel()).ToArray();offsets=new int[labels.Length];
        il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Switch,labels);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Ret);
        for(int i=0;i<labels.Length;i++)
        {
            il.MarkLabel(labels[i]);il.Emit(OpCodes.Ldarg_0);offsets[i]=il.ILOffset;
            il.Emit(CheckedOpcodes[i]);il.Emit(OpCodes.Conv_U4);il.Emit(OpCodes.Ret);
        }
        return type.CreateType()!.GetMethod("Convert")!;
    }
    private static readonly IReadOnlyDictionary<string,MethodInfo> methods=new Dictionary<string,MethodInfo>
    {
        ["conv.i1"]=Create(OpCodes.Conv_I1),["conv.u1"]=Create(OpCodes.Conv_U1),
        ["conv.i2"]=Create(OpCodes.Conv_I2),["conv.u2"]=Create(OpCodes.Conv_U2)
    };
    /// <summary>Read a stable raw-IL method with one UInt32 input and result.</summary>
    public static MethodInfo Method(string opcode)=>methods[opcode];
    /// <summary>Create negative fixtures as well as valid explicit conversion methods.</summary>
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
    /// <summary>Create raw narrow-local, argument-write and return fixtures.</summary>
    public static MethodInfo Storage(Type storage,bool argument=false,bool result=false,bool readOnly=false)
    {
        var a=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Storage"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var t=a.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var m=t.DefineMethod("Evaluate",MethodAttributes.Public|MethodAttributes.Static,result?storage:typeof(uint),argument?[storage,typeof(uint)]:[typeof(uint)]);
        var il=m.GetILGenerator();
        if(!argument&&readOnly)il.Emit(OpCodes.Ldarg_0);
        else if(argument){if(!readOnly){il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Starg_S,(byte)0);}il.Emit(OpCodes.Ldarg_0);}
        else {il.DeclareLocal(storage);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Stloc_0);il.Emit(OpCodes.Ldloc_0);}
        il.Emit(OpCodes.Ret);
        return t.CreateType()!.GetMethod("Evaluate")!;
    }

    /// <summary>Independent modular arithmetic model, not a call into either hardware evaluator.</summary>
    public static uint Expected(uint value,string opcode)
    {
        int width=opcode.EndsWith('1')?8:16;
        uint modulus=1u<<width,low=value%(modulus);
        return opcode is "conv.i1" or "conv.i2" && low>=modulus/2?unchecked(low-modulus):low;
    }
}
