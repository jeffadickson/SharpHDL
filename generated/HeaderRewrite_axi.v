// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_HeaderRewrite_axi(
  input wire aclk,
  input wire aresetn
  // 64-bit port, bit 0 least significant.
  ,input wire [63:0] \word
  // 64-bit port, bit 0 least significant.
  ,output wire [63:0] \word_out
);
h_HeaderRewrite core(.clk(aclk), .rst(~aresetn),
  .s_word(\word ),
  .s_word_out(\word_out ));
endmodule
