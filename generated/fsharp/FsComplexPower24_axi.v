// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_FsComplexPower24_axi(
  input wire aclk,
  input wire aresetn
  // Packed float operands: A [23:0], B [47:24]. Custom float: SIGN [23], EXP [22:16] bias 63, FRACTION [15:0] with hidden leading 1. EXP=0 flushes to zero; EXP=all ones is NaR. Round nearest even; finite overflow saturates.
  ,input wire [47:0] input_tdata
  // input: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,input wire [5:0] input_tkeep
  // input: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,input wire [0:0] input_tlast
  // input: TUSER [7:0], application metadata carried with the same beat.
  ,input wire [7:0] input_tuser
  // input: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,input wire [0:0] input_tvalid
  // input: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,output wire [0:0] input_tready
  // Custom float: SIGN [23], EXP [22:16] bias 63, FRACTION [15:0] with hidden leading 1. EXP=0 flushes to zero; EXP=all ones is NaR. Round nearest even; finite overflow saturates.
  ,output wire [23:0] output_tdata
  // output: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [2:0] output_tkeep
  // output: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] output_tlast
  // power: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] output_tuser
  // output: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] output_tvalid
  // output: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] output_tready
);
wire core_input_tready;
assign input_tready = aresetn & core_input_tready;
wire core_output_tvalid;
assign output_tvalid = aresetn & core_output_tvalid;
h_FsComplexPower24 core(.clk(aclk), .rst(~aresetn),
  .s_input_tdata(input_tdata),
  .s_input_tkeep(input_tkeep),
  .s_input_tlast(input_tlast),
  .s_input_tuser(input_tuser),
  .s_input_tvalid((input_tvalid & aresetn)),
  .s_input_tready(core_input_tready),
  .s_output_tdata(output_tdata),
  .s_output_tkeep(output_tkeep),
  .s_output_tlast(output_tlast),
  .s_output_tuser(output_tuser),
  .s_output_tvalid(core_output_tvalid),
  .s_output_tready((output_tready & aresetn)));
endmodule
