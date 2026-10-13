`timescale 1ns/1ps
module tb;
reg aclk=0,aresetn=0;
reg [31:0] s_axis_tdata=32'h12345678;
reg [3:0] s_axis_tkeep=15;
reg s_axis_tlast=1,s_axis_tvalid=0;
reg [7:0] s_axis_tuser=42;
wire s_axis_tready;
wire [31:0] m_axis_tdata;
wire [3:0] m_axis_tkeep;
wire m_axis_tlast,m_axis_tvalid;
wire [7:0] m_axis_tuser;
reg m_axis_tready=0;
h_Fifo1_axi dut(.*);
task tick;begin #1;aclk=1;#1;aclk=0;end endtask
initial begin
#1;
if(m_axis_tvalid!==0 || s_axis_tready!==0)$fatal(1,"Handshake not masked during reset");
tick();aresetn=1;s_axis_tvalid=1;#1;
if(s_axis_tready!==1)$fatal(1,"Empty queue not ready");
tick();s_axis_tvalid=0;#1;
if(m_axis_tvalid!==1 || m_axis_tdata!==32'h12345678 || m_axis_tuser!==42)$fatal(1,"Wrapper payload mapping");
tick();tick();
if(m_axis_tvalid!==1 || m_axis_tdata!==32'h12345678)$fatal(1,"Wrapper lost stalled token");
aresetn=0;#1;
if(m_axis_tvalid!==0 || s_axis_tready!==0)$fatal(1,"Reset must immediately mask valid/ready");
tick();aresetn=1;#1;
if(m_axis_tvalid!==0)$fatal(1,"Reset failed to flush queue");
$display("PASS HDL AXI reset/payload/stall adapter");$finish;
end
endmodule
