module tb;reg clk=0,rst=1,start=0;reg [31:0] arg1=3,arg2=0;wire done,fault;wire [31:0] result;integer cycles;h_ArrayXilinx7Series dut(.clk(clk),.rst(rst),.s_start(start),.s_arg1(arg1),.s_arg2(arg2),.s_done(done),.s_fault(fault),.s_result(result));initial begin #1;clk=1;#1;clk=0;rst=0;
arg2=7;start=1;#1;clk=1;#1;clk=0;start=0;cycles=0;while(!done&&cycles<200)begin #1;clk=1;#1;clk=0;cycles=cycles+1;end if(!done||fault||result!==32'd7)$fatal(1,"Sequential native memory mismatch");
arg2=4;start=1;#1;clk=1;#1;clk=0;start=0;cycles=0;while(!done&&cycles<200)begin #1;clk=1;#1;clk=0;cycles=cycles+1;end if(!done||fault||result!==32'd11)$fatal(1,"Sequential native memory mismatch");
rst=1;#1;clk=1;#1;clk=0;rst=0;
arg2=1;start=1;#1;clk=1;#1;clk=0;start=0;cycles=0;while(!done&&cycles<200)begin #1;clk=1;#1;clk=0;cycles=cycles+1;end if(!done||fault||result!==32'd12)$fatal(1,"Sequential native memory mismatch");
$display("PASS sequential memory Xilinx7Series");$finish;end endmodule
