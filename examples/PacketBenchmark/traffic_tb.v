// Independent sequence oracle, shared by source RTL and mapped cell netlists.
// Inputs hold stable until accepted. Reset cancels the entire partial packet.
module tb;
 reg clk=0,rst=1;
 reg [31:0] s_in_data=0;
 reg s_in_valid=0,s_out_ready=0;
 wire s_in_ready,s_out_valid;
 wire [31:0] s_out_data;
 h_PacketChecksum dut(.*);
 reg [31:0] rng=32'h12345678,expected=0,held_data=0;
 reg [15:0] model_sum=0,total;
 reg occupied=0,held=0;
 integer cycle,accepted=0,received=0,discarded=0,packets=0,stalls=0,replacements=0;
 integer reset_blocked=0,reset_replacement=0;
 integer zero_words=0,overflows=0,single_packets=0,packet_words=0;
 initial begin
  #5;clk=1;#5;clk=0;rst=0;
  for(cycle=0;cycle<12000;cycle=cycle+1) begin
   rng={rng[30:0],rng[31]^rng[21]^rng[1]^rng[0]};
   // First 1024 cycles saturated; later bursts and a final drain.
   if(!s_in_valid && cycle<11000) begin
     s_in_valid=(cycle<1024)||rng[2]||rng[3];
     s_in_data={rng[7:4]==0,rng[30:16],rng[15:0]};
     // Directed wrapping, zero data, and consecutive single-word packets.
     case(accepted)
       0:s_in_data=32'h1234ffff;
       1:s_in_data=32'h56780001;
       2:s_in_data=32'h80000000;
       3:s_in_data=32'h80000000;
       4:s_in_data=32'hffffffff;
     endcase
   end
   s_out_ready=(cycle<1024)||(cycle>=11000)||((cycle%83)<53 && rng[0]);
   rst=0;
   #1;
   if(cycle>2100 && !reset_blocked && s_out_valid && !s_out_ready) begin rst=1;reset_blocked=1;end
   if(cycle>7100 && !reset_replacement && s_out_valid && s_out_ready && s_in_valid && s_in_ready) begin rst=1;reset_replacement=1;end
   if(held && (!s_out_valid || s_out_data!==held_data)) $fatal(1,"output changed while stalled at %0d",cycle);
   if(rst) begin
     if(occupied) discarded=discarded+1;
     occupied=0;model_sum=0;packet_words=0;held=0;
   end else begin
     if(s_in_ready !== (!s_out_valid || s_out_ready)) $fatal(1,"admission at %0d",cycle);
     if(s_out_valid !== occupied) $fatal(1,"latency/occupancy at %0d",cycle);
     if(s_out_valid && s_out_ready) begin
       if(!occupied || s_out_data!==expected) $fatal(1,"checksum/order at %0d: %h != %h",cycle,s_out_data,expected);
       received=received+1;occupied=0;
     end
     if(s_in_valid && s_in_ready) begin
       if(occupied) $fatal(1,"overflow");
       if(s_in_data[15:0]==0) zero_words=zero_words+1;
       if(({1'b0,model_sum}+{1'b0,s_in_data[15:0]})>17'd65535) overflows=overflows+1;
       packet_words=packet_words+1;
       if(s_in_data[31]) begin
         if(packet_words==1) single_packets=single_packets+1;
         packet_words=0;
       end
       total=model_sum+s_in_data[15:0];
       expected={s_in_data[31],15'b0,total};
       model_sum=s_in_data[31] ? 0 : total;
       if(s_in_data[31]) packets=packets+1;
       occupied=1;accepted=accepted+1;
       if(s_out_valid && s_out_ready) replacements=replacements+1;
     end
     held=s_out_valid && !s_out_ready;held_data=s_out_data;
     if(held) stalls=stalls+1;
   end
   // Preserve presented signals through the active edge, then release accepted input.
   begin : edge_sample
     reg input_taken;
     input_taken=!rst && s_in_valid && s_in_ready;
     #4;clk=1;#5;clk=0;
     if(input_taken) s_in_valid=0;
   end
   if(cycle==1023 && (accepted!=1024 || received!=1023)) $fatal(1,"sustained II/latency");
  end
  if(occupied || accepted!=received+discarded || packets<100 || stalls<100 || replacements<1000 || !reset_blocked || !reset_replacement || zero_words==0 || overflows==0 || single_packets==0) $fatal(1,"coverage/accounting");
  $display("PASS packet traffic: cycles=12000 accepted=%0d received=%0d reset_discarded=%0d packet_ends=%0d stalled_cycles=%0d replacements=%0d; saturated II=1 latency=1",accepted,received,discarded,packets,stalls,replacements);
  $finish;
 end
endmodule
