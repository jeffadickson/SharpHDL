`timescale 1ns/1ps
module tb_tinyrisc_uart;
    localparam integer DIV=16;
    reg clk=0, rst=1;
    wire uart_tx, halted, fault;
    wire [15:0] pc;
    reg [7:0] expected [0:14];
    integer i,j;
    reg [7:0] decoded;
    tinyrisc_hello_top #(.CLKS_PER_BIT(DIV)) dut(
        .clk(clk),.rst(rst),.uart_tx(uart_tx),
        .halted(halted),.fault(fault),.pc(pc)
    );
    always #5 clk=~clk;
    initial begin
        expected[0]="H";expected[1]="e";expected[2]="l";
        expected[3]="l";expected[4]="o";expected[5]=",";
        expected[6]=" ";expected[7]="w";expected[8]="o";
        expected[9]="r";expected[10]="l";expected[11]="d";
        expected[12]="!";expected[13]=8'd13;expected[14]=8'd10;
        repeat(4) @(negedge clk);
        rst=0;
        for(i=0;i<15;i=i+1) begin
            @(negedge uart_tx); // start bit
            #(DIV*5);
            if(uart_tx!==1'b0) $fatal(1,"start bit byte %0d",i);
            for(j=0;j<8;j=j+1) begin
                #(DIV*10);
                decoded[j]=uart_tx;
            end
            #(DIV*10);
            if(uart_tx!==1'b1) $fatal(1,"stop bit byte %0d",i);
            if(decoded!==expected[i])
                $fatal(1,"UART byte %0d expected %02x got %02x",i,expected[i],decoded);
        end
        wait(halted);
        repeat(3) @(posedge clk);
        if(fault || uart_tx!==1'b1) $fatal(1,"unexpected fault or non-idle TX");
        $display("PASS serial 8N1 UART: Hello, world! CR LF; HALT, no fault");
        $finish;
    end
    initial begin
        #2000000;
        $fatal(1,"UART Hello World timeout pc=%h",pc);
    end
endmodule
