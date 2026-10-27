using System.Numerics;
using SharpHDL.Verification;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Reusable protocol checkers challenged against real peripherals and malformed traces.</summary>
public sealed class AxiConformanceTests
{
    private static void Run(Module module,byte[] commands,bool lite)
    {
        var sim=module.Simulate();var ports=module.Ports.ToDictionary(p=>p.Name);
        void Drive(string name,int value)=>sim.Set(ports[name],unchecked((uint)value));
        int Read(string name)=>unchecked((int)(uint)sim.Read(ports[name]));
        if(lite)AxiConformanceCampaign.LiteTraffic(commands,Drive,Read,reset=>sim.Tick(reset));
        else AxiConformanceCampaign.StreamTraffic(commands,Drive,Read,reset=>sim.Tick(reset));
    }

    /// <summary>All command bytes cover strobes, addresses, AW/W order and write/read reset phases.</summary>
    [Fact]
    public void Directed_lite_and_stream_transactions_match_independent_models()
    {
        Run(AxiConformanceCampaign.BuildLite(),Enumerable.Range(0,256).Select(i=>(byte)i).ToArray(),true);
        Run(AxiConformanceCampaign.BuildStream(),Enumerable.Repeat((byte)2,12).Concat(Enumerable.Repeat((byte)3,24))
            .Concat(new byte[]{130,3,35,67,99,195,3,2,2,3}).ToArray(),false);
    }

    /// <summary>FsCheck shrinks command arrays while the driver keeps offered source transactions legal.</summary>
    [FsCheck.Xunit.Property(MaxTest=60,Replay="1145655947,296144285")]
    public void Generated_traffic_preserves_order_stability_and_reset(byte[] commands)
    {
        var bounded=(commands??[]).Take(32).ToArray();
        Run(AxiConformanceCampaign.BuildLite(),bounded,true);
        Run(AxiConformanceCampaign.BuildStream(),bounded,false);
    }

    /// <summary>Every stream payload field and dropping VALID are checked even on the accepting edge.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Stream_stalled_offer_corruption_is_rejected(int field)
    {
        var monitor=new AxiStreamConformance(16,4);
        var offer=new AxiStreamObservation(0x1234,3,4,true,true,false,false);monitor.Observe(offer);
        var changed=field switch{0=>offer with{Data=7},1=>offer with{Keep=1},2=>offer with{User=2},
            3=>offer with{Last=false},_=>offer with{Valid=false}};
        Assert.Throws<InvalidOperationException>(()=>monitor.Observe(changed with{Ready=true}));
    }

    /// <summary>Accepted/idle offers impose no later hold obligation; minimum widths include zero payload.</summary>
    [Fact]
    public void Stream_minimum_profile_and_unstalled_offers_are_legal()
    {
        var monitor=new AxiStreamConformance(8,1);
        var offer=new AxiStreamObservation(0,1,1,false,true,true,false);
        Assert.True(monitor.Observe(offer));
        Assert.True(monitor.Observe(offer with{Data=1}));
        Assert.False(monitor.Observe(offer with{Data=2,Valid=false,Ready=false}));
        Assert.True(monitor.Observe(offer with{Data=3}));
        Assert.Equal(3,monitor.Accepted);
    }

    /// <summary>Null-byte beats are legal; reset cancels offers without erasing cumulative counts.</summary>
    [Fact]
    public void Stream_reset_widths_and_counters_are_explicit()
    {
        var monitor=new AxiStreamConformance(16,4);
        var offer=new AxiStreamObservation(1,0,0,false,true,true,false);
        Assert.True(monitor.Observe(offer));Assert.False(monitor.Observe(offer with{Ready=false}));
        Assert.False(monitor.Observe(offer with{Reset=true}));Assert.True(monitor.Observe(offer with{Data=2}));
        Assert.Equal(2,monitor.Accepted);
        Assert.Throws<ArgumentException>(()=>monitor.Observe(offer with{Data=-1}));
        Assert.Throws<ArgumentException>(()=>monitor.Observe(offer with{Data=65536}));
        Assert.Throws<ArgumentException>(()=>monitor.Observe(offer with{Keep=-1}));
        Assert.Throws<ArgumentException>(()=>monitor.Observe(offer with{Keep=4}));
        Assert.Throws<ArgumentException>(()=>monitor.Observe(offer with{User=-1}));
        Assert.Throws<ArgumentException>(()=>monitor.Observe(offer with{User=16}));
        foreach(int width in new[]{0,7,9,4097})Assert.Throws<ArgumentOutOfRangeException>(()=>new AxiStreamConformance(width));
        foreach(int width in new[]{0,4097})Assert.Throws<ArgumentOutOfRangeException>(()=>new AxiStreamConformance(8,width));
        Assert.True(new AxiStreamConformance(4096,4096).Observe(new((BigInteger.One<<4096)-1,(BigInteger.One<<512)-1,0,false,true,true,false)));
    }

