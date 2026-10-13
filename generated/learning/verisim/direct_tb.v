`timescale 1ns/1ps
module tb;reg clk=0,rst=0;
reg sink_ready=0;
integer direct_sent=0,direct_received=0;
wire [7:0] s_direct_in_tdata=10+direct_sent;
wire s_direct_in_tvalid=(direct_sent<3);
wire s_direct_in_tkeep=1;
wire s_direct_in_tlast=(direct_sent==2);
wire [1:0] s_direct_in_tuser=direct_sent;
wire s_direct_in_tready;
wire [7:0] s_direct_out_tdata;
wire s_direct_out_tkeep,s_direct_out_tlast,s_direct_out_tvalid;
wire [1:0] s_direct_out_tuser;
wire s_direct_out_tready=sink_ready;
wire s_direct_accepted,s_direct_delivered;
reg direct_held=0;reg [11:0] direct_previous;
reg direct_push,direct_pop;
integer buffered_sent=0,buffered_received=0;
wire [7:0] s_buffered_in_tdata=10+buffered_sent;
wire s_buffered_in_tvalid=(buffered_sent<3);
wire s_buffered_in_tkeep=1;
wire s_buffered_in_tlast=(buffered_sent==2);
wire [1:0] s_buffered_in_tuser=buffered_sent;
wire s_buffered_in_tready;
wire [7:0] s_buffered_out_tdata;
wire s_buffered_out_tkeep,s_buffered_out_tlast,s_buffered_out_tvalid;
wire [1:0] s_buffered_out_tuser;
wire s_buffered_out_tready=sink_ready;
wire s_buffered_accepted,s_buffered_delivered;
reg buffered_held=0;reg [11:0] buffered_previous;
reg buffered_push,buffered_pop;
h_Backpressure dut(.*);
// Inspect pre-edge handshake values. Counters use nonblocking updates at the edge, like
// separate clocked producers and receivers, never in a race with the DUT.
task tick;begin
#2;
if(!rst) begin
 if(direct_held && (s_direct_out_tvalid!==1 || {s_direct_out_tuser,s_direct_out_tlast,s_direct_out_tkeep,s_direct_out_tdata}!==direct_previous))$fatal(1,"direct stalled beat changed");
 direct_held=s_direct_out_tvalid && !sink_ready;
 direct_previous={s_direct_out_tuser,s_direct_out_tlast,s_direct_out_tkeep,s_direct_out_tdata};
 if(s_direct_delivered)begin
  if(direct_received>=3 || s_direct_out_tdata!==(8'd10+direct_received) || s_direct_out_tkeep!==1 || s_direct_out_tlast!==(direct_received==2) || s_direct_out_tuser!==direct_received)$fatal(1,"direct lost, duplicated, reordered or damaged beat");
 end
end
direct_push=!rst && s_direct_accepted;direct_pop=!rst && s_direct_delivered;
if(!rst) begin
 if(buffered_held && (s_buffered_out_tvalid!==1 || {s_buffered_out_tuser,s_buffered_out_tlast,s_buffered_out_tkeep,s_buffered_out_tdata}!==buffered_previous))$fatal(1,"buffered stalled beat changed");
 buffered_held=s_buffered_out_tvalid && !sink_ready;
 buffered_previous={s_buffered_out_tuser,s_buffered_out_tlast,s_buffered_out_tkeep,s_buffered_out_tdata};
 if(s_buffered_delivered)begin
  if(buffered_received>=3 || s_buffered_out_tdata!==(8'd10+buffered_received) || s_buffered_out_tkeep!==1 || s_buffered_out_tlast!==(buffered_received==2) || s_buffered_out_tuser!==buffered_received)$fatal(1,"buffered lost, duplicated, reordered or damaged beat");
 end
end
buffered_push=!rst && s_buffered_accepted;buffered_pop=!rst && s_buffered_delivered;
clk=1;
direct_sent<=direct_sent+direct_push;direct_received<=direct_received+direct_pop;
buffered_sent<=buffered_sent+buffered_push;buffered_received<=buffered_received+buffered_pop;
#2;clk=0;end endtask
initial begin
rst=1;tick;rst=0;
tick; // Cycle 1: buffered head captures 10; direct source holds 10.
if(direct_sent!==0 || buffered_sent!==1 || buffered_received!==0)$fatal(1,"first slot");
tick; // Cycle 2: spill captures 11.
if(buffered_sent!==2 || buffered_received!==0)$fatal(1,"spill slot");
tick; // Cycle 3: both producers must wait.
if(direct_sent!==0 || buffered_sent!==2)$fatal(1,"full capacity");
sink_ready=1;#1;
if(s_direct_in_tready!==1 || s_buffered_in_tready!==0)$fatal(1,"ready path isolation");
tick; // Cycle 4: both deliver 10; full skid cannot yet accept 12.
if(direct_sent!==1 || buffered_sent!==2 || direct_received!==1 || buffered_received!==1)$fatal(1,"full recovery");
tick; // Cycle 5: deliver 11, skid accepts 12.
if(direct_sent!==2 || buffered_sent!==3 || direct_received!==2 || buffered_received!==2)$fatal(1,"simultaneous push pop");
tick; // Cycle 6: both deliver 12, including LAST and USER.
tick; // Cycle 7: no duplicate delivery while empty.
if(direct_sent!==3 || buffered_sent!==3 || direct_received!==3 || buffered_received!==3)$fatal(1,"drain");
$display("PASS VeriSim direct");$finish;end
initial begin $dumpfile("dump.vcd");$dumpvars(1,tb);end
endmodule
