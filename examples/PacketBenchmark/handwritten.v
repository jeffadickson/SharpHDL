// Independent one-stage reference. Same protocol/latency as the CIL design.
module h_PacketChecksum(
 input clk,rst,
 input [31:0] s_in_data, input s_in_valid, output s_in_ready,
 output [31:0] s_out_data, output reg s_out_valid,
 input s_out_ready
);
 // Input [15:0]: payload; [31]: last; [30:16]: ignored.
 // Output [15:0]: inclusive modulo-65536 sum; [31]: last; others zero.
 reg [15:0] running_sum, result_sum;
 reg result_last;
 wire accept=s_in_valid && s_in_ready;
 // The hand implementation advances private state on input acceptance.
 // Reset clears speculative state and pending output together. Only the
 // external token sequence is the contract; private state need not match CIL.
 wire [15:0] total=running_sum+s_in_data[15:0];
 assign s_in_ready=!s_out_valid || s_out_ready;
 assign s_out_data={result_last,15'b0,result_sum};
 always @(posedge clk) begin
   if(rst) begin
     running_sum<=0;result_sum<=0;
     result_last<=0;s_out_valid<=0;
   end else begin
     if(s_in_ready) s_out_valid<=s_in_valid;
     if(accept) begin
       result_sum<=total;
       result_last<=s_in_data[31];
       running_sum<=s_in_data[31] ? 16'b0 : total;
     end
   end
 end
endmodule
