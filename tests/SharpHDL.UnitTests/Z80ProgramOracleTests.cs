using System.Numerics;
using Konamiman.Z80dotNet;
using SharpHDL.Examples;
using SharpHDL.HLS;
using SharpHDL.Simulation;
using Xunit;

namespace SharpHDL.UnitTests;

/// <summary>Continuous architectural comparisons with an independent, test-only CPU.</summary>
public sealed class Z80ProgramOracleTests
{
    private static readonly Lazy<ScheduledHardwareDesign> Design = new(Z80TestCore.Build);

    /// <summary>No state is re-seeded between instructions; stalls cannot duplicate side effects.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Programs_match_after_every_retirement(int scenario)
        =>RunProgram(scenario,0);

    /// <summary>Deliberate RAM/port corruption must be observed by the independent comparison.</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void Negative_controls_detect_corrupted_side_effects(int corruption)
        =>Assert.ThrowsAny<Xunit.Sdk.XunitException>(()=>RunProgram(corruption==1?0:1,corruption));

    private static void RunProgram(int scenario,int corruption)
    {
        byte[] program = scenario switch
        {
            0 => [0x31,0,0xFF,0x21,0,0x80,0x06,5,0xAF,0x80,0x10,0xFD,0x77,0x23,0x36,0xA5,0x76],
            1 => [0xDD,0x21,0,0x80,0xDD,0x36,0xFF,0x81,0xDD,0xCB,0xFF,0x06,
                  0xFD,0x21,0,0x90,0xFD,0x36,1,0x19,0xFD,0x7E,1,0xED,0x44,
                  0x01,0x34,0x12,0xED,0x79,0xED,0x78,0xD3,0x55,0xDB,0x56,0xFB,0,0xF3,0x76],
            2 => [0x21,0xFF,0x7F,0x11,0,0x90,0x01,3,0,0x36,0x41,0xED,0xA0,
                  0x36,0x42,0xED,0xA0,0x36,0x43,0xED,0xA0,0xD9,0x08,0xD9,0x08,0x76],
            _ => [0x3E,0x7F,0xC6,1,0xCE,0x7F,0xD6,0xFF,0xDE,1,0xE6,0x55,
                  0xEE,0xA5,0xF6,0x80,0xFE,0xFF,0x3C,0x3D,0x76]
        };
        var d=Design.Value;
        var machine=new HardwareGraphMachine(d.Graph,d.Inputs,d.Outputs,d.NextState,d.ResetValues,d.Memories);
        var actual=new TraceMemory(false); var expected=new TraceMemory(false);
        program.CopyTo(actual.Bytes,0); program.CopyTo(expected.Bytes,0);
        var ports=new TraceMemory(true);
        var reference=new Z80Processor { ClockSynchronizer=null, Memory=expected, PortsSpace=ports };
        reference.UseExtendedPortsSpace=true;
        reference.Reset();
        var r=reference.Registers;
        r.AF=r.BC=r.DE=r.HL=r.IX=r.IY=r.IR=0; r.SP=unchecked((short)0xFF00);
        r.Alternate.AF=r.Alternate.BC=r.Alternate.DE=r.Alternate.HL=0;
        machine.Reset(); machine.SetInput("init_sp",0xFF00);
        machine.SetInput("load_state",1); machine.Tick(); machine.SetInput("load_state",0);
        IReadOnlyDictionary<string,BigInteger>? snapshot=null;
        int Read(string name)=>(int)(snapshot??=machine.ReadOutputs())[name];
        void Tick(){machine.Tick();snapshot=null;}
        var actualPorts=new List<(int Address,bool Write,byte Data)>();
        int edges=0,retired=0;
        while(!reference.IsHalted && retired<128)
        {
            // Leave retire, return to idle, and pulse step once. A continuous
            // software CPU and the hardware now start from the same boundary.
            while(Read("idle")==0)Tick();
            machine.SetInput("step",1);snapshot=null;Tick();machine.SetInput("step",0);snapshot=null;
            for(int watchdog=0;Read("instruction_done")==0;watchdog++)
            {
                Assert.True(watchdog<256,"Instruction watchdog");Assert.Equal(0,Read("fault"));
                bool ready=++edges%4==0;
                int valid=Read("bus_valid"),address=Read("bus_address");
                bool write=Read("bus_write")!=0,io=Read("bus_io")!=0;
                byte value=(byte)Read("bus_wdata");
                byte data=io?TraceMemory.PortValue(address):actual.Bytes[address];
                machine.SetInput("bus_ready",ready?1:0);machine.SetInput("bus_rdata",data);snapshot=null;
                if(valid!=0&&ready)
                {
                    if(io)actualPorts.Add((address,write,write?value:data));
                    else if(write)actual[address]=value;
                }
                Tick();
            }
            reference.ExecuteNextInstruction();retired++;
            if(corruption==1&&retired==1)actual.Bytes[0x9000]^=1;
            if(corruption==2&&actualPorts.Count>0)
            {var access=actualPorts[0];actualPorts[0]=(access.Address,access.Write,(byte)(access.Data^1));}
            foreach(var pair in new (string,int)[]{("pc_out",r.PC),("sp_out",(ushort)r.SP),
                ("af_out",(ushort)r.AF),("bc_out",(ushort)r.BC),("de_out",(ushort)r.DE),("hl_out",(ushort)r.HL),
                ("ix_out",(ushort)r.IX),("iy_out",(ushort)r.IY),("i_out",r.I),("r_out",r.R),
                ("iff1_out",r.IFF1),("iff2_out",r.IFF2),("im_out",reference.InterruptMode),
                ("alt_out0",(ushort)r.Alternate.BC),("alt_out1",(ushort)r.Alternate.DE),
                ("alt_out2",(ushort)r.Alternate.HL),("alt_out3",(ushort)r.Alternate.AF)})
                Assert.True(Read(pair.Item1)==pair.Item2,$"case {scenario}, retirement {retired}, {pair.Item1}: hardware={Read(pair.Item1):X4}, reference={pair.Item2:X4}");
            Assert.Equal(expected.Writes,actual.Writes);
            Assert.Equal(ports.Accesses,actualPorts);
            Assert.True(expected.Bytes.AsSpan().SequenceEqual(actual.Bytes),$"RAM at retirement {retired}");
            Assert.Equal(reference.IsHalted,Read("halted_out")!=0);
        }
        Assert.True(reference.IsHalted);Assert.True(retired>10);
    }

