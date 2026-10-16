`timescale 1ns/1ps
module tb_cil_core;
  reg clk=0,rst=1;
  reg [31:0] in_data=0;
  reg in_valid=0,out_ready=0;
  wire legacy_in_ready,legacy_out_valid,native_in_ready,native_out_valid;
  wire [31:0] legacy_out_data,native_out_data;
  reg [31:0] expected[0:2047];
  integer head=0,tail=0,cycle,accepted=0,completed=0;
  h_CilCoreDemo legacy(
    .clk(clk),.rst(rst),
    .s_in_data(in_data),.s_in_valid(in_valid),.s_in_ready(legacy_in_ready),
    .s_out_data(legacy_out_data),.s_out_valid(legacy_out_valid),
    .s_out_ready(out_ready));
  h_CilCoreNative native_dut(
    .clk(clk),.rst(rst),
    .s_in_data(in_data),.s_in_valid(in_valid),.s_in_ready(native_in_ready),
    .s_out_data(native_out_data),.s_out_valid(native_out_valid),
    .s_out_ready(out_ready));
  always #5 clk=~clk;
  initial begin
    repeat(3) @(negedge clk);
    rst=0;
    for(cycle=0;cycle<750;cycle=cycle+1) begin
      @(negedge clk);
      in_data=$urandom;
      in_valid=(cycle<650 && $urandom_range(0,3)!=0);
      out_ready=(cycle>=650 || $urandom_range(0,2)!=0);
      #1;
      if(legacy_in_ready!==native_in_ready ||
         legacy_out_valid!==native_out_valid)
        $fatal(1,"CIL Core ready/valid mismatch at cycle %0d",cycle);
      if(legacy_out_valid && legacy_out_data!==native_out_data)
        $fatal(1,"CIL Core payload mismatch at cycle %0d",cycle);
      if(legacy_out_valid && out_ready) begin
        if(head>=tail)$fatal(1,"Output without input");
        if(legacy_out_data!==expected[head])
          $fatal(1,"CIL Core arithmetic mismatch at cycle %0d",cycle);
        head=head+1;completed=completed+1;
      end
      if(in_valid && legacy_in_ready) begin
        expected[tail]=(in_data+32'd11)*in_data+32'd9;
        tail=tail+1;accepted=accepted+1;
      end
      @(posedge clk);
    end
    if(head!=tail || accepted!=completed || accepted<100)
      $fatal(1,"CIL Core lost/duplicated tokens: %0d %0d",accepted,completed);
    @(negedge clk);
    in_valid=1;out_ready=0;
    repeat(8) @(posedge clk);
    @(negedge clk);
    rst=1;in_valid=0;
    @(posedge clk);
    #1;
    if(legacy_out_valid!==0 || native_out_valid!==0)
      $fatal(1,"CIL Core reset did not flush stalled tokens");
    @(negedge clk);
    rst=0;out_ready=1;
    repeat(5) begin
      @(posedge clk);#1;
      if(legacy_out_valid!==0 || native_out_valid!==0)
        $fatal(1,"CIL Core ghost token after reset");
    end
    $display("PASS production CIL -> Core HLS -> legacy/native RTL: %0d/%0d tokens, stall/reset",accepted,completed);
    $finish;
  end
endmodule
