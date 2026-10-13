using System;
using System.Linq;
using System.Numerics;

namespace SharpHDL
{
    // Payload and sidebands are valid together; transfer = Valid & Ready.
    public sealed class Stream
    {
        public Signal Data, Keep, Last, User, Valid, Ready;
        public int Width { get { return Data.Width; } }
        public int UserWidth { get { return User.Width; } }
        static Stream Create(Module m,string name,int width,int userWidth,int direction)
        {
            if(width%8!=0) throw new ArgumentException("Stream data must be byte-aligned.");
            Func<string,int,Signal> forward=(n,w)=>direction==1?m.Input(n,w):direction==2?m.Output(n,w):m.Wire(n,w);
            Func<string,int,Signal> reverse=(n,w)=>direction==1?m.Output(n,w):direction==2?m.Input(n,w):m.Wire(n,w);
            var stream=new Stream { Data=forward(name+"_tdata",width),Keep=forward(name+"_tkeep",width/8),
                Last=forward(name+"_tlast",1),User=forward(name+"_tuser",userWidth),Valid=forward(name+"_tvalid",1),
                Ready=reverse(name+"_tready",1) };
            stream.Data.Describe(name+": TDATA ["+(width-1)+":0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.");
            stream.Keep.Describe(name+": TKEEP bit i qualifies TDATA byte lane i; transported with each beat.");
            stream.Last.Describe(name+": TLAST marks the final beat of a packet; meaningful with TVALID.");
            stream.User.Describe(name+": TUSER ["+(userWidth-1)+":0], application metadata carried with the same beat.");
            stream.Valid.Describe(name+": TVALID; producer offers a beat. Must remain asserted during a stall.");
            stream.Ready.Describe(name+": TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.");
            return stream;
        }
        public static Stream Input(Module m,string name,int width,int userWidth=1) { return Create(m,name,width,userWidth,1); }
        public static Stream Output(Module m,string name,int width,int userWidth=1) { return Create(m,name,width,userWidth,2); }
        public static Stream Internal(Module m,string name,int width,int userWidth=1) { return Create(m,name,width,userWidth,0); }
        public Expr Fire { get { return Valid & Ready; } }
        public static void Connect(Module m,Stream source,Stream sink)
        {
            if(source.Width!=sink.Width || source.UserWidth!=sink.UserWidth) throw new ArgumentException("Stream shape mismatch.");
            sink.Data.Describe(source.Data.Annotation);sink.User.Describe(source.User.Annotation);
            m.Drive(sink.Data,source.Data);m.Drive(sink.Keep,source.Keep);m.Drive(sink.Last,source.Last);
            m.Drive(sink.User,source.User);m.Drive(sink.Valid,source.Valid);m.Drive(source.Ready,sink.Ready);
        }
    }

    public static class StreamBlocks
    {
        // Replicate every input transaction exactly once to each consumer.
        // Consumers may accept on different cycles. Upstream holds the beat
        // until all consumers have accepted; flags prevent duplicate delivery.
        public static Stream[] Broadcast(Module m,Stream input,string name,int consumers)
        {
            if(consumers<1 || consumers>16)throw new ArgumentOutOfRangeException("consumers");
            var outputs=new Stream[consumers];var accepted=new Signal[consumers];
            Expr allReady=Expr.Const(1,1);
            for(int i=0;i<consumers;i++)
            {
                outputs[i]=Stream.Internal(m,name+"_"+i,input.Width,input.UserWidth);
                accepted[i]=m.Register(name+"_accepted"+i,1,0);
                allReady=allReady & (accepted[i] | outputs[i].Ready);
                m.Drive(outputs[i].Data,input.Data);m.Drive(outputs[i].Keep,input.Keep);
                m.Drive(outputs[i].Last,input.Last);m.Drive(outputs[i].User,input.User);
                m.Drive(outputs[i].Valid,input.Valid & ~accepted[i]);
            }
            m.Drive(input.Ready,allReady);
            for(int i=0;i<consumers;i++)
                m.Next(accepted[i],Expr.Mux(input.Fire,Expr.Const(1,0),accepted[i] | outputs[i].Fire));
            return outputs;
        }