    /// <summary>Responses consume earlier independent credits and survive backpressure unchanged.</summary>
    [Fact]
    public void Lite_credits_responses_and_reset_have_independent_state()
    {
        var monitor=new AxiLiteConformance(8,1);
        var request=default(AxiLiteObservation) with{AwValid=true,AwReady=true,WriteValid=true,WriteReady=true,ArValid=true,ArReady=true};
        var response=default(AxiLiteObservation) with{WriteResponseValid=true,ReadValid=true,ReadData=0xDEADBEEF};
        monitor.Observe(request);monitor.Observe(response);
        monitor.Observe(response with{WriteResponseReady=true,ReadReady=true});
        Assert.Equal(1,monitor.CompletedWrites);Assert.Equal(1,monitor.CompletedReads);
        monitor.Observe(response with{Reset=true});
        Assert.Equal(1,monitor.CompletedWrites);Assert.Equal(1,monitor.CompletedReads);
        Assert.Throws<InvalidOperationException>(()=>monitor.Observe(response));
        monitor.Observe(default(AxiLiteObservation) with{Reset=true});
        monitor.Observe(default(AxiLiteObservation) with{AwValid=true,AwAddress=1});
        monitor.Observe(default(AxiLiteObservation) with{Reset=true});
        monitor.Observe(request with{AwAddress=4});
        monitor.Observe(response with{WriteResponseReady=true,ReadReady=true});
        Assert.Equal(2,monitor.CompletedWrites);Assert.Equal(2,monitor.CompletedReads);
    }

    /// <summary>Every independent channel carries its own held payload/VALID obligation.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Lite_stalled_channel_corruption_is_rejected(int channel)
    {
        var monitor=new AxiLiteConformance();var sample=default(AxiLiteObservation);
        if(channel==3)monitor.Observe(sample with{AwValid=true,AwReady=true,WriteValid=true,WriteReady=true});
        if(channel==4)monitor.Observe(sample with{ArValid=true,ArReady=true});
        sample=channel switch{0 or 5=>sample with{AwValid=true},1=>sample with{WriteValid=true},
            2=>sample with{ArValid=true},3=>sample with{WriteResponseValid=true},_=>sample with{ReadValid=true}};
        monitor.Observe(sample);
        var changed=channel switch{0=>sample with{AwAddress=4},1=>sample with{WriteStrobe=1},
            2=>sample with{ArProtection=1},3=>sample with{WriteResponse=2},4=>sample with{ReadData=1},_=>sample with{AwValid=false}};
        var error=Assert.Throws<InvalidOperationException>(()=>monitor.Observe(changed));
        Assert.Contains(new[]{"AW","W","AR","B","R","AW"}[channel],error.Message);
    }

    /// <summary>Boundary values and same-edge credit replacement remain legal at a one-request budget.</summary>
    [Fact]
    public void Lite_maximum_fields_and_replacement_credits_are_legal()
    {
        var monitor=new AxiLiteConformance(2,1);
        // Inactive response wires can carry EXOKAY bits without offering that response.
        monitor.Observe(default(AxiLiteObservation) with{WriteResponse=1,ReadResponse=1});
        var request=default(AxiLiteObservation) with{AwAddress=3,ArAddress=3,AwProtection=7,ArProtection=7,
            WriteData=uint.MaxValue,WriteStrobe=15,AwValid=true,AwReady=true,WriteValid=true,WriteReady=true,ArValid=true,ArReady=true};
        var response=default(AxiLiteObservation) with{WriteResponse=3,ReadResponse=3,ReadData=uint.MaxValue,
            WriteResponseValid=true,WriteResponseReady=true,ReadValid=true,ReadReady=true};
        monitor.Observe(request);
        monitor.Observe(request with{WriteResponse=3,ReadResponse=3,ReadData=uint.MaxValue,
            WriteResponseValid=true,WriteResponseReady=true,ReadValid=true,ReadReady=true});
        monitor.Observe(response);
        Assert.Equal(2,monitor.CompletedWrites);Assert.Equal(2,monitor.CompletedReads);
        Assert.Throws<InvalidOperationException>(()=>monitor.Observe(response with{ReadValid=false}));
        Assert.Throws<InvalidOperationException>(()=>monitor.Observe(response with{WriteResponseValid=false}));
        var noRequest=new AxiLiteConformance();
        noRequest.Observe(default(AxiLiteObservation) with{ArReady=true});
        Assert.Throws<InvalidOperationException>(()=>noRequest.Observe(default(AxiLiteObservation) with{ReadValid=true,ReadReady=true}));
    }

