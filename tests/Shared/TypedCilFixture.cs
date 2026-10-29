using System.Reflection;
using System.Reflection.Emit;

namespace SharpHDL.Verification;

/// <summary>Raw CIL bodies challenge validation without relying on C#'s own type checking.</summary>
public static class TypedCilFixture
{
    /// <summary>Create a two-argument RAM/scalar kernel, optionally with declared local storage.</summary>
    public static MethodInfo Create(Action<ILGenerator> emit,params Type[] locals)
    {
        var a=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Typed"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var type=a.DefineDynamicModule("main").DefineType("Kernel",TypeAttributes.Public);
        var m=type.DefineMethod("Evaluate",MethodAttributes.Public|MethodAttributes.Static,typeof(uint),[typeof(uint[]),typeof(uint)]);
        var il=m.GetILGenerator();foreach(var local in locals)il.DeclareLocal(local);emit(il);
        return type.CreateType()!.GetMethod("Evaluate")!;
    }
}
