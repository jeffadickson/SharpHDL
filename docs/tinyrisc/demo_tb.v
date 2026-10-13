`timescale 1ns/1ps
module tb;
reg clk=0,rst=1;
reg s_run=1,s_step=0;
reg [15:0] s_switches=42;
reg s_rx_valid=0,s_tx_ready=1;
reg [7:0] s_rx_data=0;
wire s_tx_valid,s_rx_ready,s_halted,s_fault,s_retired;
wire [7:0] s_tx_data;
wire [15:0] s_pc,s_ir,s_last_pc,s_leds;
wire [1:0] s_phase;
wire [15:0] s_r0,s_r1,s_r2,s_r3,s_r4,s_r5,s_r6,s_r7;
h_CountRun dut(.*);
always #5 clk=~clk;
initial begin
  $dumpfile("dump.vcd");
  $dumpvars(1,tb);
  #12 rst=0;
  wait(s_halted); #1;
  if(s_fault!==0 || s_leds!==15 || s_r3!==15)$fatal(1,"TinyRisc result mismatch");
  $display("PASS TinyRisc demo: sum=15 RAM=15 LEDs=15");
  #9 $finish;
end
initial begin #2000; $fatal(1,"TinyRisc demo timeout"); end
endmodule