    /// <summary>The pinned reference preserves block-I/O H/C/PV; those flags need the separate vector oracle.</summary>
    [Fact]
    public void Reference_block_io_limitation_is_explicit()
    {
        var cpu=new Z80Processor { ClockSynchronizer=null };
        cpu.Reset();cpu.Memory.SetContents(0,[0xED,0xA2]);
        cpu.Registers.BC=0x0101;cpu.Registers.HL=unchecked((short)0x8000);cpu.Registers.F=0x15;
        cpu.ExecuteNextInstruction();
        Assert.Equal(0x15,cpu.Registers.F&0x15);
    }

    // This is transport glue, not a CPU oracle: instructions and flags are
    // executed exclusively by the pinned third-party processor.
    private sealed class TraceMemory(bool ports) : IMemory
    {
        internal byte[] Bytes { get; }=new byte[65536];
        internal List<(int Address,byte Data)> Writes { get; }=[];
        internal List<(int Address,bool Write,byte Data)> Accesses { get; }=[];
        public int Size=>Bytes.Length;
        internal static byte PortValue(int address)=>(byte)(address^(address>>8)^0xA5);
        public byte this[int address]
        {
            get {byte value=ports?PortValue(address):Bytes[address];if(ports)Accesses.Add((address,false,value));return value;}
            set {Bytes[address]=value;if(ports)Accesses.Add((address,true,value));else Writes.Add((address,value));}
        }
        public void SetContents(int startAddress,byte[] contents,int startIndex=0,int? length=null)
            =>Array.Copy(contents,startIndex,Bytes,startAddress,length??contents.Length);
        public byte[] GetContents(int startAddress,int length)=>Bytes.AsSpan(startAddress,length).ToArray();
    }
}
