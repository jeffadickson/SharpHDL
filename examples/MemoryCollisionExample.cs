using System.Collections.Generic;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.Examples;

/// <summary>Two read-before-write RAMs with registered read data; reset clears registers and preserves cells.</summary>
public sealed class MemoryCollisionExample
{
    /// <summary>Deterministic module name, including the explicit inference request.</summary>
    public string Name { get; }
    /// <summary>Canonical pre-edge read and next-state graph.</summary>
    public HardwareGraph Graph { get; }
    /// <summary>External controls and register-shaped state inputs.</summary>
    public IReadOnlyDictionary<string,int> Inputs { get; }=new Dictionary<string,int>
    { ["address"]=0,["left"]=1,["right"]=2,["write"]=3,["saved_left"]=4,["saved_right"]=5 };
    /// <summary>Registered read results.</summary>
    public IReadOnlyDictionary<string,int> Outputs { get; }=new Dictionary<string,int>{["out_left"]=4,["out_right"]=5};
    /// <summary>Every edge captures the old cells before committing writes.</summary>
    public IReadOnlyDictionary<string,int> NextState { get; }=new Dictionary<string,int>{["saved_left"]=6,["saved_right"]=7};
    /// <summary>RAM storage is deliberately absent from reset values.</summary>
    public IReadOnlyDictionary<string,BigInteger> ResetValues { get; }=new Dictionary<string,BigInteger>{["saved_left"]=0,["saved_right"]=0};
    /// <summary>Separate physical ports, width four and depth three.</summary>
    public IReadOnlyDictionary<string,HardwareMemoryPort> Memories { get; }

    /// <summary>Create the qualification design; hints change emitted attributes, not simulation semantics.</summary>
    public MemoryCollisionExample(HardwareMemoryImplementation implementation=HardwareMemoryImplementation.Auto)
    {
        Name="MemoryCollision"+implementation;
        Graph=new(new HardwareNode[]{new(0,HardwareOperation.Input,new(2),[],Name:"address"),
            new(1,HardwareOperation.Input,new(4),[],Name:"left"),new(2,HardwareOperation.Input,new(4),[],Name:"right"),
            new(3,HardwareOperation.Input,new(1),[],Name:"write"),new(4,HardwareOperation.Input,new(4),[],Name:"saved_left"),
            new(5,HardwareOperation.Input,new(4),[],Name:"saved_right"),new(6,HardwareOperation.MemoryRead,new(4),[0],MemoryName:"left_ram"),
            new(7,HardwareOperation.MemoryRead,new(4),[0],MemoryName:"right_ram")},new[]{4,5});
        Memories=new Dictionary<string,HardwareMemoryPort>
        { ["left_ram"]=new("left_ram",4,0,3,1,new BigInteger[]{1,2,3},implementation),
          ["right_ram"]=new("right_ram",4,0,3,2,new BigInteger[]{7,8,9},implementation) };
    }
}
