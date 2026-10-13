`timescale 1ns/1ps
module tb;reg clk=0,rst=0;
reg s_start=0,s_enable=1,s_stop=0;
wire [1:0] s_phase; wire [3:0] s_ticks; wire s_running,s_done;
h_DrillController dut(.*);
initial begin rst=1; #2; clk=1; #2; clk=0;rst=0;s_start=1;
#2;clk=1;#2;clk=0;s_start=0;
repeat(4) begin #2;clk=1;#2;clk=0;end
if(s_done!==1 || s_ticks!==4)$fatal(1,"controller");
$display("PASS VeriSim fsm");$finish;end
initial begin $dumpfile("dump.vcd");$dumpvars(0,tb);end
endmodule
