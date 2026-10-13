`timescale 1ns/1ps
module tb;
 reg clk=0;always #20 clk=~clk;
 reg [3:0] buttons=0;
 wire [3:0] leds;wire [6:0] left,right;
 classroom_top #(.DB_CYCLES(4)) dut(.i_Clk(clk),
 .i_Switch_1(buttons[0]),.i_Switch_2(buttons[1]),.i_Switch_3(buttons[2]),.i_Switch_4(buttons[3]),
 .o_LED_1(leds[0]),.o_LED_2(leds[1]),.o_LED_3(leds[2]),.o_LED_4(leds[3]),
 .o_Segment1_A(left[6]),.o_Segment1_B(left[5]),.o_Segment1_C(left[4]),.o_Segment1_D(left[3]),.o_Segment1_E(left[2]),.o_Segment1_F(left[1]),.o_Segment1_G(left[0]),
 .o_Segment2_A(right[6]),.o_Segment2_B(right[5]),.o_Segment2_C(right[4]),.o_Segment2_D(right[3]),.o_Segment2_E(right[2]),.o_Segment2_F(right[1]),.o_Segment2_G(right[0]));
 task clocks;input integer n;begin repeat(n) @(negedge clk);end endtask
 task press;input [3:0] mask;begin buttons=mask;clocks(12);buttons=0;clocks(12);end endtask
 initial begin
   clocks(270);
   if(left!==~7'b1111110 || right!==~7'b1101101 || leds!==0) $fatal(1,"Startup 0+2 display");
   // A one-clock bounce does not become a press after synchronization/filtering.
   buttons=2;clocks(1);buttons=0;clocks(12);
   if(leds!==0)$fatal(1,"Debouncer accepted a short glitch");
   press(2);
   if(leds!==1 || left!==~7'b0110000 || right!==~7'b1111001)$fatal(1,"1+2 display");
   // Holding a button must increment once, not once each FPGA clock.
   buttons=2;clocks(30);buttons=0;clocks(12);
   if(leds!==2)$fatal(1,"Button edge count");
   press(1);if(leds!==0)$fatal(1,"Clear");
   press(8);if(right!==~7'b1111110)$fatal(1,"Counter mode starts at zero");
   $display("PASS Go Board shell startup synchronizers debounce edge input segment outputs");$finish;
 end
endmodule
