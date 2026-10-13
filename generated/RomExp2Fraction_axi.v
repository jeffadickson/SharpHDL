// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_RomExp2Fraction_axi(
  input wire aclk,
  input wire aresetn
  // s_axis: TDATA [15:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,input wire [15:0] \s_axis_tdata
  // s_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,input wire [1:0] \s_axis_tkeep
  // s_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,input wire [0:0] \s_axis_tlast
  // s_axis: TUSER [7:0], application metadata carried with the same beat.
  ,input wire [7:0] \s_axis_tuser
  // s_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,input wire [0:0] \s_axis_tvalid
  // s_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,output wire [0:0] \s_axis_tready
  // math_interpolate: registered 24-bit transformed payload; holds during output stalls.
  ,output wire [23:0] \m_axis_tdata
  // m_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [2:0] \m_axis_tkeep
  // m_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] \m_axis_tlast
  // math_interpolate: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] \m_axis_tuser
  // m_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] \m_axis_tvalid
  // m_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] \m_axis_tready
);
wire core_s_axis_tready;
assign \s_axis_tready  = aresetn & core_s_axis_tready;
wire core_m_axis_tvalid;
assign \m_axis_tvalid  = aresetn & core_m_axis_tvalid;
h_RomExp2Fraction core(.clk(aclk), .rst(~aresetn),
  .s_s_axis_tdata(\s_axis_tdata ),
  .s_s_axis_tkeep(\s_axis_tkeep ),
  .s_s_axis_tlast(\s_axis_tlast ),
  .s_s_axis_tuser(\s_axis_tuser ),
  .s_s_axis_tvalid((\s_axis_tvalid  & aresetn)),
  .s_s_axis_tready(core_s_axis_tready),
  .s_m_axis_tdata(\m_axis_tdata ),
  .s_m_axis_tkeep(\m_axis_tkeep ),
  .s_m_axis_tlast(\m_axis_tlast ),
  .s_m_axis_tuser(\m_axis_tuser ),
  .s_m_axis_tvalid(core_m_axis_tvalid),
  .s_m_axis_tready((\m_axis_tready  & aresetn)));
endmodule
