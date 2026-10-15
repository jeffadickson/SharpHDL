`timescale 1ns/1ps
// End-to-end HLS provenance fixture: C# Polynomial -> generated RTL ->
// Verilator -> VCD containing generated internal nets and the result handshake.
module tb_scheduled_polynomial;
  reg clk=0, rst=1;
  reg [15:0] input_data=16'd3;
  reg input_valid=0;
  wire input_ready;
  wire [15:0] output_tdata;
  wire output_tvalid;
  reg output_tready=1;
  integer cycles=0;
  h_ScheduledPolynomial1 dut(
    .clk(clk),.rst(rst),
    .s_input_data(input_data),.s_input_valid(input_valid),.s_input_ready(input_ready),
    .s_output_tdata(output_tdata),.s_output_tvalid(output_tvalid),
    .s_output_tready(output_tready)
  );
  always #5 clk=~clk;
  initial begin
    $dumpfile("generated/vscode/polynomial.vcd");
    $dumpvars(0,dut);
    $dumpvars(0,clk,rst,input_data,input_valid,input_ready,output_tdata,output_tvalid,output_tready);
    repeat(4) @(negedge clk);
    rst=0;
    input_valid=1;
    @(posedge clk);
    if(!input_ready) $fatal(1,"Input pipeline unexpectedly stalled");
    @(negedge clk);
    input_valid=0;
    wait(output_tvalid);
    if(output_tdata !== 16'd46) $fatal(1,"HLS result mismatch: expected 46 got %0d",output_tdata);
    @(posedge clk);
    $display("PASS scheduled CIL pipeline Verilator: (3+2)*(3*3)^3 = 46");
    $finish;
  end
  always @(posedge clk) begin
    cycles<=cycles+1;
    if(cycles>100) $fatal(1,"HLS pipeline timeout");
  end
endmodule
