// Hand-authored comparison design. Not a copy of emitted SharpHDL Verilog.
// Single synchronous active-high reset, signed 16-bit samples, 32-bit sums.
// Input keep must be 2'b11. Each output is a one-beat tagged feature result.
module filter_bank_handwritten (
    input wire clk, input wire rst,
    input wire [15:0] s_axis_tdata,
    input wire [1:0] s_axis_tkeep,
    input wire s_axis_tlast,
    input wire [7:0] s_axis_tuser,
    input wire s_axis_tvalid, output wire s_axis_tready,
    output wire [31:0] m_axis_tdata,
    output wire [3:0] m_axis_tkeep,
    output wire m_axis_tlast,
    output wire [15:0] m_axis_tuser,
    output wire m_axis_tvalid, input wire m_axis_tready
);
    wire [3:0] branch_valid, branch_ready;
    reg [3:0] delivered;
    wire input_fire = s_axis_tvalid && s_axis_tready;

    // A branch may accept before the other branches. Remember each delivery.
    assign branch_valid = {4{s_axis_tvalid}} & ~delivered;
    assign s_axis_tready = &(delivered | branch_ready);
    always @(posedge clk) begin
        if (rst) delivered <= 4'b0000;
        else if (input_fire) delivered <= 4'b0000;
        else delivered <= delivered | (branch_valid & branch_ready);
    end

    // Fixed flattened buses keep the example plain Verilog-2001 compatible.
    wire [127:0] result_data;
    wire [15:0] result_keep;
    wire [63:0] result_user;
    wire [3:0] result_valid, result_ready;

    feature_branch #(.C0(1),.C1(3),.C2(3),.C3(1),.DEPTH(4),.CHANNEL(0)) smooth (
        .clk(clk),.rst(rst),.s_data(s_axis_tdata),.s_last(s_axis_tlast),.s_user(s_axis_tuser),
        .s_valid(branch_valid[0]),.s_ready(branch_ready[0]),
        .m_data(result_data[31:0]),.m_keep(result_keep[3:0]),.m_user(result_user[15:0]),
        .m_valid(result_valid[0]),.m_ready(result_ready[0]));
    feature_branch #(.C0(1),.C1(-1),.C2(0),.C3(0),.DEPTH(2),.CHANNEL(1)) slope (
        .clk(clk),.rst(rst),.s_data(s_axis_tdata),.s_last(s_axis_tlast),.s_user(s_axis_tuser),
        .s_valid(branch_valid[1]),.s_ready(branch_ready[1]),
        .m_data(result_data[63:32]),.m_keep(result_keep[7:4]),.m_user(result_user[31:16]),
        .m_valid(result_valid[1]),.m_ready(result_ready[1]));
    feature_branch #(.C0(1),.C1(-2),.C2(1),.C3(0),.DEPTH(3),.CHANNEL(2)) curvature (
        .clk(clk),.rst(rst),.s_data(s_axis_tdata),.s_last(s_axis_tlast),.s_user(s_axis_tuser),
        .s_valid(branch_valid[2]),.s_ready(branch_ready[2]),
        .m_data(result_data[95:64]),.m_keep(result_keep[11:8]),.m_user(result_user[47:32]),
        .m_valid(result_valid[2]),.m_ready(result_ready[2]));
    feature_branch #(.C0(1),.C1(0),.C2(0),.C3(0),.DEPTH(1),.CHANNEL(3)) raw (
        .clk(clk),.rst(rst),.s_data(s_axis_tdata),.s_last(s_axis_tlast),.s_user(s_axis_tuser),
        .s_valid(branch_valid[3]),.s_ready(branch_ready[3]),
        .m_data(result_data[127:96]),.m_keep(result_keep[15:12]),.m_user(result_user[63:48]),
        .m_valid(result_valid[3]),.m_ready(result_ready[3]));

    wire [31:0] left_data,right_data;
    wire [3:0] left_keep,right_keep;
    wire [15:0] left_user,right_user;
    wire left_valid,right_valid,left_ready,right_ready;
    feature_merge2 merge_left (
        .clk(clk),.rst(rst),
        .a_data(result_data[31:0]),.a_keep(result_keep[3:0]),.a_user(result_user[15:0]),.a_valid(result_valid[0]),.a_ready(result_ready[0]),
        .b_data(result_data[63:32]),.b_keep(result_keep[7:4]),.b_user(result_user[31:16]),.b_valid(result_valid[1]),.b_ready(result_ready[1]),
        .m_data(left_data),.m_keep(left_keep),.m_user(left_user),.m_valid(left_valid),.m_ready(left_ready));
    feature_merge2 merge_right (
        .clk(clk),.rst(rst),
        .a_data(result_data[95:64]),.a_keep(result_keep[11:8]),.a_user(result_user[47:32]),.a_valid(result_valid[2]),.a_ready(result_ready[2]),
        .b_data(result_data[127:96]),.b_keep(result_keep[15:12]),.b_user(result_user[63:48]),.b_valid(result_valid[3]),.b_ready(result_ready[3]),
        .m_data(right_data),.m_keep(right_keep),.m_user(right_user),.m_valid(right_valid),.m_ready(right_ready));
    feature_merge2 merge_output (
        .clk(clk),.rst(rst),
        .a_data(left_data),.a_keep(left_keep),.a_user(left_user),.a_valid(left_valid),.a_ready(left_ready),
        .b_data(right_data),.b_keep(right_keep),.b_user(right_user),.b_valid(right_valid),.b_ready(right_ready),
        .m_data(m_axis_tdata),.m_keep(m_axis_tkeep),.m_user(m_axis_tuser),.m_valid(m_axis_tvalid),.m_ready(m_axis_tready));
    assign m_axis_tlast = 1'b1;
