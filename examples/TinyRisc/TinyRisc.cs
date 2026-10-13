using System;
using System.Linq;
using System.Numerics;
using SharpHDL;
using Module=SharpHDL.Module;
// TinyRisc16: explicit hardware construction, not a compiled CPU emulator.
public static class TinyRisc
{
    static Expr K(int w,int v){return Expr.Const(w,v);}
    static Expr M(Expr c,Expr a,Expr b){return Expr.Mux(c,a,b);}
    static Expr Shift(Expr a,Expr n,bool left){for(int i=0;i<4;i++)a=M(n.Slice(i,i),left?a<<(1<<i):a>>(1<<i),a);return a;}
    public static Module Build(ushort[] program,string name="TinyRisc16")
    {
        if(program==null||program.Length<1||program.Length>256)throw new ArgumentException("ROM needs 1..256 words.");
        var m=new Module(name);
        var run=m.Input("run",1);var step=m.Input("step",1);var sw=m.Input("switches",16);
        var rxv=m.Input("rx_valid",1);var rxd=m.Input("rx_data",8);var txr=m.Input("tx_ready",1);
        run.Describe("Run continuously when high; low pauses at an instruction boundary. Does not abort a stalled instruction.");
        step.Describe("Rising request while paused at a fetch boundary executes one instruction; held high does not repeat.");
        sw.Describe("16 input switch bits, read at memory-mapped word address FF01.");
        rxv.Describe("Received byte available; producer holds rx_data while rx_valid and not rx_ready.");rxd.Describe("Received byte bits[7:0], zero extended by LD from FF03.");txr.Describe("Consumer can accept one transmitted byte at this rising edge.");
        var pc=m.Register("pc_state",16,0);var ir=m.Register("instruction",16,0);var phase=m.Register("phase_state",2,0);
        var halted=m.Register("halted_state",1,0);var fault=m.Register("fault_state",1,0);var leds=m.Register("led_state",16,0);
        var retired=m.Register("retired_state",1,0);var lastpc=m.Register("last_pc_state",16,0);var prev=m.Register("previous_step",1,0);
        pc.Describe("Word-addressed next/current instruction PC; reset 0. During execute it still identifies the executing instruction.");
        ir.Describe("Instruction: opcode[15:12], rd[11:9], ra[8:6], rb[5:3] or immediate[5:0]; LDI immediate[8:0]. See ISA.md.");
        phase.Describe("00 fetch/paused boundary; 01 execute or peripheral wait; 10 synchronous RAM load completion; 11 unused.");
        var regs=Enumerable.Range(1,7).Select(i=>m.Register("r"+i+"_state",16,0)).ToArray();
        Func<Expr,Expr> read=index=>{Expr x=K(16,0);for(int i=1;i<8;i++)x=M(index.Eq(K(3,i)),regs[i-1],x);return x;};
        Expr op=ir.Slice(15,12),rd=ir.Slice(11,9),ra=ir.Slice(8,6),rb=ir.Slice(5,3),a=read(ra),b=read(rb),d=read(rd);
        Expr imm=ir.Slice(5,0).SignExtend(16),addr=a+imm,nextpc=pc+K(16,1);
        Expr live=~halted,fetch=phase.Eq(K(2,0))&live&(run|(step&~prev)),exec=phase.Eq(K(2,1))&live,loadwait=phase.Eq(K(2,2))&live;
        Expr romok=pc.Lt(K(16,program.Length)),ramok=addr.Lt(K(16,256));
        Func<int,Expr> isop=x=>op.Eq(K(4,x));Func<int,Expr> at=x=>addr.Eq(K(16,x));
        Expr load=isop(9),store=isop(10),tx=exec&store&at(0xff02),rx=exec&load&at(0xff03);
        var ram=SynchronousRam.Create(m,"scratch",16,256,addr,exec&store&ramok,d,exec&load&ramok);
        Expr legal=K(1,0),value=K(16,0),write=K(1,0),dest=rd,target=nextpc,wait=K(1,0),stop=K(1,0);
        for(int code=0;code<=6;code++)
        {
            Expr sel=isop(code)&ir.Slice(2,0).Eq(K(3,0));legal=legal|sel;write=write|sel;
            Expr v=code==0?a+b:code==1?a-b:code==2?a&b:code==3?a|b:code==4?a^b:code==5?Shift(a,b,true):Shift(a,b,false);
            value=M(sel,v,value);
        }
        legal=legal|isop(7)|isop(8);write=write|isop(7)|isop(8);value=M(isop(7),a+imm,value);value=M(isop(8),ir.Slice(8,0).SignExtend(16),value);
        Expr loadok=ramok|at(0xff00)|at(0xff01)|at(0xff03)|at(0xff04),storeok=ramok|at(0xff00)|at(0xff02);
        legal=legal|(load&loadok)|(store&storeok);write=write|(load&~ramok&loadok);
        Expr status=Expr.Concat(txr,rxv).Resize(16);
        value=M(load,M(at(0xff00),leds,M(at(0xff01),sw,M(at(0xff03),rxd.Resize(16),status))),value);
        wait=(store&at(0xff02)&~txr)|(load&at(0xff03)&~rxv);
        Expr branch=isop(11)|isop(12);legal=legal|branch;target=M(branch&M(isop(11),d.Eq(a),~d.Eq(a)),nextpc+imm,target);
        Expr jump=isop(13)&ir.Slice(5,0).Eq(K(6,0));legal=legal|jump;write=write|jump;value=M(jump,nextpc,value);target=M(jump,a,target);
        Expr halt=ir.Eq(K(16,0xf000)),nop=ir.Eq(K(16,0xf001));legal=legal|halt|nop;stop=halt;
        Expr trap=exec&~legal,badfetch=fetch&~romok,finish=exec&legal&~wait&~(load&ramok),complete=finish|loadwait;
        m.Next(prev,step);m.Next(retired,complete);m.NextWhen(lastpc,complete,pc);
        m.NextWhen(ir,fetch&romok,Expr.Lookup(16,pc,program.Select(x=>(BigInteger)x)));
        Expr phaseNext=M(fetch&romok,K(2,1),phase);phaseNext=M(exec&load&ramok,K(2,2),phaseNext);phaseNext=M(complete|trap|badfetch,K(2,0),phaseNext);m.Next(phase,phaseNext);
        m.NextWhen(pc,complete,M(loadwait,nextpc,target));m.NextWhen(halted,trap|badfetch|(finish&stop),K(1,1));m.NextWhen(fault,trap|badfetch,K(1,1));
        m.NextWhen(leds,exec&store&at(0xff00),d);
        for(int i=1;i<8;i++)m.NextWhen(regs[i-1],rd.Eq(K(3,i))&((finish&write)|loadwait),M(loadwait,ram.ReadData,value));
        Action<string,int,Expr,string> output=(n,w,e,note)=>{var p=m.Output(n,w);p.Describe(note);m.Drive(p,e);};
        output("tx_valid",1,tx,"Byte transfer only at rising clk when tx_valid && tx_ready; stable while stalled. UART byte interface, not a serial pin.");output("tx_data",8,d.Slice(7,0),"Transmit byte bits[7:0] from ST to FF02; upper source bits discarded.");output("rx_ready",1,rx,"LD FF03 requests a byte; transfer at rising clk with rx_valid.");
        output("pc",16,pc,"Word-addressed PC; instruction memory has configured finite bounds.");output("ir",16,ir,"Latched instruction; unchanged at a paused fetch boundary.");output("phase",2,phase,"0 boundary/fetch, 1 execute/peripheral wait, 2 RAM read completion.");
        output("halted",1,halted,"Sticky HALT or fault; synchronous reset resumes from PC 0.");output("fault",1,fault,"Sticky invalid opcode/encoding/address or out-of-ROM fetch. No architectural instruction retirement on fault.");output("retired",1,retired,"One-edge instruction retirement pulse; includes HALT, excludes faults.");output("last_pc",16,lastpc,"Address of most recently retired instruction.");output("leds",16,leds,"16-bit output latch at FF00; reset clears output.");
        for(int i=0;i<8;i++)output("r"+i,16,i==0?K(16,0):regs[i-1],"Architectural register R"+i+(i==0?" always reads zero; writes discarded.":"; unsigned bit pattern, arithmetic wraps modulo 65536."));
        return m;
    }
}
