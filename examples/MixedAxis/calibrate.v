// Signed sensor calibration: y = saturate_int16(GAIN*x + OFFSET).
// TDATA is a two's-complement signed sample; TKEEP must be 2'b11 for
// scientific use. Sidebands are transported unchanged, including partial masks.
// One output slot, one cycle latency, one beat/cycle when downstream is ready.
module sensor_calibrate #(
    parameter signed [15:0] GAIN = 2,
    parameter signed [15:0] OFFSET = -10
)(
    input wire aclk, input wire aresetn,
    input wire [15:0] s_axis_tdata,
    input wire [1:0] s_axis_tkeep,
    input wire s_axis_tlast,
    input wire [7:0] s_axis_tuser,
    input wire s_axis_tvalid, output wire s_axis_tready,
    output reg [15:0] m_axis_tdata,
    output reg [1:0] m_axis_tkeep,
    output reg m_axis_tlast,
    output reg [7:0] m_axis_tuser,
    output reg m_axis_tvalid, input wire m_axis_tready
);
wire available = !m_axis_tvalid || m_axis_tready;
assign s_axis_tready = aresetn && available;
wire signed [31:0] product = $signed(s_axis_tdata) * GAIN;
wire signed [32:0] calibrated = {product[31],product} + {{17{OFFSET[15]}},OFFSET};
wire [15:0] limited = calibrated > 33'sd32767 ? 16'h7fff :
                      calibrated < -33'sd32768 ? 16'h8000 : calibrated[15:0];
always @(posedge aclk) begin
    if (!aresetn) begin
        m_axis_tvalid <= 0;
        m_axis_tdata <= 0; m_axis_tkeep <= 0;
        m_axis_tlast <= 0; m_axis_tuser <= 0;
    end else if (available) begin
        m_axis_tvalid <= s_axis_tvalid;
        if (s_axis_tvalid) begin
            m_axis_tdata <= limited; m_axis_tkeep <= s_axis_tkeep;
            m_axis_tlast <= s_axis_tlast; m_axis_tuser <= s_axis_tuser;
        end
    end
end
endmodule
