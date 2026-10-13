`timescale 1ns/1ps
module tb;reg clk=0,rst=0;
reg s_start=0;reg [31:0] s_arg0=3,s_arg1=10;
wire s_busy,s_done,s_fault,s_assertion_failed,s_timeout,s_accept;wire [31:0] s_result;
h_CsSeqRuntimeWhile dut(.*);
initial begin rst=1;#2;clk=1;#2;clk=0;rst=0;s_start=1;
#2;clk=1;#2;clk=0;s_start=0;
repeat(250) begin #2;clk=1;#2;clk=0;end
if(s_done!==1 || s_fault!==0 || s_result!==16)$fatal(1,"sequential");
$display("PASS VeriSim sequential");$finish;end
initial begin $dumpfile("dump.vcd");$dumpvars(1,tb);end
endmodule
