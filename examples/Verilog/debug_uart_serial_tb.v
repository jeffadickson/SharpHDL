`timescale 1ns/1ps
module tb;
localparam DIV=8;
localparam BIT_TIME=80;
reg clk=0,rst=1,uart_rx=1;
wire uart_tx;
wire [7:0] control_out;
wire [31:0] counter_out;
wire rx_overrun,framing_error;
debug_uart_serial #(.DIVISOR(DIV)) dut(.*);
always #5 clk=~clk;
reg [7:0] received[0:31];
integer count=0,k;
reg [7:0] value;
// Independent output decoder, samples centers of data bits and stop bit.
initial forever begin
    @(negedge uart_tx);
    #(BIT_TIME+BIT_TIME/2);
    for(k=0;k<8;k=k+1)begin value[k]=uart_tx;#(BIT_TIME);end
    if(uart_tx!==1)$fatal(1,"Bad UART TX stop bit");
    received[count]=value;count=count+1;
end
task send;
input [7:0] byte_value;
integer bit_index;
begin
    @(negedge clk);uart_rx=0;
    repeat(DIV)@(negedge clk);
    for(bit_index=0;bit_index<8;bit_index=bit_index+1)begin
        uart_rx=byte_value[bit_index];repeat(DIV)@(negedge clk);
    end
    uart_rx=1;repeat(DIV+2)@(negedge clk);
end
endtask
reg [31:0] stopped_count;
initial begin
    repeat(4)@(negedge clk);rst=0;
    send(0);wait(count==1);if(received[0]!==8'ha5)$fatal(1,"PING");
    send(1);send(8'h5a);wait(count==2);if(received[1]!==8'h5a)$fatal(1,"ECHO");
    send(3);send(0);wait(count==3);if(received[2]!==6 || control_out!==0)$fatal(1,"WRITE CONTROL");
    stopped_count=counter_out;
    send(4);wait(count==4);if(received[3]!==0)$fatal(1,"READ CONTROL");
    send(2);wait(count==8);
    if({received[7],received[6],received[5],received[4]}!==stopped_count)$fatal(1,"READ COUNTER");
    if(counter_out!==stopped_count)$fatal(1,"Counter failed to stop");
    send(99);wait(count==9);if(received[8]!==8'he0)$fatal(1,"UNKNOWN COMMAND");
    send(3);send(1);wait(count==10);if(received[9]!==6 || control_out!==1)$fatal(1,"ENABLE");
    send(4);wait(count==11);if(received[10]!==1)$fatal(1,"READ ENABLED CONTROL");
    if(rx_overrun || framing_error)$fatal(1,"Unexpected UART receive error");
    $display("PASS HDL serial UART: 11 reply bytes, ping/echo/control/counter/errors");$finish;
end
initial begin #200000;$fatal(1,"Serial test timeout");end
endmodule
