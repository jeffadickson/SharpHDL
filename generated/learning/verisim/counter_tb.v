`timescale 1ns/1ps
module tb;reg clk=0,rst=0;
reg s_enable=0;wire [7:0] s_value;
h_Counter dut(.*);
task tick;begin #2;clk=1;#2;clk=0;end endtask
initial begin rst=1;tick;rst=0;
if(s_value!==0)$fatal(1,"reset");s_enable=1;repeat(3)tick;
if(s_value!==3)$fatal(1,"count");s_enable=0;repeat(2)tick;
if(s_value!==3)$fatal(1,"hold");s_enable=1;tick;
if(s_value!==4)$fatal(1,"resume");
$display("PASS VeriSim counter");$finish;end
initial begin $dumpfile("dump.vcd");$dumpvars(1,tb);end
endmodule
