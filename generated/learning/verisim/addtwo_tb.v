`timescale 1ns/1ps
module tb;reg clk=0,rst=0;
reg [7:0] s_b=0;wire [7:0] s_a;
h_AddTwo dut(.*);
initial begin #5;if(s_a!==2)$fatal(1,"0+2");
s_b=7;#5;if(s_a!==9)$fatal(1,"7+2");
s_b=254;#5;if(s_a!==0)$fatal(1,"eight-bit wrap");
$display("PASS VeriSim addtwo");$finish;end
initial begin $dumpfile("dump.vcd");$dumpvars(1,tb);end
endmodule
