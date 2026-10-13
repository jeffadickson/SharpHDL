// AXI4-Stream subset: TDATA/TKEEP/TLAST/TUSER/TVALID/TREADY.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_FsOscillator_axi(
  input wire aclk,
  input wire aresetn
  // 24-bit port, bit 0 least significant.
  ,input wire [23:0] phase_step
  // osc_result: TDATA [63:0]; byte lane i = bits [8*i+7:8*i], lane 0 least significant. Payload format is application-defined.
  ,output wire [63:0] m_axis_tdata
  // m_axis: TKEEP bit i qualifies TDATA byte lane i; transported with each beat.
  ,output wire [7:0] m_axis_tkeep
  // m_axis: TLAST marks the final beat of a packet; meaningful with TVALID.
  ,output wire [0:0] m_axis_tlast
  // osc_result: TUSER [7:0], application metadata carried with the same beat.
  ,output wire [7:0] m_axis_tuser
  // m_axis: TVALID; producer offers a beat. Must remain asserted during a stall.
  ,output wire [0:0] m_axis_tvalid
  // m_axis: TREADY; consumer accepts on rising clk when TVALID is also high. Flows opposite payload.
  ,input wire [0:0] m_axis_tready
);
wire core_m_axis_tvalid;
assign m_axis_tvalid = aresetn & core_m_axis_tvalid;
h_FsOscillator core(.clk(aclk), .rst(~aresetn),
  .s_phase_step(phase_step),
  .s_m_axis_tdata(m_axis_tdata),
  .s_m_axis_tkeep(m_axis_tkeep),
  .s_m_axis_tlast(m_axis_tlast),
  .s_m_axis_tuser(m_axis_tuser),
  .s_m_axis_tvalid(core_m_axis_tvalid),
  .s_m_axis_tready((m_axis_tready & aresetn)));
endmodule
