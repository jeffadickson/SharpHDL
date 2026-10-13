// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_SignedLookup_axi(
  input wire aclk,
  input wire aresetn
  // 8-bit port, bit 0 least significant.
  ,input wire [7:0] \a
  // 8-bit port, bit 0 least significant.
  ,input wire [7:0] \b
  // 3-bit port, bit 0 least significant.
  ,input wire [2:0] \address
  // 8-bit port, bit 0 least significant.
  ,output wire [7:0] \asr
  // 16-bit port, bit 0 least significant.
  ,output wire [15:0] \sext
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \less
  // 8-bit port, bit 0 least significant.
  ,output wire [7:0] \lut
);
h_SignedLookup core(.clk(aclk), .rst(~aresetn),
  .s_a(\a ),
  .s_b(\b ),
  .s_address(\address ),
  .s_asr(\asr ),
  .s_sext(\sext ),
  .s_less(\less ),
  .s_lut(\lut ));
endmodule
