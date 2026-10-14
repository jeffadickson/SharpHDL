`timescale 1ns/1ps
module tb;
reg aclk=0,aresetn=0;
reg [15:0] s_axis_tdata=0;
reg [1:0] s_axis_tkeep=0;
reg s_axis_tlast=0,s_axis_tvalid=0;
reg [7:0] s_axis_tuser=0;
wire s_axis_tready;
wire [31:0] m_axis_tdata;
wire [3:0] m_axis_tkeep;
wire m_axis_tlast,m_axis_tvalid;
wire [7:0] m_axis_tuser;
reg m_axis_tready=0;
mixed_science dut(.*);
reg [44:0] expected[0:20000];
reg [44:0] held;
reg stalled=0;
integer head=0,tail=0,cycle,x,y,received=0,accepted=0,discarded=0,stall_count=0;
reg [31:0] energy;
reg [31:0] rng=32'h618ac39d;
reg take;
wire [44:0] actual={m_axis_tuser,m_axis_tlast,m_axis_tkeep,m_axis_tdata};
initial begin
  for(cycle=0;cycle<12000;cycle=cycle+1) begin
    aresetn=cycle>2 && cycle!=317 && cycle!=4001 && cycle!=7903;
    rng=rng^(rng<<13); rng=rng^(rng>>17); rng=rng^(rng<<5);
    m_axis_tready=cycle>=11000 ? 1 : (rng[2:0]!=0 && cycle%131<87);
    #5;
    if(!aresetn) begin
      discarded=discarded+tail-head; head=0;tail=0;stalled=0;
    end else begin
      if(stalled && (!m_axis_tvalid || actual!==held)) $fatal(1,"Changed stalled beat at cycle %0d",cycle);
      if(m_axis_tvalid && m_axis_tready) begin
        if(head==tail) $fatal(1,"Unexpected output");
        if(actual!==expected[head]) $fatal(1,"Mismatch cycle %0d actual=%h expected=%h",cycle,actual,expected[head]);
        head=head+1;received=received+1;
      end
      if(s_axis_tvalid && s_axis_tready) begin
        x=$signed(s_axis_tdata);y=x*2-10;
        if(y>32767)y=32767;
        if(y< -32768)y=-32768;
        energy=y*y;
        expected[tail]={s_axis_tuser,s_axis_tlast,(s_axis_tkeep==3?4'hf:4'h0),energy};
        tail=tail+1;accepted=accepted+1;
      end
      stalled=m_axis_tvalid && !m_axis_tready;held=actual;
      if(stalled)stall_count=stall_count+1;
    end
    take=s_axis_tvalid && s_axis_tready;
    aclk=1;#5;aclk=0;
    // A legal producer retains its offered beat throughout backpressure.
    if(!aresetn) s_axis_tvalid=0;
    else if(!s_axis_tvalid || take) begin
      s_axis_tvalid=cycle<11000 && rng[3:2]!=0;
      s_axis_tdata=rng[31:16];s_axis_tkeep=rng[7:6];
      s_axis_tlast=rng[11:8]==0;s_axis_tuser=rng[23:16];
    end
  end
  if(head!=tail || accepted!=received+discarded) $fatal(1,"Lost beats");
  if(received<1000 || stall_count<1000 || discarded==0) $fatal(1,"Insufficient coverage");
  $display("PASS mixed RTL: %0d accepted, %0d received, %0d reset-discarded, %0d stalled cycles",accepted,received,discarded,stall_count);
  $finish;
end
endmodule
