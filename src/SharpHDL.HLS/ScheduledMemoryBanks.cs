using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpHDL.Core;

namespace SharpHDL.HLS;

/// <summary>Explicit cyclic banks: logical word = row * bank count + bank.</summary>
public sealed class ScheduledMemoryBanks
{
    internal ScheduledMemoryBanks(ScheduledMemory[] banks,int rows)
        {Banks=Array.AsReadOnly(banks);Rows=rows;}
    /// <summary>Independent physical single-port memories; no implicit arbitration.</summary>
    public IReadOnlyList<ScheduledMemory> Banks { get; }
    /// <summary>Logical rows in each bank.</summary>
    public int Rows { get; }
    /// <summary>Total logical words.</summary>
    public int Depth=>checked(Banks.Count*Rows);
    /// <summary>Map a checked linear address to its cyclic bank.</summary>
    public int BankForWord(int word){Check(word);return word%Banks.Count;}
    /// <summary>Map a checked linear address to its row.</summary>
    public int RowForWord(int word){Check(word);return word/Banks.Count;}
    private void Check(int word){if(word<0||word>=Depth)throw new ArgumentOutOfRangeException(nameof(word));}
}

public sealed partial class ScheduledMachineBuilder
{
    /// <summary>
    /// Declare 1..16 power-of-two independent banks and at most 65536 logical
    /// words, with optional exact linear initialization. Access each bank using
    /// ordinary Read/Write; same-bank conflicts remain conservatively rejected.
    /// Names and contents are checked before any bank is allocated.
    /// </summary>
    public ScheduledMemoryBanks BankedMemory(string memoryName,int width,int bankCount,int rows,
        IEnumerable<BigInteger>? initial=null,HardwareMemoryImplementation implementation=HardwareMemoryImplementation.Auto)
    {
        EnsureMutable();ValidateWidth(width);
        if(bankCount<1||bankCount>16||!BitOperations.IsPow2((uint)bankCount))throw new ArgumentOutOfRangeException(nameof(bankCount));
        if(rows<1||rows>65536/bankCount)throw new ArgumentOutOfRangeException(nameof(rows));
        if(!Enum.IsDefined(implementation))throw new ArgumentOutOfRangeException(nameof(implementation));
        int depth=bankCount*rows;
        var words=initial==null?new BigInteger[depth]:initial.Take(depth+1).ToArray();
        if(words.Length!=depth||words.Any(v=>v<0||v>=(BigInteger.One<<width)))throw new ArgumentException("Banked initial contents must exactly match logical depth and width.",nameof(initial));
        var bankNames=Enumerable.Range(0,bankCount).Select(i=>memoryName+"_bank"+i).ToArray();
        if(string.IsNullOrWhiteSpace(memoryName)||new[]{memoryName}.Concat(bankNames).Any(names.Contains))
            throw new ArgumentException("Bank group and physical bank names must be unique.",nameof(memoryName));
        AddName(memoryName);
        var banks=bankNames.Select((name,bank)=>Memory(name,width,rows,
            Enumerable.Range(0,rows).Select(row=>words[row*bankCount+bank]),implementation)).ToArray();
        return new ScheduledMemoryBanks(banks,rows);
    }
}