        // One-entry elastic buffer. All payload bits hold while valid && !ready.
        public static Stream Map(Module m,Stream input,string name,int width,Func<Expr,Expr> map)
        {
            var output=Stream.Internal(m,name,width,input.UserWidth);
            var valid=m.Register(name+"_valid",1,0);
            var data=m.Register(name+"_data",width,0);
            var keep=m.Register(name+"_keep",width/8,0);
            var last=m.Register(name+"_last",1,0);
            var user=m.Register(name+"_user",input.UserWidth,0);
            var available=(~valid)|output.Ready;
            m.Drive(input.Ready,available);
            m.NextWhen(valid,available,input.Valid);
            var load=available & input.Valid;
            var transformed=map(input.Data);
            data.Describe(transformed.Annotation ?? (name+": registered "+width+"-bit transformed payload; holds during output stalls."));
            output.Data.Describe(data.Annotation);
            keep.Describe(name+": registered byte-valid mask; bit i qualifies payload bits [8*i+7:8*i].");
            last.Describe(name+": registered packet-end flag.");user.Describe(name+": registered TUSER metadata ["+(input.UserWidth-1)+":0].");
            valid.Describe(name+": output holding register occupied; clears on synchronous reset.");
            m.NextWhen(data,load,transformed);
            m.NextWhen(keep,load,width==input.Width?(Expr)input.Keep:Expr.Const(width/8,Expr.Mask(width/8)));
            m.NextWhen(last,load,input.Last);m.NextWhen(user,load,input.User);
            m.Drive(output.Valid,valid);m.Drive(output.Data,data);m.Drive(output.Keep,keep);
            m.Drive(output.Last,last);m.Drive(output.User,user);return output;
        }
        public static Stream Pipeline(Module m,Stream input,string name,int stages)
        {
            if(stages<1) throw new ArgumentOutOfRangeException("stages");
            var s=input;
            for(int i=0;i<stages;i++) s=Map(m,s,name+"_"+i,s.Width,x=>x);
            return s;
        }
        // Two-entry skid buffer: registered payload and no combinational ready path.
        public static Stream SkidBuffer(Module m,Stream input,string name)
        {
            var output=Stream.Internal(m,name,input.Width,input.UserWidth);
            output.Data.Describe(input.Data.Description);output.User.Describe(input.User.Description);
            int w=input.Width,k=input.Keep.Width,p=w+k+1+input.UserWidth;
            string layout="Packed stream beat: DATA ["+(w-1)+":0], KEEP ["+(w+k-1)+":"+w+"], LAST ["+(w+k)+"], USER ["+(p-1)+":"+(w+k+1)+"].";
            var head=m.Register(name+"_head",p,0);head.Describe(layout+" Output holding register.");
            var spill=m.Register(name+"_spill",p,0);spill.Describe(layout+" Captures the extra accepted beat when downstream stalls.");
            var valid=m.Register(name+"_valid",1,0);valid.Describe("Head contains a valid beat; reset clears it.");
            var full=m.Register(name+"_spill_valid",1,0);full.Describe("Spill occupied. Input ready is its inverse, independent of downstream ready.");
            var beat=Expr.Concat(input.User,Expr.Concat(input.Last,Expr.Concat(input.Keep,input.Data))).Describe(layout);
            var advance=~valid | output.Ready;
            m.Drive(input.Ready,~full);
            m.Next(valid,Expr.Mux(advance,full | input.Valid,valid));
            m.NextWhen(head,advance & (full | input.Valid),Expr.Mux(full,spill,beat));
            m.Next(full,Expr.Mux(advance,Expr.Const(1,0),full | input.Fire));
            m.NextWhen(spill,~advance & input.Fire,beat);
            m.Drive(output.Valid,valid);m.Drive(output.Data,head.Slice(w-1,0));
            m.Drive(output.Keep,head.Slice(w+k-1,w));m.Drive(output.Last,head.Slice(w+k,w+k));
            m.Drive(output.User,head.Slice(p-1,w+k+1));return output;
        }
        // Register-bank FIFO: explicit depth, no fall-through. Intended for small queues.
        public static Stream Fifo(Module m,Stream input,string name,int depth)
        {
            if(depth<1 || depth>256) throw new ArgumentOutOfRangeException("depth");
            int cw=1;while((BigInteger.One<<cw)<=depth)cw++;
            var n=m.Register(name+"_count",cw,0);
            var output=Stream.Internal(m,name,input.Width,input.UserWidth);
            var valid=~n.Eq(Expr.Const(cw,0));
            m.Drive(output.Valid,valid);
            var pop=valid & output.Ready;
            var room=n.Lt(Expr.Const(cw,depth)) | pop;
            m.Drive(input.Ready,room);
            var push=input.Valid & room;
            m.Next(n,Expr.Mux(push ^ pop,Expr.Mux(push,n+Expr.Const(cw,1),n-Expr.Const(cw,1)),n));
            int pw=input.Width+input.Keep.Width+1+input.UserWidth;
            var packed=Expr.Concat(input.User,Expr.Concat(input.Last,Expr.Concat(input.Keep,input.Data)));
            var slots=Enumerable.Range(0,depth).Select(i=>m.Register(name+"_slot"+i,pw,0)).ToArray();
            foreach(var slot in slots) slot.Describe("FIFO packed beat: DATA ["+(input.Width-1)+":0], KEEP ["+(input.Width+input.Keep.Width-1)+":"+input.Width+"], LAST ["+(input.Width+input.Keep.Width)+"], USER ["+(pw-1)+":"+(input.Width+input.Keep.Width+1)+"].");
            var index=Expr.Mux(pop,n-Expr.Const(cw,1),n);
            for(int i=0;i<depth;i++)
            {
                Expr shifted=i+1<depth?(Expr)slots[i+1]:Expr.Const(pw,0);
                var baseValue=Expr.Mux(pop,shifted,slots[i]);
                m.Next(slots[i],Expr.Mux(push & index.Eq(Expr.Const(cw,i)),packed,baseValue));
            }
            var head=slots[0];int w=input.Width,k=input.Keep.Width;
            m.Drive(output.Data,head.Slice(w-1,0));m.Drive(output.Keep,head.Slice(w+k-1,w));
            m.Drive(output.Last,head.Slice(w+k,w+k));m.Drive(output.User,head.Slice(pw-1,w+k+1));
            return output;
        }
        // Fair two-input arbiter; holds selected source across packet and across stalls.
        public static Stream Arbitrate(Module m,Stream a,Stream b,string name)
        {
            if(a.Width!=b.Width || a.UserWidth!=b.UserWidth)throw new ArgumentException("Stream shape mismatch.");
            var output=Stream.Internal(m,name,a.Width,a.UserWidth);
            var locked=m.Register(name+"_locked",1,0);var selected=m.Register(name+"_selected",1,0);
            var turn=m.Register(name+"_turn",1,0);
            var choose=Expr.Mux(locked,selected,Expr.Mux(a.Valid & b.Valid,turn,~a.Valid));
            var valid=Expr.Mux(choose,b.Valid,a.Valid);var fire=valid & output.Ready;
            var last=Expr.Mux(choose,b.Last,a.Last);
            // Latch selection when valid is offered, even before the first handshake.
            var offer=valid & ~locked;
            m.NextWhen(selected,offer,choose);
            m.Next(locked,Expr.Mux(fire & last,Expr.Const(1,0),Expr.Mux(offer,Expr.Const(1,1),locked)));
            m.NextWhen(turn,fire & last,~choose);
            m.Drive(a.Ready,output.Ready & ~choose);m.Drive(b.Ready,output.Ready & choose);
            m.Drive(output.Valid,valid);m.Drive(output.Data,Expr.Mux(choose,b.Data,a.Data));
            m.Drive(output.Keep,Expr.Mux(choose,b.Keep,a.Keep));m.Drive(output.Last,last);
            m.Drive(output.User,Expr.Mux(choose,b.User,a.User));return output;
        }
    }
}
