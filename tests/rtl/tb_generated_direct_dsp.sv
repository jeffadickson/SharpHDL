`timescale 1ns/1ps
module tb_generated_direct_dsp;
  reg clk=0,rst=0;
  reg [15:0] a=0,b=0;
  wire [31:0] product;
  integer i;
  h_DirectDsp dut(.clk(clk),.rst(rst),.s_a(a),.s_b(b),.s_product(product));
  initial begin
    for(i=0;i<500;i=i+1)begin
      a=$random;b=$random;#1;
      if(product!==(a*b))$fatal(1,"Generated C# DSP mismatch iteration %0d",i);
    end
    $display("PASS C# -> explicit DSP48E2 RTL wrapper: 500 vectors");
    $finish;
  end
endmodule
