// Port-name shim: use the exact existing FsFilterBank4 reference testbench.
// The hand-written implementation itself does not depend on SharpHDL.
module h_FsFilterBank4 (
    input wire clk,input wire rst,
    input wire [15:0] s_s_axis_tdata,input wire [1:0] s_s_axis_tkeep,
    input wire s_s_axis_tlast,input wire [7:0] s_s_axis_tuser,
    input wire s_s_axis_tvalid,output wire s_s_axis_tready,
    output wire [31:0] s_m_axis_tdata,output wire [3:0] s_m_axis_tkeep,
    output wire s_m_axis_tlast,output wire [15:0] s_m_axis_tuser,
    output wire s_m_axis_tvalid,input wire s_m_axis_tready
);
filter_bank_handwritten dut (
    .clk(clk),.rst(rst),
    .s_axis_tdata(s_s_axis_tdata),.s_axis_tkeep(s_s_axis_tkeep),.s_axis_tlast(s_s_axis_tlast),
    .s_axis_tuser(s_s_axis_tuser),.s_axis_tvalid(s_s_axis_tvalid),.s_axis_tready(s_s_axis_tready),
    .m_axis_tdata(s_m_axis_tdata),.m_axis_tkeep(s_m_axis_tkeep),.m_axis_tlast(s_m_axis_tlast),
    .m_axis_tuser(s_m_axis_tuser),.m_axis_tvalid(s_m_axis_tvalid),.m_axis_tready(s_m_axis_tready));
endmodule
