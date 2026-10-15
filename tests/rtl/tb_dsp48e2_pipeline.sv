`timescale 1ns/1ps
module tb_dsp48e2_pipeline;
  reg clk=0,rst=1,input_valid=0;
  reg [15:0] a=0,b=0;
  wire output_valid;
  wire [31:0] product;
  reg [31:0] expected0=0,expected1=0,expected2=0;
  reg [2:0] valid=0;
  integer i;
  sharphdl_dsp48e2_mul_pipeline #(.A_WIDTH(16),.B_WIDTH(16)) dut(
    .clk(clk),.rst(rst),.input_valid(input_valid),.a(a),.b(b),
    .output_valid(output_valid),.product(product));
  always #5 clk=~clk;
  always @(posedge clk) begin
    if(rst) begin
      valid<=0;expected0<=0;expected1<=0;expected2<=0;
    end else begin
      valid<={valid[1:0],input_valid};
      expected2<=expected1;expected1<=expected0;expected0<=a*b;
    end
    #1;
    if(output_valid!==valid[2])$fatal(1,"DSP valid pipeline misalignment");
    if(output_valid && product!==expected2)
      $fatal(1,"DSP data pipeline mismatch: %h expected %h",product,expected2);
  end
  initial begin
    repeat(3)@(negedge clk);
    rst=0;
    for(i=0;i<200;i=i+1)begin
      @(negedge clk);
      a=$random;b=$random;input_valid=(i%7!=0);
    end
    @(negedge clk);input_valid=0;
    repeat(5)@(negedge clk);
    $display("PASS DSP48E2 three-stage behavioral pipeline with valid gaps");
    $finish;
  end
endmodule
