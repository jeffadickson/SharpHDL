// Original teacher-maintained board shell. Learners edit StudentLogic.cs.
// Go Board: iCE40HX1K-VQ100, 25 MHz oscillator, active-high buttons/LEDs,
// active-low segment pins. Pin facts: Nandland Go_Board_Constraints.pcf.
module classroom_top #(
    parameter integer DB_CYCLES=250000 // 10 ms stable interval at 25 MHz
)(
    input i_Clk,
    input i_Switch_1, i_Switch_2, i_Switch_3, i_Switch_4,
    output o_LED_1, o_LED_2, o_LED_3, o_LED_4,
    output o_Segment1_A, o_Segment1_B, o_Segment1_C, o_Segment1_D,
           o_Segment1_E, o_Segment1_F, o_Segment1_G,
    output o_Segment2_A, o_Segment2_B, o_Segment2_C, o_Segment2_D,
           o_Segment2_E, o_Segment2_F, o_Segment2_G
);
    // iCE40 FPGA configuration initializes this counter to zero. Hold the
    // generated synchronous-reset core in reset through the first 255 clocks.
    reg [7:0] startup=0;
    wire reset=~(&startup);
    always @(posedge i_Clk) if(reset) startup<=startup+1'b1;
    // Two-register synchronizers reduce metastability propagation. A debounce
    // filter then requires DB_CYCLES equal samples before changing its output.
    (* async_reg="true" *) reg [3:0] sync1=0,sync2=0;
    always @(posedge i_Clk) begin
        sync1<={i_Switch_4,i_Switch_3,i_Switch_2,i_Switch_1};
        sync2<=sync1;
    end
    localparam integer CW=DB_CYCLES<2?1:$clog2(DB_CYCLES);
    reg [3:0] stable=0;
    genvar k;
    generate for(k=0;k<4;k=k+1) begin: debounce
        reg [CW-1:0] age=0;
        always @(posedge i_Clk) begin
            if(reset) begin age<=0;stable[k]<=0;end
            else if(sync2[k]==stable[k]) age<=0;
            else if(age==DB_CYCLES-1) begin stable[k]<=sync2[k];age<=0;end
            else age<=age+1'b1;
        end
    end endgenerate
    // Digit buses carry one hexadecimal nibble each. LED bit0 is least significant.
    wire [3:0] left_digit,right_digit,leds;
    h_ClassroomCore core(.clk(i_Clk),.rst(reset),.s_buttons(stable),
        .s_left_digit(left_digit),.s_right_digit(right_digit),.s_leds(leds));
    assign {o_LED_4,o_LED_3,o_LED_2,o_LED_1}=leds;
    // A..G active-high font; invert once at the physical active-low outputs.
    function [6:0] font;
        input [3:0] digit;
        begin case(digit)
          0:font=7'b1111110; 1:font=7'b0110000; 2:font=7'b1101101;
          3:font=7'b1111001; 4:font=7'b0110011; 5:font=7'b1011011;
          6:font=7'b1011111; 7:font=7'b1110000; 8:font=7'b1111111;
          9:font=7'b1111011; 10:font=7'b1110111; 11:font=7'b0011111;
          12:font=7'b1001110; 13:font=7'b0111101; 14:font=7'b1001111;
          15:font=7'b1000111; default:font=0;
        endcase end
    endfunction
    assign {o_Segment1_A,o_Segment1_B,o_Segment1_C,o_Segment1_D,o_Segment1_E,o_Segment1_F,o_Segment1_G}=~font(left_digit);
    assign {o_Segment2_A,o_Segment2_B,o_Segment2_C,o_Segment2_D,o_Segment2_E,o_Segment2_F,o_Segment2_G}=~font(right_digit);
endmodule