    /// <summary>Reset cancels each held offer and every independent pending request credit.</summary>
    [Fact]
    public void Lite_reset_cancels_each_channel_and_pending_credits()
    {
        var request=default(AxiLiteObservation) with{AwValid=true,AwReady=true,WriteValid=true,WriteReady=true,ArValid=true,ArReady=true};
        for(int channel=0;channel<5;channel++)
        {
            var monitor=new AxiLiteConformance();
            if(channel>=3)monitor.Observe(request);
            var held=channel switch{0=>default(AxiLiteObservation) with{AwValid=true},
                1=>default(AxiLiteObservation) with{WriteValid=true},2=>default(AxiLiteObservation) with{ArValid=true},
                3=>default(AxiLiteObservation) with{WriteResponseValid=true},_=>default(AxiLiteObservation) with{ReadValid=true}};
            monitor.Observe(held);monitor.Observe(default(AxiLiteObservation) with{Reset=true});
            monitor.Observe(default); // Dropping the formerly held offer is legal after reset.
        }
        for(int partial=0;partial<2;partial++)
        {
            var monitor=new AxiLiteConformance();monitor.Observe(request);
            monitor.Observe(default(AxiLiteObservation) with{Reset=true});
            monitor.Observe(default(AxiLiteObservation) with{AwValid=partial==0,AwReady=true,WriteValid=partial==1,WriteReady=true});
            Assert.Throws<InvalidOperationException>(()=>monitor.Observe(default(AxiLiteObservation) with{WriteResponseValid=true}));
        }
        // A failed B trace can leave a held B offer; check AR cancellation on a
        // fresh trace so that a stale B offer cannot mask a missing read-credit reset.
        var readMonitor=new AxiLiteConformance();readMonitor.Observe(request);
        readMonitor.Observe(default(AxiLiteObservation) with{Reset=true});
        Assert.Throws<InvalidOperationException>(()=>readMonitor.Observe(default(AxiLiteObservation) with{ReadValid=true}));
    }

    /// <summary>Unsolicited/early responses, width errors, EXOKAY and profile credit exhaustion fail loudly.</summary>
    [Fact]
    public void Lite_malformed_traces_and_profile_limits_are_rejected()
    {
        for(int partial=0;partial<3;partial++)
        {
            var monitor=new AxiLiteConformance();
            monitor.Observe(default(AxiLiteObservation) with{AwValid=partial==1,AwReady=true,WriteValid=partial==2,WriteReady=true});
            Assert.Throws<InvalidOperationException>(()=>monitor.Observe(default(AxiLiteObservation) with{WriteResponseValid=true}));
        }
        Assert.Throws<InvalidOperationException>(()=>new AxiLiteConformance().Observe(default(AxiLiteObservation) with{ReadValid=true}));
        Assert.Throws<InvalidOperationException>(()=>new AxiLiteConformance().Observe(default(AxiLiteObservation) with{AwValid=true,AwReady=true,WriteValid=true,WriteReady=true,WriteResponseValid=true}));
        var malformed=new[]{default(AxiLiteObservation) with{AwAddress=256},default(AxiLiteObservation) with{ArAddress=256},
            default(AxiLiteObservation) with{AwProtection=8},default(AxiLiteObservation) with{ArProtection=8},
            default(AxiLiteObservation) with{WriteStrobe=16},default(AxiLiteObservation) with{WriteResponse=4},
            default(AxiLiteObservation) with{ReadResponse=4},default(AxiLiteObservation) with{WriteResponse=1,WriteResponseValid=true},
            default(AxiLiteObservation) with{ReadResponse=1,ReadValid=true}};
        foreach(var sample in malformed)Assert.Throws<ArgumentException>(()=>new AxiLiteConformance(8).Observe(sample));
        foreach(int width in new[]{1,33})Assert.Throws<ArgumentOutOfRangeException>(()=>new AxiLiteConformance(width));
        foreach(int limit in new[]{0,4097})Assert.Throws<ArgumentOutOfRangeException>(()=>new AxiLiteConformance(8,limit));
        new AxiLiteConformance(32,4096).Observe(default(AxiLiteObservation) with{AwAddress=uint.MaxValue,ArAddress=uint.MaxValue});
        foreach(var sample in new[]{default(AxiLiteObservation) with{AwValid=true,AwReady=true},
            default(AxiLiteObservation) with{WriteValid=true,WriteReady=true},default(AxiLiteObservation) with{ArValid=true,ArReady=true}})
        {var monitor=new AxiLiteConformance(8,1);monitor.Observe(sample);Assert.Throws<InvalidOperationException>(()=>monitor.Observe(sample));}
    }
}
