`timescale 1ns/1ps
module tb; reg clk=0; reg rst=1;
reg [63:0] word=0;
wire [63:0] word_out;
h_HeaderRewrite dut(.clk(clk),.rst(rst),.s_word(word),.s_word_out(word_out)); initial begin #1; clk=1; #1; clk=0; rst=0;
word=64'd2450244826532435576;
rst=0; #1;
if (word_out !== 64'd2307818488316843640) $fatal(1, "HeaderRewrite cycle 0 port word_out");
clk=1; #1; clk=0;
$display("PASS HDL HeaderRewrite");$finish;end endmodule
