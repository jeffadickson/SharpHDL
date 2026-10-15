`timescale 1ns/1ps
// Clock-counted TinyRisc workload matched to the .NET benchmark:
// 100 warmup edges, then 10,000 edges with reset on each 128th timed edge.
module tb_compiled_benchmark;
  reg clk=0, rst=0;
  wire halted, fault;
  wire [15:0] pc;
  integer edge_num;
  h_BenchmarkTinyRisc dut(
    .clk(clk),.rst(rst),.s_run(1'b1),.s_step(1'b0),
    .s_switches(16'b0),.s_rx_valid(1'b0),.s_rx_data(8'b0),
    .s_tx_ready(1'b1),.s_tx_valid(),.s_tx_data(),
    .s_rx_ready(),.s_pc(pc),.s_ir(),.s_phase(),
    .s_halted(halted),.s_fault(fault),.s_retired(),
    .s_last_pc(),.s_leds(),.s_r0(),.s_r1(),.s_r2(),
    .s_r3(),.s_r4(),.s_r5(),.s_r6(),.s_r7()
  );
  task automatic tick(input bit reset_value);
    begin
      rst=reset_value;
      #5 clk=1;
      #5 clk=0;
    end
  endtask
  initial begin
    tick(1);
    for(edge_num=0;edge_num<100;edge_num=edge_num+1) tick(0);
    for(edge_num=0;edge_num<10000;edge_num=edge_num+1)
      tick(edge_num%128==0);
    if(fault) $fatal(1,"TinyRisc benchmark fault, pc=%h",pc);
    $display("PASS VERILATOR TinyRisc cycle benchmark: 10000 timed edges, PC=%h, halted=%d",pc,halted);
    $finish;
  end
endmodule
