module Z80BusFormal(input clk, input rst, input [0:0] s_step, input [0:0] s_load_state, input [0:0] s_bus_ready, input [7:0] s_bus_rdata, input [0:0] s_int_request, input [0:0] s_nmi, input [15:0] s_init_bc, input [15:0] s_init_alt0, input [15:0] s_init_de, input [15:0] s_init_alt1, input [15:0] s_init_hl, input [15:0] s_init_alt2, input [15:0] s_init_af, input [15:0] s_init_alt3, input [15:0] s_init_pc, input [15:0] s_init_sp, input [15:0] s_init_ix, input [15:0] s_init_iy, input [1:0] s_init_im, input [7:0] s_init_i, input [7:0] s_init_r, input [0:0] s_init_iff1, input [0:0] s_init_iff2, input [1:0] s_init_ei_delay);
wire [0:0] s_bus_io;
wire [0:0] s_bus_irq_ack;
wire [0:0] s_interrupt_done;
wire [0:0] s_reti;
wire [0:0] s_bus_valid;
wire [0:0] s_bus_write;
wire [15:0] s_bus_address;
wire [7:0] s_bus_wdata;
wire [0:0] s_instruction_done;
wire [0:0] s_idle;
wire [0:0] s_fault;
wire [0:0] s_halted_out;
wire [15:0] s_pc_out;
wire [15:0] s_sp_out;
wire [15:0] s_af_out;
wire [15:0] s_bc_out;
wire [15:0] s_de_out;
wire [15:0] s_hl_out;
wire [15:0] s_ix_out;
wire [15:0] s_iy_out;
wire [7:0] s_i_out;
wire [7:0] s_r_out;
wire [0:0] s_iff1_out;
wire [0:0] s_iff2_out;
wire [1:0] s_im_out;
wire [1:0] s_ei_delay_out;
wire [15:0] s_alt_out0;
wire [15:0] s_alt_out1;
wire [15:0] s_alt_out2;
wire [15:0] s_alt_out3;
wire [7:0] s_opcode_out;
h_Z80TestCore dut(.clk(clk),.rst(rst),.s_step(s_step),.s_load_state(s_load_state),.s_bus_ready(s_bus_ready),.s_bus_rdata(s_bus_rdata),.s_int_request(s_int_request),.s_nmi(s_nmi),.s_init_bc(s_init_bc),.s_init_alt0(s_init_alt0),.s_init_de(s_init_de),.s_init_alt1(s_init_alt1),.s_init_hl(s_init_hl),.s_init_alt2(s_init_alt2),.s_init_af(s_init_af),.s_init_alt3(s_init_alt3),.s_init_pc(s_init_pc),.s_init_sp(s_init_sp),.s_init_ix(s_init_ix),.s_init_iy(s_init_iy),.s_init_im(s_init_im),.s_init_i(s_init_i),.s_init_r(s_init_r),.s_init_iff1(s_init_iff1),.s_init_iff2(s_init_iff2),.s_init_ei_delay(s_init_ei_delay),.s_bus_io(s_bus_io),.s_bus_irq_ack(s_bus_irq_ack),.s_interrupt_done(s_interrupt_done),.s_reti(s_reti),.s_bus_valid(s_bus_valid),.s_bus_write(s_bus_write),.s_bus_address(s_bus_address),.s_bus_wdata(s_bus_wdata),.s_instruction_done(s_instruction_done),.s_idle(s_idle),.s_fault(s_fault),.s_halted_out(s_halted_out),.s_pc_out(s_pc_out),.s_sp_out(s_sp_out),.s_af_out(s_af_out),.s_bc_out(s_bc_out),.s_de_out(s_de_out),.s_hl_out(s_hl_out),.s_ix_out(s_ix_out),.s_iy_out(s_iy_out),.s_i_out(s_i_out),.s_r_out(s_r_out),.s_iff1_out(s_iff1_out),.s_iff2_out(s_iff2_out),.s_im_out(s_im_out),.s_ei_delay_out(s_ei_delay_out),.s_alt_out0(s_alt_out0),.s_alt_out1(s_alt_out1),.s_alt_out2(s_alt_out2),.s_alt_out3(s_alt_out3),.s_opcode_out(s_opcode_out));
reg past_valid=0, past_reset, past_request, past_ready;
reg [15:0] past_bus_address;
reg [7:0] past_bus_wdata;
reg [0:0] past_bus_write;
reg [0:0] past_bus_io;
reg [0:0] past_bus_irq_ack;
always @(posedge clk) begin
if (past_valid && !rst && !past_reset && past_request && !past_ready) begin
assert(s_bus_valid);
assert(s_bus_address == past_bus_address);
assert(s_bus_wdata == past_bus_wdata);
assert(s_bus_write == past_bus_write);
assert(s_bus_io == past_bus_io);
assert(s_bus_irq_ack == past_bus_irq_ack);
end
past_valid <= 1; past_reset <= rst; past_request <= s_bus_valid; past_ready <= s_bus_ready;
past_bus_address <= s_bus_address;
past_bus_wdata <= s_bus_wdata;
past_bus_write <= s_bus_write;
past_bus_io <= s_bus_io;
past_bus_irq_ack <= s_bus_irq_ack;
end
endmodule
