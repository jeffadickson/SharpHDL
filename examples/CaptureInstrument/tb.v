`timescale 1ns/1ps
module tb;
reg aclk=0,aresetn=0;
reg [7:0] s_axi_awaddr=0,s_axi_araddr=0;
reg [2:0] s_axi_awprot=0,s_axi_arprot=0;
reg s_axi_awvalid=0,s_axi_wvalid=0,s_axi_bready=0,s_axi_arvalid=0,s_axi_rready=0;
wire s_axi_awready,s_axi_wready,s_axi_bvalid,s_axi_arready,s_axi_rvalid;
reg [31:0] s_axi_wdata=0;reg [3:0] s_axi_wstrb=0;
wire [31:0] s_axi_rdata;wire [1:0] s_axi_bresp,s_axi_rresp;
reg [15:0] s_axis_tdata=0;reg [1:0] s_axis_tkeep=0;
reg s_axis_tvalid=0,s_axis_tlast=0;reg [7:0] s_axis_tuser=0;wire s_axis_tready;
h_CaptureInstrument_axi dut(.*);
task tick;begin #5;aclk=1;#5;aclk=0;end endtask
task write_reg(input [7:0] address,input [31:0] value,input integer ad,input integer dd);
integer t;reg aw,w,af,wf,finished;
begin
 aw=0;w=0;finished=0;s_axi_bready=0;s_axi_awaddr=address;s_axi_wdata=value;s_axi_wstrb=15;
 for(t=0;t<80&&!finished;t=t+1)begin
  s_axi_awvalid=!aw&&t>=ad;s_axi_wvalid=!w&&t>=dd;#1;
  af=s_axi_awvalid&&s_axi_awready;wf=s_axi_wvalid&&s_axi_wready;
  if(s_axi_bvalid)begin
   if(!aw||!w||s_axi_bresp!==0)$fatal(1,"write response");
   // Hold a response for several clocks to check stability at the integrated boundary.
   s_axi_awvalid=0;s_axi_wvalid=0;repeat(3)begin tick;if(!s_axi_bvalid||s_axi_bresp!==0)$fatal(1,"B stall");end
   s_axi_bready=1;finished=1;
  end
  tick;aw=aw|af;w=w|wf;
 end
 s_axi_awvalid=0;s_axi_wvalid=0;s_axi_bready=0;
 if(!finished)$fatal(1,"write timeout");
end endtask
task read_reg(input [7:0] address,input [31:0] expected);
integer t;reg accepted,finished;
begin
 accepted=0;finished=0;s_axi_araddr=address;s_axi_arvalid=1;s_axi_rready=0;
 for(t=0;t<80&&!finished;t=t+1)begin
  #1;
  if(s_axi_arvalid&&s_axi_arready)begin tick;s_axi_arvalid=0;accepted=1;end
  else tick;
  if(s_axi_rvalid)begin
   if(!accepted||s_axi_rresp!==0||s_axi_rdata!==expected)$fatal(1,"read %h actual=%h expected=%h",address,s_axi_rdata,expected);
   repeat(3)begin tick;if(!s_axi_rvalid||s_axi_rdata!==expected)$fatal(1,"R stall");end
   s_axi_rready=1;tick;s_axi_rready=0;finished=1;
  end
 end
 if(!finished)$fatal(1,"read timeout");
end endtask
task sample(input [15:0] data,input [1:0] keep,input last,input [7:0] user);
begin
 s_axis_tdata=data;s_axis_tkeep=keep;s_axis_tlast=last;s_axis_tuser=user;s_axis_tvalid=1;#1;
 if(!s_axis_tready)$fatal(1,"capture unexpectedly not ready");tick;s_axis_tvalid=0;
end endtask
integer i;
initial begin
 #1;if(s_axi_awready||s_axi_wready||s_axi_arready||s_axis_tready||s_axi_bvalid||s_axi_rvalid)$fatal(1,"reset masks all handshakes");
 tick;aresetn=1;
 write_reg(0,2,0,5);write_reg(0,1,7,0); // clear then arm, test both channel arrival orders
 sample(123,3,0,7);sample(456,1,0,8);sample(789,0,1,9);
 #1;if(s_axis_tready)$fatal(1,"LAST stops capture");
 read_reg(8,3);read_reg(12,2);
 read_reg(16,(7<<19)|(3<<16)|123);read_reg(20,(8<<19)|(1<<16)|456);
 write_reg(4,1,0,0);read_reg(16,(9<<19)|(1<<18)|789);
 write_reg(0,2,0,0);write_reg(0,0,0,0);read_reg(8,0);read_reg(16,(9<<19)|(1<<18)|789); // retained RAM
 write_reg(0,1,0,0);
 for(i=0;i<32;i=i+1)sample(i,3,0,i);
 #1;if(s_axis_tready)$fatal(1,"full capture must stop");
 read_reg(8,32);read_reg(12,4);
 // Pending read response must be masked immediately and discarded at reset edge.
 s_axi_araddr=8;s_axi_arvalid=1;tick;s_axi_arvalid=0;#1;if(!s_axi_rvalid)$fatal(1,"pending read missing");
 aresetn=0;#1;if(s_axi_rvalid||s_axi_awready||s_axi_wready||s_axi_arready||s_axis_tready)$fatal(1,"reset mask");
 tick;aresetn=1;read_reg(8,0);read_reg(12,0);
 $display("PASS integrated AXI-controlled capture: ordering, response stalls, metadata, full, clear, retained RAM and reset");$finish;
end
endmodule
