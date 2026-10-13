// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_Arbiter_axi(
  input wire aclk,
  input wire aresetn
  // a_axis: TDATA [31:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,input wire [31:0] a_axis_tdata
  // a_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,input wire [3:0] a_axis_tkeep
  // a_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,input wire [0:0] a_axis_tlast
  // a_axis: TUSER [7:0], application metadata carried with the same beat.
  ,input wire [7:0] a_axis_tuser
  // a_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,input wire [0:0] a_axis_tvalid
  // a_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,output wire [0:0] a_axis_tready
  // b_axis: TDATA [31:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,input wire [31:0] b_axis_tdata
  // b_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,input wire [3:0] b_axis_tkeep
  // b_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,input wire [0:0] b_axis_tlast
  // b_axis: TUSER [7:0], application metadata carried with the same beat.
  ,input wire [7:0] b_axis_tuser
  // b_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,input wire [0:0] b_axis_tvalid
  // b_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,output wire [0:0] b_axis_tready
  // arb: TDATA [31:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,output wire [31:0] m_axis_tdata
  // m_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [3:0] m_axis_tkeep
  // m_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] m_axis_tlast
  // arb: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] m_axis_tuser
  // m_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] m_axis_tvalid
  // m_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] m_axis_tready
);
wire core_a_axis_tready;
assign a_axis_tready = aresetn & core_a_axis_tready;
wire core_b_axis_tready;
assign b_axis_tready = aresetn & core_b_axis_tready;
wire core_m_axis_tvalid;
assign m_axis_tvalid = aresetn & core_m_axis_tvalid;
h_Arbiter core(.clk(aclk), .rst(~aresetn),
  .s_a_axis_tdata(a_axis_tdata),
  .s_a_axis_tkeep(a_axis_tkeep),
  .s_a_axis_tlast(a_axis_tlast),
  .s_a_axis_tuser(a_axis_tuser),
  .s_a_axis_tvalid((a_axis_tvalid & aresetn)),
  .s_a_axis_tready(core_a_axis_tready),
  .s_b_axis_tdata(b_axis_tdata),
  .s_b_axis_tkeep(b_axis_tkeep),
  .s_b_axis_tlast(b_axis_tlast),
  .s_b_axis_tuser(b_axis_tuser),
  .s_b_axis_tvalid((b_axis_tvalid & aresetn)),
  .s_b_axis_tready(core_b_axis_tready),
  .s_m_axis_tdata(m_axis_tdata),
  .s_m_axis_tkeep(m_axis_tkeep),
  .s_m_axis_tlast(m_axis_tlast),
  .s_m_axis_tuser(m_axis_tuser),
  .s_m_axis_tvalid(core_m_axis_tvalid),
  .s_m_axis_tready((m_axis_tready & aresetn)));
endmodule
