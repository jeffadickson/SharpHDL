// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_DebugUartController_axi(
  input wire aclk,
  input wire aresetn
  // rx: TDATA [7:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,input wire [7:0] \rx_tdata
  // rx: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,input wire [0:0] \rx_tkeep
  // rx: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,input wire [0:0] \rx_tlast
  // rx: TUSER [7:0], application metadata carried with the same beat.
  ,input wire [7:0] \rx_tuser
  // rx: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,input wire [0:0] \rx_tvalid
  // rx: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,output wire [0:0] \rx_tready
  // tx: TDATA [7:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,output wire [7:0] \tx_tdata
  // tx: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [0:0] \tx_tkeep
  // tx: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] \tx_tlast
  // tx: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] \tx_tuser
  // tx: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] \tx_tvalid
  // tx: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] \tx_tready
  // 8-bit port, bit 0 least significant.
  ,output wire [7:0] \control_out
  // 32-bit port, bit 0 least significant.
  ,output wire [31:0] \counter_out
);
wire core_rx_tready;
assign \rx_tready  = aresetn & core_rx_tready;
wire core_tx_tvalid;
assign \tx_tvalid  = aresetn & core_tx_tvalid;
h_DebugUartController core(.clk(aclk), .rst(~aresetn),
  .s_rx_tdata(\rx_tdata ),
  .s_rx_tkeep(\rx_tkeep ),
  .s_rx_tlast(\rx_tlast ),
  .s_rx_tuser(\rx_tuser ),
  .s_rx_tvalid((\rx_tvalid  & aresetn)),
  .s_rx_tready(core_rx_tready),
  .s_tx_tdata(\tx_tdata ),
  .s_tx_tkeep(\tx_tkeep ),
  .s_tx_tlast(\tx_tlast ),
  .s_tx_tuser(\tx_tuser ),
  .s_tx_tvalid(core_tx_tvalid),
  .s_tx_tready((\tx_tready  & aresetn)),
  .s_control_out(\control_out ),
  .s_counter_out(\counter_out ));
endmodule
