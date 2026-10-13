// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_PacketRouter_axi(
  input wire aclk,
  input wire aresetn
  // First beat: Packed fields, MSB first: version [63:60]; type [59:48]; length [47:32]; source [31:0]. Subsequent beats: opaque payload bytes.
  ,input wire [63:0] \s_axis_tdata
  // s_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,input wire [7:0] \s_axis_tkeep
  // s_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,input wire [0:0] \s_axis_tlast
  // s_axis: TUSER [7:0], application metadata carried with the same beat.
  ,input wire [7:0] \s_axis_tuser
  // s_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,input wire [0:0] \s_axis_tvalid
  // s_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,output wire [0:0] \s_axis_tready
  // queue_zero: TDATA [63:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,output wire [63:0] \m_axis_zero_tdata
  // m_axis_zero: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [7:0] \m_axis_zero_tkeep
  // m_axis_zero: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] \m_axis_zero_tlast
  // queue_zero: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] \m_axis_zero_tuser
  // m_axis_zero: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] \m_axis_zero_tvalid
  // m_axis_zero: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] \m_axis_zero_tready
  // queue_one: TDATA [63:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,output wire [63:0] \m_axis_one_tdata
  // m_axis_one: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [7:0] \m_axis_one_tkeep
  // m_axis_one: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] \m_axis_one_tlast
  // queue_one: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] \m_axis_one_tuser
  // m_axis_one: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] \m_axis_one_tvalid
  // m_axis_one: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] \m_axis_one_tready
);
wire core_s_axis_tready;
assign \s_axis_tready  = aresetn & core_s_axis_tready;
wire core_m_axis_zero_tvalid;
assign \m_axis_zero_tvalid  = aresetn & core_m_axis_zero_tvalid;
wire core_m_axis_one_tvalid;
assign \m_axis_one_tvalid  = aresetn & core_m_axis_one_tvalid;
h_PacketRouter core(.clk(aclk), .rst(~aresetn),
  .s_s_axis_tdata(\s_axis_tdata ),
  .s_s_axis_tkeep(\s_axis_tkeep ),
  .s_s_axis_tlast(\s_axis_tlast ),
  .s_s_axis_tuser(\s_axis_tuser ),
  .s_s_axis_tvalid((\s_axis_tvalid  & aresetn)),
  .s_s_axis_tready(core_s_axis_tready),
  .s_m_axis_zero_tdata(\m_axis_zero_tdata ),
  .s_m_axis_zero_tkeep(\m_axis_zero_tkeep ),
  .s_m_axis_zero_tlast(\m_axis_zero_tlast ),
  .s_m_axis_zero_tuser(\m_axis_zero_tuser ),
  .s_m_axis_zero_tvalid(core_m_axis_zero_tvalid),
  .s_m_axis_zero_tready((\m_axis_zero_tready  & aresetn)),
  .s_m_axis_one_tdata(\m_axis_one_tdata ),
  .s_m_axis_one_tkeep(\m_axis_one_tkeep ),
  .s_m_axis_one_tlast(\m_axis_one_tlast ),
  .s_m_axis_one_tuser(\m_axis_one_tuser ),
  .s_m_axis_one_tvalid(core_m_axis_one_tvalid),
  .s_m_axis_one_tready((\m_axis_one_tready  & aresetn)));
endmodule