endmodule

// Four-tap FIR plus one elastic output register and a small queued output.
// Unused taps have zero coefficients and are removed by synthesis.
module feature_branch #(
    parameter signed [31:0] C0=1,C1=0,C2=0,C3=0,
    parameter integer DEPTH=4,
    parameter [7:0] CHANNEL=0
)(
    input wire clk,input wire rst,
    input wire [15:0] s_data,input wire s_last,input wire [7:0] s_user,
    input wire s_valid,output wire s_ready,
    output wire [31:0] m_data,output wire [3:0] m_keep,output wire [15:0] m_user,
    output wire m_valid,input wire m_ready
);
    reg signed [15:0] delay1,delay2,delay3;
    wire signed [31:0] x0={{16{s_data[15]}},s_data};
    wire signed [31:0] x1={{16{delay1[15]}},delay1};
    wire signed [31:0] x2={{16{delay2[15]}},delay2};
    wire signed [31:0] x3={{16{delay3[15]}},delay3};
    wire signed [31:0] p0=x0*C0,p1=x1*C1,p2=x2*C2,p3=x3*C3;
    wire signed [31:0] sum=p0+p1+p2+p3;

    reg valid;
    reg [31:0] data;
    reg [3:0] keep;
    reg last;
    reg [7:0] user;
    wire fifo_ready;
    assign s_ready = !valid || fifo_ready;
    wire fire=s_valid && s_ready;
    always @(posedge clk) begin
        if (rst) begin
            delay1<=0;delay2<=0;delay3<=0;
            valid<=0;data<=0;keep<=0;last<=0;user<=0;
        end else begin
            if (s_ready) valid<=s_valid;
            if (fire) begin
                delay1<=s_data;delay2<=delay1;delay3<=delay2;
                data<=sum;keep<=4'hf;last<=s_last;user<=s_user;
            end
        end
    end
    wire [44:0] head;
    feature_fifo #(.WIDTH(45),.DEPTH(DEPTH)) queue (
        .clk(clk),.rst(rst),.s_payload({user,last,keep,data}),.s_valid(valid),.s_ready(fifo_ready),
        .m_payload(head),.m_valid(m_valid),.m_ready(m_ready));
    assign m_data=head[31:0];
    assign m_keep=head[35:32];
    // The queued input Last bit is intentionally replaced: one result per message.
    assign m_user={CHANNEL,head[44:37]};
endmodule

// Register-array FIFO, no fall-through. Allows push and pop together when full.
module feature_fifo #(parameter WIDTH=45,parameter DEPTH=4)(
    input wire clk,input wire rst,
    input wire [WIDTH-1:0] s_payload,input wire s_valid,output wire s_ready,
    output wire [WIDTH-1:0] m_payload,output wire m_valid,input wire m_ready
);
    localparam COUNT_WIDTH=$clog2(DEPTH+1);
    reg [COUNT_WIDTH-1:0] count;
    reg [WIDTH-1:0] slots[0:DEPTH-1];
    assign m_valid=(count!=0);
    wire pop=m_valid && m_ready;
    assign s_ready=(count<DEPTH) || pop;
    wire push=s_valid && s_ready;
    wire [COUNT_WIDTH-1:0] write_index=pop ? count-1'b1 : count;
    assign m_payload=slots[0];
    integer i;
    always @(posedge clk) begin
        if (rst) begin
            count<=0;
            for(i=0;i<DEPTH;i=i+1) slots[i]<=0;
        end else begin
            case ({push,pop})
                2'b10: count<=count+1'b1;
                2'b01: count<=count-1'b1;
                default: count<=count;
            endcase
            for(i=0;i<DEPTH;i=i+1) begin
                if(push && write_index==i) slots[i]<=s_payload;
                else if(pop) begin
                    if(i+1<DEPTH) slots[i]<=slots[i+1];
                    else slots[i]<=0;
                end
            end
        end
    end
endmodule

// Round-robin two-input merger for single-beat feature messages.
// Lock an offered choice while stalled; otherwise a new contender could change
// the output payload before the consumer accepts it.
module feature_merge2 (
    input wire clk,input wire rst,
    input wire [31:0] a_data,input wire [3:0] a_keep,input wire [15:0] a_user,
    input wire a_valid,output wire a_ready,
    input wire [31:0] b_data,input wire [3:0] b_keep,input wire [15:0] b_user,
    input wire b_valid,output wire b_ready,
    output wire [31:0] m_data,output wire [3:0] m_keep,output wire [15:0] m_user,
    output wire m_valid,input wire m_ready
);
    reg locked,selected,turn;
    wire choose_b=locked ? selected : ((a_valid && b_valid) ? turn : !a_valid);
    assign m_valid=choose_b ? b_valid : a_valid;
    assign m_data=choose_b ? b_data : a_data;
    assign m_keep=choose_b ? b_keep : a_keep;
    assign m_user=choose_b ? b_user : a_user;
    assign a_ready=m_ready && !choose_b;
    assign b_ready=m_ready && choose_b;
    wire fire=m_valid && m_ready;
    wire offer=m_valid && !locked;
    always @(posedge clk) begin
        if(rst) begin locked<=0;selected<=0;turn<=0;end
        else begin
            if(offer) selected<=choose_b;
            if(fire) begin locked<=0;turn<=!choose_b;end
            else if(offer) locked<=1;
        end
    end
endmodule
