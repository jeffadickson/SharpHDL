`timescale 1ns/1ps
// Compare the routed-wrapper input netlist with the independently regression-
// tested source CPU. Inspect every register on every cycle, not only LEDs.
module tb;
 reg clk=0,rst=1,s_run=0,s_step=0,s_rx_valid=0,s_tx_ready=0;
 reg [15:0] s_switches=16'h1234;
 reg [7:0] s_rx_data=8'h5a;
 reg [3:0] inspect=0;
 wire [15:0] s_pc,s_ir,s_last_pc,s_leds,s_r0,s_r1,s_r2,s_r3,s_r4,s_r5,s_r6,s_r7;
 wire [1:0] s_phase;
 wire s_halted,s_fault,s_retired,s_tx_valid,s_rx_ready;
 wire [7:0] s_tx_data;
 wire [15:0] got_leds,debug;
 wire got_tx_valid,got_rx_ready;
 wire [7:0] got_tx_data;
 reg [15:0] expected;
 integer cycle,sel;
 h_CountRun reference_cpu(.*);
 TinyRiscBench dut(.clk(clk),.rst(rst),.s_run(s_run),.s_step(s_step),.s_switches(s_switches),
 .s_rx_valid(s_rx_valid),.s_rx_data(s_rx_data),.s_tx_ready(s_tx_ready),.inspect(inspect),
 .s_tx_valid(got_tx_valid),.s_tx_data(got_tx_data),.s_rx_ready(got_rx_ready),.s_leds(got_leds),.debug(debug));
 initial begin
  for(cycle=0;cycle<1000;cycle=cycle+1) begin
   rst=(cycle==0 || cycle==300);
   s_run=cycle<100 || cycle>500;
   s_step=cycle%7==0;s_tx_ready=cycle%3==0;s_rx_valid=cycle%5==0;
   #5;clk=1;#5;clk=0;
   for(sel=0;sel<16;sel=sel+1) begin
    inspect=sel;#1;
    case(sel)
     0:expected=s_pc;1:expected=s_ir;2:expected=s_last_pc;
     3:expected=s_r0;4:expected=s_r1;5:expected=s_r2;6:expected=s_r3;
     7:expected=s_r4;8:expected=s_r5;9:expected=s_r6;10:expected=s_r7;
     11:expected={11'b0,s_phase,s_retired,s_fault,s_halted};default:expected=0;
    endcase
    if(debug!==expected || {got_leds,got_tx_valid,got_tx_data,got_rx_ready}!=={s_leds,s_tx_valid,s_tx_data,s_rx_ready})
      $fatal(1,"TinyRisc mapped wrapper cycle %0d selector %0d",cycle,sel);
   end
  end
  $display("PASS TinyRisc mapped wrapper: 1000 cycles, 16 debug selectors per cycle, reset/run/step/peripheral controls");$finish;
 end
endmodule
