#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpHDL.Verification;

/// <summary>Pre-edge AXI-Stream signals; bit zero is the least-significant byte lane.</summary>
public readonly record struct AxiStreamObservation(BigInteger Data,BigInteger Keep,BigInteger User,
    bool Last,bool Valid,bool Ready,bool Reset);

/// <summary>Strict payload stability and width checks for byte-aligned AXI-Stream traces.</summary>
public sealed class AxiStreamConformance
{
    private readonly BigInteger dataLimit,keepLimit,userLimit;
    private AxiStreamObservation? stalled;
    /// <summary>Create a bounded profile; widths are bits, data must be byte aligned.</summary>
    public AxiStreamConformance(int dataWidth,int userWidth=1)
    {
        if(dataWidth<8||dataWidth>4096||dataWidth%8!=0)throw new ArgumentOutOfRangeException(nameof(dataWidth));
        if(userWidth<1||userWidth>4096)throw new ArgumentOutOfRangeException(nameof(userWidth));
        dataLimit=BigInteger.One<<dataWidth;keepLimit=BigInteger.One<<(dataWidth/8);userLimit=BigInteger.One<<userWidth;
    }
    /// <summary>Cumulative accepted beats, excluding reset edges. Reset does not erase this counter.</summary>
    public long Accepted { get; private set; }
    /// <summary>
    /// Observe one rising-edge sample. Return whether a transfer occurred. Reset
    /// flushes stalled state; null TKEEP beats are legal. All payload bits are held
    /// to a strict stability profile, including unqualified data bytes. A violation
    /// terminates the trace; reset the checker before continuing.
    /// </summary>
    public bool Observe(AxiStreamObservation sample)
    {
        if(sample.Data<0||sample.Data>=dataLimit||sample.Keep<0||sample.Keep>=keepLimit||sample.User<0||sample.User>=userLimit)
            throw new ArgumentException("AXI-Stream payload does not fit the declared widths.",nameof(sample));
        if(sample.Reset){stalled=null;return false;}
        if(stalled is {} prior&&(!sample.Valid||sample.Data!=prior.Data||sample.Keep!=prior.Keep||sample.User!=prior.User||sample.Last!=prior.Last))
            throw new InvalidOperationException("AXI-Stream changed a stalled offer.");
        stalled=sample.Valid&&!sample.Ready?sample:null;
        bool accepted=sample.Valid&&sample.Ready;
        if(accepted)Accepted=checked(Accepted+1);
        return accepted;
    }
}

/// <summary>One pre-edge sample of independent AXI4-Lite address/data/response channels.</summary>
public readonly record struct AxiLiteObservation(uint AwAddress,byte AwProtection,bool AwValid,bool AwReady,
    uint WriteData,byte WriteStrobe,bool WriteValid,bool WriteReady,byte WriteResponse,bool WriteResponseValid,bool WriteResponseReady,
    uint ArAddress,byte ArProtection,bool ArValid,bool ArReady,uint ReadData,byte ReadResponse,bool ReadValid,bool ReadReady,bool Reset);

/// <summary>
/// Bounded AXI4-Lite credit and stalled-channel checker. Addresses are byte
/// addresses; alignment/access permission is peripheral policy, not a bus violation.
/// This does not prove liveness or validate response data against a register map.
/// </summary>
public sealed class AxiLiteConformance
{
    private readonly ulong addressLimit;
    private readonly int maximumOutstanding;
    private int addresses,data,reads;
    private readonly Held<(uint,byte)> aw=new("AW"),w=new("W"),ar=new("AR"),r=new("R");
    private readonly Held<byte> b=new("B");
    /// <summary>Create a 32-bit data profile with 2..32 address bits and a finite credit budget.</summary>
    public AxiLiteConformance(int addressWidth=16,int maximumOutstanding=16)
    {
        if(addressWidth<2||addressWidth>32)throw new ArgumentOutOfRangeException(nameof(addressWidth));
        if(maximumOutstanding<1||maximumOutstanding>4096)throw new ArgumentOutOfRangeException(nameof(maximumOutstanding));
        addressLimit=1UL<<addressWidth;this.maximumOutstanding=maximumOutstanding;
    }
    /// <summary>Cumulative B-channel handshakes, retained across reset.</summary>
    public long CompletedWrites { get; private set; }
    /// <summary>Cumulative R-channel handshakes, retained across reset.</summary>
    public long CompletedReads { get; private set; }
    /// <summary>Check a rising-edge sample; reset flushes credits and held offers.</summary>
    public void Observe(AxiLiteObservation s)
    {
        if(s.AwAddress>=addressLimit||s.ArAddress>=addressLimit||s.AwProtection>7||s.ArProtection>7||s.WriteStrobe>15||
            s.WriteResponse>3||s.ReadResponse>3||s.WriteResponseValid&&s.WriteResponse==1||s.ReadValid&&s.ReadResponse==1)
            throw new ArgumentException("AXI4-Lite field width or unsupported EXOKAY response.",nameof(s));
        if(s.Reset){addresses=data=reads=0;aw.Clear();w.Clear();ar.Clear();r.Clear();b.Clear();return;}
        aw.Observe(s.AwValid,s.AwReady,(s.AwAddress,s.AwProtection));
        w.Observe(s.WriteValid,s.WriteReady,(s.WriteData,s.WriteStrobe));
        ar.Observe(s.ArValid,s.ArReady,(s.ArAddress,s.ArProtection));
        b.Observe(s.WriteResponseValid,s.WriteResponseReady,s.WriteResponse);
        r.Observe(s.ReadValid,s.ReadReady,(s.ReadData,s.ReadResponse));
        // Responses require credits from earlier edges, not requests offered on
        // the same edge. AW and W accumulate independently and pair in order.
        if(s.WriteResponseValid&&(addresses==0||data==0))throw new InvalidOperationException("B response precedes an accepted AW/W pair.");
        if(s.ReadValid&&reads==0)throw new InvalidOperationException("R response precedes an accepted AR.");
        if(s.AwValid&&s.AwReady)addresses++;
        if(s.WriteValid&&s.WriteReady)data++;
        if(s.ArValid&&s.ArReady)reads++;
        if(s.WriteResponseValid&&s.WriteResponseReady){addresses--;data--;CompletedWrites=checked(CompletedWrites+1);}
        if(s.ReadValid&&s.ReadReady){reads--;CompletedReads=checked(CompletedReads+1);}
        if(addresses>maximumOutstanding||data>maximumOutstanding||reads>maximumOutstanding)
            throw new InvalidOperationException("Trace exceeds the configured outstanding-credit budget.");
    }
    private sealed class Held<T>(string channel) where T:struct
    {
        private T? value;
        internal void Clear()=>value=null;
        internal void Observe(bool valid,bool ready,T payload)
        {
            if(value.HasValue&&(!valid||!EqualityComparer<T>.Default.Equals(value.Value,payload)))
                throw new InvalidOperationException($"AXI4-Lite {channel} changed a stalled channel offer.");
            value=valid&&!ready?payload:null;
        }
    }
}
