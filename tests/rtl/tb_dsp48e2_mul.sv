`timescale 1ns/1ps
module tb_dsp48e2_mul;
  reg clk=0;
  reg [15:0] a=0,b=0;
  wire [31:0] product;
  integer i;
  sharphdl_dsp48e2_mul_unsigned #(.A_WIDTH(16),.B_WIDTH(16))
      dut(.clk(clk),.a(a),.b(b),.product(product));
  initial begin
    a=0;b=0;#1;
    if(product!==32'd0)$fatal(1,"zero mismatch");
    a=16'hffff;b=16'hffff;#1;
    if(product!==32'hfffe0001)$fatal(1,"max mismatch");
    for(i=0;i<1000;i=i+1)begin
      a=$random;b=$random;#1;
      if(product!==(a*b))$fatal(1,"DSP behavioral mismatch iteration %0d",i);
    end
    $display("PASS DSP48E2 wrapper behavioral 16x16: 1002 vectors");
    $finish;
  end
endmodule
