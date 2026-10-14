// TinyRisc16 + existing ROM and RAM + MMIO TX byte interface + real UART 8N1.
// Compile with generated/tinyrisc/HelloWorld.v.
module tinyrisc_hello_top #(parameter integer CLKS_PER_BIT=16)(
    input wire clk, input wire rst, output wire uart_tx,
    output wire halted, output wire fault, output wire [15:0] pc
);
    wire tx_valid, tx_ready;
    wire [7:0] tx_data;
    h_HelloWorld cpu(
        .clk(clk), .rst(rst),
        .s_run(1'b1), .s_step(1'b0), .s_switches(16'd0),
        .s_rx_valid(1'b0), .s_rx_data(8'd0), .s_tx_ready(tx_ready),
        .s_tx_valid(tx_valid), .s_tx_data(tx_data),
        .s_rx_ready(), .s_pc(pc), .s_ir(), .s_phase(),
        .s_halted(halted), .s_fault(fault), .s_retired(),
        .s_last_pc(), .s_leds(),
        .s_r0(), .s_r1(), .s_r2(), .s_r3(),
        .s_r4(), .s_r5(), .s_r6(), .s_r7()
    );
    tinyrisc_uart_tx #(.CLKS_PER_BIT(CLKS_PER_BIT)) serial(
        .clk(clk), .rst(rst), .data(tx_data), .valid(tx_valid),
        .ready(tx_ready), .tx(uart_tx)
    );
endmodule

// Accept exactly one byte on valid && ready. Idle-high UART, start, 8 LSB-first
// bits, stop. ready remains low throughout serialization.
module tinyrisc_uart_tx #(parameter integer CLKS_PER_BIT=16)(
    input wire clk, input wire rst, input wire [7:0] data,
    input wire valid, output wire ready, output wire tx
);
    reg busy=0;
    reg [9:0] shifter=10'h3ff;
    reg [3:0] bit_index=0;
    reg [31:0] timer=0;
    assign ready=!busy;
    assign tx=busy ? shifter[0] : 1'b1;
    initial if (CLKS_PER_BIT < 2) $fatal(1,"CLKS_PER_BIT must be >=2");
    always @(posedge clk) begin
        if(rst) begin
            busy<=0; shifter<=10'h3ff;bit_index<=0;timer<=0;
        end else if (!busy) begin
            if(valid) begin
                shifter<={1'b1,data,1'b0};
                busy<=1;
                bit_index<=0;
                timer<=CLKS_PER_BIT-1;
            end
        end else if (timer != 0) begin
            timer<=timer-1;
        end else if(bit_index==9) begin
            busy<=0;
            shifter<=10'h3ff;
            timer<=0;
        end else begin
            shifter<={1'b1,shifter[9:1]};
            bit_index<=bit_index+1'b1;
            timer<=CLKS_PER_BIT-1;
        end
    end
endmodule
