using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace SharpHDL.Verification;

/// <summary>Independent transaction models reusable by managed tests and actual HDL replay.</summary>
public static class AxiConformanceCampaign
{
    /// <summary>A two-register Lite peripheral with a read-only status word and error responses.</summary>
    public static Module BuildLite()
    {
        var m=new Module("AxiConformanceLite");var p=AxiLitePort.Slave(m,"s_axi",8);
        var bank=new AxiLiteRegisterBank(m,p,"registers");
        bank.AddRegister("first",0,0x12345678);bank.AddRegister("second",4,0xAABBCCDD);
        bank.AddStatus("status",8,Expr.Const(32,0xCAFEBABE));bank.Build();return m;
    }

    /// <summary>A four-beat FIFO preserving every stream sideband, including null-byte beats.</summary>
    public static Module BuildStream()
    {
        var m=new Module("AxiConformanceStream");var input=Stream.Input(m,"s_axis",16,4);
        Stream.Connect(m,StreamBlocks.Fifo(m,input,"queue",4),Stream.Output(m,"m_axis",16,4));return m;
    }

    /// <summary>Run legal source traffic with stalls/reset, checked by an independent queue and protocol monitors.</summary>
    public static void StreamTraffic(byte[] commands,Action<string,int> drive,Func<string,int> read,Action<bool> clock)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var expected=new Queue<AxiStreamObservation>();AxiStreamObservation? pending=null;
        var source=new AxiStreamConformance(16,4);var sink=new AxiStreamConformance(16,4);
        drive("s_axis_tvalid",0);drive("m_axis_tready",0);clock(true);
        int count=Math.Min(commands.Length,256);
        for(int edge=0;edge<count+64;edge++)
        {
            int command=edge<count?commands[edge]:0;
            bool reset=edge<count&&(command&128)!=0,ready=edge>=count||(command&1)!=0;
            if(!pending.HasValue&&edge<count&&(command&2)!=0)
                pending=new(((command<<8)|edge)&65535,(command>>5)&3,(command>>2)&15,(command&16)!=0,true,false,false);
            var offer=pending??default;
            drive("s_axis_tvalid",pending.HasValue?1:0);drive("s_axis_tdata",(int)offer.Data);
            drive("s_axis_tkeep",(int)offer.Keep);drive("s_axis_tuser",(int)offer.User);drive("s_axis_tlast",offer.Last?1:0);
            drive("m_axis_tready",ready?1:0);
            var input=offer with {Valid=pending.HasValue,Ready=read("s_axis_tready")!=0,Reset=reset};
            var output=new AxiStreamObservation(read("m_axis_tdata"),read("m_axis_tkeep"),read("m_axis_tuser"),
                read("m_axis_tlast")!=0,read("m_axis_tvalid")!=0,ready,reset);
            bool sent=source.Observe(input),received=sink.Observe(output);
            if(reset){expected.Clear();pending=null;}
            else
            {
                if(received)
                {
                    if(!expected.TryDequeue(out var beat)||beat!=output)
                        throw new InvalidOperationException($"FIFO order/sideband mismatch at edge {edge}.");
                }
                if(sent){expected.Enqueue(input with {Ready=true});pending=null;}
            }
            clock(reset);
        }
        if(expected.Count!=0||pending.HasValue)throw new InvalidOperationException("FIFO drain watchdog expired.");
    }

    /// <summary>
    /// Independently pair AW/W in either order, hold B/R responses, check all
    /// strobes and error addresses, and reset buffered requests/responses. Commands
    /// are bounded at 256; source transactions remain legal under shrinking.
    /// </summary>
    public static void LiteTraffic(byte[] commands,Action<string,int> drive,Func<string,int> read,Action<bool> clock)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var monitor=new AxiLiteConformance(8,1);
        var inputs=new Dictionary<string,int>();var externalDrive=drive;
        drive=(name,value)=>{inputs[name]=value;externalDrive(name,value);};
        int Input(string suffix)=>inputs["s_axi_"+suffix];
        byte[] initial={0x78,0x56,0x34,0x12,0xDD,0xCC,0xBB,0xAA};var memory=(byte[])initial.Clone();
        foreach(string name in new[]{"awvalid","wvalid","arvalid","bready","rready","awaddr","araddr","awprot","arprot","wdata","wstrb"})drive("s_axi_"+name,0);
        clock(true);
        AxiLiteObservation Sample(bool reset=false)=>new((uint)Input("awaddr"),(byte)Input("awprot"),Input("awvalid")!=0,read("s_axi_awready")!=0,
            unchecked((uint)Input("wdata")),(byte)Input("wstrb"),Input("wvalid")!=0,read("s_axi_wready")!=0,
            (byte)read("s_axi_bresp"),read("s_axi_bvalid")!=0,Input("bready")!=0,
            (uint)Input("araddr"),(byte)Input("arprot"),Input("arvalid")!=0,read("s_axi_arready")!=0,
            unchecked((uint)read("s_axi_rdata")),(byte)read("s_axi_rresp"),read("s_axi_rvalid")!=0,Input("rready")!=0,reset);
        void Reset()
        {monitor.Observe(Sample(true));clock(true);memory=(byte[])initial.Clone();}
        // AR handshakes on the buffered write's commit edge: the held response
        // must keep the pre-write snapshot while the register itself changes.
        drive("s_axi_wdata",0x01020304);drive("s_axi_wstrb",15);
        for(int edge=0;edge<5;edge++)
        {
            drive("s_axi_awvalid",edge==0?1:0);drive("s_axi_wvalid",edge==0?1:0);drive("s_axi_arvalid",edge==1?1:0);
            drive("s_axi_bready",edge==4?1:0);drive("s_axi_rready",edge==4?1:0);
            var sample=Sample();monitor.Observe(sample);
            if(edge==0&&(!sample.AwReady||!sample.WriteReady||!sample.ArReady))throw new InvalidOperationException("Reset peripheral did not accept initial requests.");
            if(edge==1&&!sample.ArReady)throw new InvalidOperationException("Concurrent read was not accepted.");
            if(sample.ReadValid&&(sample.ReadData!=0x12345678||sample.ReadResponse!=0))throw new InvalidOperationException("Read/write collision lost the old-data snapshot.");
            if(sample.WriteResponseValid&&sample.WriteResponse!=0)throw new InvalidOperationException("Collision write failed.");
            if(edge==4&&(!sample.ReadValid||!sample.WriteResponseValid))throw new InvalidOperationException("Collision response watchdog expired.");
            clock(false);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(0,4),0x01020304);
        foreach(byte command in commands.Take(256))
        {
            uint address=new uint[]{0,4,8,1,12}[command%5];uint data=unchecked(0xA5000000U+command*0x010101U);
            byte strobes=(byte)((command>>2)&15);bool aw=false,w=false,complete=false,aborted=false;
            drive("s_axi_arvalid",0);drive("s_axi_rready",0);
            drive("s_axi_awaddr",(int)address);drive("s_axi_awprot",command&7);
            drive("s_axi_wdata",unchecked((int)data));drive("s_axi_wstrb",strobes);
            for(int edge=0;edge<64;edge++)
            {
                drive("s_axi_awvalid",!aw&&edge>=(command&3)?1:0);
                drive("s_axi_wvalid",!w&&edge>=((command>>2)&3)?1:0);
                drive("s_axi_bready",edge>=8+((command>>6)&1)*3?1:0);
                if((command&192)==128&&edge==4){Reset();aborted=true;break;}
                var sample=Sample();monitor.Observe(sample);
                if(sample.WriteResponseValid&&sample.WriteResponse!=(address is 0 or 4?0:2))
                    throw new InvalidOperationException("Incorrect Lite write response.");
                aw|=sample.AwValid&&sample.AwReady;w|=sample.WriteValid&&sample.WriteReady;
                bool response=sample.WriteResponseValid&&sample.WriteResponseReady;clock(false);
                if(response){complete=true;break;}
            }
            if(!complete&&!aborted)throw new InvalidOperationException("Lite write watchdog expired.");
            if(complete&&address is 0 or 4)
                for(int lane=0;lane<4;lane++)if((strobes&(1<<lane))!=0)memory[(int)address+lane]=(byte)(data>>(8*lane));
            drive("s_axi_awvalid",0);drive("s_axi_wvalid",0);drive("s_axi_bready",0);
            drive("s_axi_araddr",(int)address);drive("s_axi_arprot",(command>>3)&7);
            bool ar=false,readComplete=false,readReset=false;
            for(int edge=0;edge<64;edge++)
            {
                drive("s_axi_arvalid",ar?0:1);drive("s_axi_rready",edge>=4+command%3?1:0);
                if((command&192)==192&&!readReset&&edge==2)
                {Reset();readReset=true;ar=false;edge=-1;continue;}
                var sample=Sample();monitor.Observe(sample);
                uint expected=address is 0 or 4?BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan((int)address,4)):address==8?0xCAFEBABE:0;
                if(sample.ReadValid&&(sample.ReadData!=expected||sample.ReadResponse!=(address is 0 or 4 or 8?0:2)))
                    throw new InvalidOperationException("Incorrect Lite read snapshot/data/response.");
                ar|=sample.ArValid&&sample.ArReady;bool response=sample.ReadValid&&sample.ReadReady;clock(false);
                if(response){readComplete=true;break;}
            }
            if(!readComplete)throw new InvalidOperationException("Lite read watchdog expired.");
            drive("s_axi_arvalid",0);drive("s_axi_rready",0);
        }
    }
}
