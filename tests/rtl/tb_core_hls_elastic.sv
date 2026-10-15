`timescale 1ns/1ps
// Differential acceptance of a pipeline generated entirely from Core IR.
// Tests token ordering, arithmetic, random backpressure, and full drain.
module tb_core_hls_elastic;
  reg clk=0, rst=1;
  reg [15:0] input_data=0;
  reg input_valid=0, output_ready=0;
  wire input_ready, output_valid;
  wire [15:0] output_data;
  reg [15:0] expected[0:2047];
  integer head=0,tail=0,accepted=0,completed=0,cycle;
  h_CoreHlsElastic dut(
    .clk(clk),.rst(rst),
    .s_hls_input_data(input_data),.s_hls_input_valid(input_valid),
    .s_hls_input_ready(input_ready),.s_hls_output_data(output_data),
    .s_hls_output_valid(output_valid),.s_hls_output_ready(output_ready)
  );
  always #5 clk=~clk;
  initial begin
    repeat(4) @(negedge clk);
    rst=0;
    for(cycle=0;cycle<650;cycle=cycle+1) begin
      @(negedge clk);
      input_data=16'($urandom_range(0,65535));
      input_valid=cycle<550 ? ($urandom_range(0,3)!=0) : 0;
      output_ready=cycle>=550 ? 1 : ($urandom_range(0,2)!=0);
      @(posedge clk);
      if(output_valid && output_ready) begin
        if(head>=tail)$fatal(1,"Output without accepted token at cycle %0d",cycle);
        if(output_data!==expected[head])
          $fatal(1,"Core HLS output mismatch cycle %0d: got %h expected %h",
                 cycle,output_data,expected[head]);
        head=head+1;
        completed=completed+1;
      end
      if(input_valid && input_ready) begin
        expected[tail]=((input_data+16'd11)*input_data+16'd9);
        tail=tail+1;
        accepted=accepted+1;
      end
    end
    if(head!=tail)$fatal(1,"Core HLS lost %0d accepted tokens",tail-head);
    if(accepted<100)$fatal(1,"Core HLS insufficient randomized coverage");
    if(completed!=accepted)$fatal(1,"Core HLS token count mismatch");
    $display("PASS Core IR elastic HLS Verilator: accepted=%0d completed=%0d",
             accepted,completed);
    $finish;
  end
endmodule
