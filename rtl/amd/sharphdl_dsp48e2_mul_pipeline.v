// Direct AMD DSP48E2 three-register-stage streaming multiplier.
// Fixed latency 3 rising edges, II=1. No ready/backpressure input.
// Valid is pipelined alongside AREG/BREG, MREG and PREG. To use this in an
// elastic stream, insert a correctly sized skid/FIFO boundary downstream.
// Requires AMD UNISIM for device simulation and implementation.
module sharphdl_dsp48e2_mul_pipeline #(
    parameter integer A_WIDTH=16,
    parameter integer B_WIDTH=16
)(
    input wire clk,
    input wire rst,
    input wire input_valid,
    input wire [A_WIDTH-1:0] a,
    input wire [B_WIDTH-1:0] b,
    output wire output_valid,
    output wire [A_WIDTH+B_WIDTH-1:0] product
);
    initial if(A_WIDTH<1||A_WIDTH>26||B_WIDTH<1||B_WIDTH>17)
        $error("DSP48E2 unsigned pipeline supports A 1..26, B 1..17");
    reg [2:0] valid_pipe=3'b0;
    always @(posedge clk)
        if(rst)valid_pipe<=3'b0;
        else valid_pipe<={valid_pipe[1:0],input_valid};
    assign output_valid=valid_pipe[2];
`ifdef SHARPH_DL_DSP_BEHAVIORAL
    reg [A_WIDTH+B_WIDTH-1:0] p0=0,p1=0,p2=0;
    always @(posedge clk)
        if(rst)begin p0<=0;p1<=0;p2<=0;end
        else begin p0<=a*b;p1<=p0;p2<=p1;end
    assign product=p2;
`else
    wire [47:0] p48;
    wire [29:0] dsp_a={3'b111,{(27-A_WIDTH){1'b0}},a};
    wire [17:0] dsp_b={{(18-B_WIDTH){1'b0}},b};
    DSP48E2 #(
        .A_INPUT("DIRECT"), .B_INPUT("DIRECT"),
        .AMULTSEL("A"), .BMULTSEL("B"),
        .USE_MULT("MULTIPLY"), .USE_SIMD("ONE48"),
        .AREG(1), .ACASCREG(1), .BREG(1), .BCASCREG(1),
        .MREG(1), .PREG(1), .ADREG(0), .CREG(0), .DREG(0),
        .ALUMODEREG(0), .CARRYINREG(0), .CARRYINSELREG(0),
        .INMODEREG(0), .OPMODEREG(0)
    ) dsp48e2_inst (
        .CLK(clk), .A(dsp_a), .B(dsp_b), .P(p48),
        .C(48'b0), .D(27'b0), .ACIN(30'b0), .BCIN(18'b0),
        .PCIN(48'b0), .CARRYCASCIN(1'b0), .MULTSIGNIN(1'b0),
        .ALUMODE(4'b0), .CARRYIN(1'b0), .CARRYINSEL(3'b0),
        .INMODE(5'b0), .OPMODE(9'b000000101),
        .CEA1(1'b1), .CEA2(1'b1), .CEAD(1'b1), .CEALUMODE(1'b1),
        .CEB1(1'b1), .CEB2(1'b1), .CEC(1'b1), .CECARRYIN(1'b1),
        .CECTRL(1'b1), .CED(1'b1), .CEINMODE(1'b1),
        .CEM(1'b1), .CEP(1'b1),
        .RSTA(rst), .RSTALLCARRYIN(rst), .RSTALUMODE(rst),
        .RSTB(rst), .RSTC(rst), .RSTCTRL(rst), .RSTD(rst),
        .RSTINMODE(rst), .RSTM(rst), .RSTP(rst)
    );
    assign product=p48[A_WIDTH+B_WIDTH-1:0];
`endif
endmodule
