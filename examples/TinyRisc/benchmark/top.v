// Core characterization wrapper, not a board pinout or physical UART.
// Multiplex debug outputs to retain every architectural register without
// requiring hundreds of package pins. inspect 0..10: PC, IR, last-PC, R0..R7;
// inspect 11: bits [4:3] phase, [2] retired, [1] fault, [0] halted.
module TinyRiscBench(
 input clk,rst,s_run,s_step,
 input [15:0] s_switches,
 input s_rx_valid,input [7:0] s_rx_data,input s_tx_ready,
 input [3:0] inspect,
 output s_tx_valid,output [7:0] s_tx_data,output s_rx_ready,
 output [15:0] s_leds,output reg [15:0] debug
);
 wire [15:0] s_pc,s_ir,s_last_pc,s_r0,s_r1,s_r2,s_r3,s_r4,s_r5,s_r6,s_r7;
 wire [1:0] s_phase;
 wire s_halted,s_fault,s_retired;
 h_CountRun cpu(.*);
 always @* begin
  case(inspect)
   0:debug=s_pc;1:debug=s_ir;2:debug=s_last_pc;
   3:debug=s_r0;4:debug=s_r1;5:debug=s_r2;6:debug=s_r3;
   7:debug=s_r4;8:debug=s_r5;9:debug=s_r6;10:debug=s_r7;
   11:debug={11'b0,s_phase,s_retired,s_fault,s_halted};
   default:debug=0;
  endcase
 end
endmodule
