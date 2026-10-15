// SharpHDL explicit AMD UltraScale / UltraScale+ DSP48E2 multiplier.
// Single physical primitive, UNSIGNED A_WIDTH <= 26, B_WIDTH <= 17.
// Fully combinational: AREG=BREG=MREG=PREG=0. Do not use as a pipelined
// macro. This is a target-specific, nonportable component.
// For portable RTL simulation define SHARPH_DL_DSP_BEHAVIORAL; for actual
// device simulation/synthesis provide the AMD UNISIM DSP48E2 primitive.
module sharphdl_dsp48e2_mul_unsigned #(
    parameter integer A_WIDTH=16,
    parameter integer B_WIDTH=16
)(
    input wire clk,
    input wire [A_WIDTH-1:0] a,
    input wire [B_WIDTH-1:0] b,
    output wire [A_WIDTH+B_WIDTH-1:0] product
);
    // Parameter errors are caught by the accompanying C# builder; these
    // Verilog guards protect users instantiating the wrapper directly.
    initial begin
        if (A_WIDTH<1 || A_WIDTH>26 || B_WIDTH<1 || B_WIDTH>17)
            $error("DSP48E2 unsigned multiplier requires A_WIDTH 1..26, B_WIDTH 1..17");
    end
`ifdef SHARPH_DL_DSP_BEHAVIORAL
    assign product=a*b;
`else
    wire [47:0] p48;
    // DSP48E2 uses signed 27x18. Zero-extend unsigned operands so both
    // internal sign bits are zero. A[29:27] are unused for multiplication.
    wire [29:0] dsp_a={3'b111,{(27-A_WIDTH){1'b0}},a};
    wire [17:0] dsp_b={{(18-B_WIDTH){1'b0}},b};
    DSP48E2 #(
        .A_INPUT("DIRECT"), .B_INPUT("DIRECT"),
        .AMULTSEL("A"), .BMULTSEL("B"),
        .USE_MULT("MULTIPLY"), .USE_SIMD("ONE48"),
        .AREG(0), .ACASCREG(0), .BREG(0), .BCASCREG(0),
        .MREG(0), .PREG(0), .ADREG(0), .CREG(0), .DREG(0),
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
        .RSTA(1'b0), .RSTALLCARRYIN(1'b0), .RSTALUMODE(1'b0),
        .RSTB(1'b0), .RSTC(1'b0), .RSTCTRL(1'b0), .RSTD(1'b0),
        .RSTINMODE(1'b0), .RSTM(1'b0), .RSTP(1'b0)
    );
    assign product=p48[A_WIDTH+B_WIDTH-1:0];
`endif
endmodule
