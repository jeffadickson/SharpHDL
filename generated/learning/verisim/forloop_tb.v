`timescale 1ns/1ps
module tb;reg clk=0,rst=0;
reg [31:0] s_a=2,s_b=3;wire [31:0] s_y;wire s_fault;
h_CilCsLoop12 dut(.*);
initial begin #5;if(s_y!==38 || s_fault!==0)$fatal(1,"unrolled");
$display("PASS VeriSim forloop");$finish;end
initial begin $dumpfile("dump.vcd");$dumpvars(0,tb);end
endmodule
