// AXI4-Lite 32-bit slave and optional AXI4-Stream ports; shared clock and reset.
// aclk: rising-edge clock. aresetn: active-low; assert through a clock edge to reset the synchronous core.
// Reset masks handshakes immediately; no reset synchronizer is included.
module h_AxiRegisters_axi(
  input wire aclk,
  input wire aresetn
  // AXI4-Lite byte write address; captured independently of WDATA. 32-bit register accesses require 4-byte alignment.
  ,input wire [7:0] \s_axi_awaddr
  // AWPROT accepted but ignored; no privilege/security filtering.
  ,input wire [2:0] \s_axi_awprot
  // 1-bit port, bit 0 least significant.
  ,input wire [0:0] \s_axi_awvalid
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \s_axi_awready
  // AXI4-Lite 32-bit write word; byte lane i is bits [8*i+7:8*i].
  ,input wire [31:0] \s_axi_wdata
  // WSTRB bit i updates byte lane i; zero strobes are a no-op on writable registers.
  ,input wire [3:0] \s_axi_wstrb
  // 1-bit port, bit 0 least significant.
  ,input wire [0:0] \s_axi_wvalid
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \s_axi_wready
  // BRESP: 00 OKAY, 10 SLVERR for unaligned/unmapped/read-only writes; stable until BREADY.
  ,output wire [1:0] \s_axi_bresp
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \s_axi_bvalid
  // 1-bit port, bit 0 least significant.
  ,input wire [0:0] \s_axi_bready
  // AXI4-Lite byte read address; read data snapshots on AR handshake.
  ,input wire [7:0] \s_axi_araddr
  // ARPROT accepted but ignored; no privilege/security filtering.
  ,input wire [2:0] \s_axi_arprot
  // 1-bit port, bit 0 least significant.
  ,input wire [0:0] \s_axi_arvalid
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \s_axi_arready
  // AXI4-Lite 32-bit read snapshot; stable while RVALID and not RREADY.
  ,output wire [31:0] \s_axi_rdata
  // RRESP: 00 OKAY, 10 SLVERR for unaligned/unmapped reads (data zero).
  ,output wire [1:0] \s_axi_rresp
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \s_axi_rvalid
  // 1-bit port, bit 0 least significant.
  ,input wire [0:0] \s_axi_rready
  // 32-bit port, bit 0 least significant.
  ,input wire [31:0] \status
  // 32-bit port, bit 0 least significant.
  ,output wire [31:0] \register_a
  // 32-bit port, bit 0 least significant.
  ,output wire [31:0] \register_b
  // 1-bit port, bit 0 least significant.
  ,output wire [0:0] \write_a
);
wire core_s_axi_awready;
assign \s_axi_awready  = aresetn & core_s_axi_awready;
wire core_s_axi_wready;
assign \s_axi_wready  = aresetn & core_s_axi_wready;
wire core_s_axi_bvalid;
assign \s_axi_bvalid  = aresetn & core_s_axi_bvalid;
wire core_s_axi_arready;
assign \s_axi_arready  = aresetn & core_s_axi_arready;
wire core_s_axi_rvalid;
assign \s_axi_rvalid  = aresetn & core_s_axi_rvalid;
h_AxiRegisters core(.clk(aclk), .rst(~aresetn),
  .s_s_axi_awaddr(\s_axi_awaddr ),
  .s_s_axi_awprot(\s_axi_awprot ),
  .s_s_axi_awvalid((\s_axi_awvalid  & aresetn)),
  .s_s_axi_awready(core_s_axi_awready),
  .s_s_axi_wdata(\s_axi_wdata ),
  .s_s_axi_wstrb(\s_axi_wstrb ),
  .s_s_axi_wvalid((\s_axi_wvalid  & aresetn)),
  .s_s_axi_wready(core_s_axi_wready),
  .s_s_axi_bresp(\s_axi_bresp ),
  .s_s_axi_bvalid(core_s_axi_bvalid),
  .s_s_axi_bready((\s_axi_bready  & aresetn)),
  .s_s_axi_araddr(\s_axi_araddr ),
  .s_s_axi_arprot(\s_axi_arprot ),
  .s_s_axi_arvalid((\s_axi_arvalid  & aresetn)),
  .s_s_axi_arready(core_s_axi_arready),
  .s_s_axi_rdata(\s_axi_rdata ),
  .s_s_axi_rresp(\s_axi_rresp ),
  .s_s_axi_rvalid(core_s_axi_rvalid),
  .s_s_axi_rready((\s_axi_rready  & aresetn)),
  .s_status(\status ),
  .s_register_a(\register_a ),
  .s_register_b(\register_b ),
  .s_write_a(\write_a ));
endmodule
