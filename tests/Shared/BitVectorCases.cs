using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.Verification;

/// <summary>Independent complete two-bit truth tables used by Z3, Core and Yosys qualification.</summary>
public static class BitVectorCases
{
    /// <summary>One original expression and an independently enumerated reference circuit.</summary>
    public sealed record Case(string Name,HardwareGraph Left,int LeftOutput,HardwareGraph Right,int RightOutput,bool Corrupted);
    /// <summary>All supported operation encodings, with resize/oversized-shift boundary variants.</summary>
    public static IReadOnlyList<string> Names {get;}=Array.AsReadOnly(new[]{"add","sub","mul","and","or","xor","not","eq","ult","slt","select",
        "shl","shr","ashr","shl_large","shr_large","ashr_large","slice","resize_down","resize_up","sext","concat"});
    /// <summary>Build a free-input circuit and a complete, independently calculated truth-table reference.</summary>
    public static Case Build(int index,bool corrupted=false)
    {
        if(index<0||index>=Names.Count)throw new ArgumentOutOfRangeException(nameof(index));
        string name=Names[index];int width=name is "eq" or "ult" or "slt" or "slice" or "resize_down"?1:
            name is "resize_up" or "sext" or "concat"?4:2;
        var left=new List<HardwareNode>{new(0,HardwareOperation.Input,new(2),[],Name:"x"),new(1,HardwareOperation.Input,new(2),[],Name:"y")};
        int Node(HardwareOperation op,int bits,int[] operands,int offset=0){int id=left.Count;left.Add(new(id,op,new(bits),operands,Offset:offset));return id;}
        int output=name switch
        {
            "add"=>Node(HardwareOperation.Add,width,[0,1]),"sub"=>Node(HardwareOperation.Subtract,width,[0,1]),
            "mul"=>Node(HardwareOperation.Multiply,width,[0,1]),"and"=>Node(HardwareOperation.And,width,[0,1]),
            "or"=>Node(HardwareOperation.Or,width,[0,1]),"xor"=>Node(HardwareOperation.Xor,width,[0,1]),
            "not"=>Node(HardwareOperation.Not,width,[0]),"eq"=>Node(HardwareOperation.Equal,1,[0,1]),
            "ult"=>Node(HardwareOperation.LessThan,1,[0,1]),"slt"=>Node(HardwareOperation.SignedLessThan,1,[0,1]),
            "select"=>Node(HardwareOperation.Select,width,[Node(HardwareOperation.Slice,1,[0],0),0,1]),
            "shl" or "shl_large"=>Node(HardwareOperation.ShiftLeft,width,[0],name.EndsWith("large")?32:1),
            "shr" or "shr_large"=>Node(HardwareOperation.ShiftRight,width,[0],name.EndsWith("large")?32:1),
            "ashr" or "ashr_large"=>Node(HardwareOperation.ArithmeticShiftRight,width,[0],name.EndsWith("large")?32:1),
            "slice"=>Node(HardwareOperation.Slice,1,[0],1),
            "resize_down" or "resize_up"=>Node(HardwareOperation.Resize,width,[0]),
            "sext"=>Node(HardwareOperation.SignExtend,width,[0]),"concat"=>Node(HardwareOperation.Concat,width,[0,1]),
            _=>throw new InvalidOperationException("Missing qualification operation.")
        };
        var reference=new List<HardwareNode>{new(0,HardwareOperation.Input,new(2),[],Name:"x"),new(1,HardwareOperation.Input,new(2),[],Name:"y")};
        int Constant(int bits,int value){int id=reference.Count;reference.Add(new(id,HardwareOperation.Constant,new(bits),[],new BigInteger(value)));return id;}
        int RefNode(HardwareOperation op,int bits,params int[] operands){int id=reference.Count;reference.Add(new(id,op,new(bits),operands));return id;}
        int result=Constant(width,0),mask=(1<<width)-1;
        for(int x=0;x<4;x++)for(int y=0;y<4;y++)
        {
            int sx=x<2?x:x-4,sy=y<2?y:y-4;
            int expected=name switch
            {
                "add"=>x+y,"sub"=>x-y,"mul"=>x*y,"and"=>x&y,"or"=>x|y,"xor"=>x^y,"not"=>~x,
                "eq"=>x==y?1:0,"ult"=>x<y?1:0,"slt"=>sx<sy?1:0,"select"=>(x&1)!=0?x:y,
                "shl"=>x*2,"shr"=>x/2,"ashr"=>sx>>1,"shl_large" or "shr_large"=>0,
                "ashr_large"=>sx<0?-1:0,"slice"=>x/2,"resize_down" or "resize_up"=>x,
                "sext"=>sx,"concat"=>x*4+y,_=>throw new InvalidOperationException()
            };
            expected&=mask;if(corrupted&&x==3&&y==1)expected^=1;
            int ex=RefNode(HardwareOperation.Equal,1,0,Constant(2,x)),ey=RefNode(HardwareOperation.Equal,1,1,Constant(2,y));
            result=RefNode(HardwareOperation.Select,width,RefNode(HardwareOperation.And,1,ex,ey),Constant(width,expected),result);
        }
        return new(name,new HardwareGraph(left),output,new HardwareGraph(reference),result,corrupted);
    }
}
