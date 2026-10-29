#!/usr/bin/env bash
# Run after run-scheduled-rtl.sh memory-qualification; never overwrite evidence.
set -euo pipefail
[[ $# == 1 && "$1" == /* ]] || { echo 'Usage: qualify-memory-hardware-linux.sh <absolute-retained-RTL-campaign>' >&2; exit 2; }
campaign=$1
test -d "$campaign" && test -d "$campaign/vectors"
runner="$campaign/runner-artifacts/bin/SharpHDL.ToolCli/release/SharpHDL.ToolCli.dll"
test -f "$runner" && test -f "$campaign/vectors/MemoryCollisionAuto.v"
evidence="$campaign/memory-hardware"
test ! -e "$evidence"
mkdir -- "$evidence"
cat > "$evidence/reference.v" <<'VERILOG'
// Independent procedural reference: three initialized words, reset retains RAM.
module collision_reference #(parameter WRITE_FIRST=0)(input clk,rst,input [1:0] address,
  input [3:0] left,right,input write,output reg [3:0] out_left,out_right);
reg [3:0] l[0:2],r[0:2];
initial begin l[0]=1;l[1]=2;l[2]=3;r[0]=7;r[1]=8;r[2]=9;end
always @(posedge clk) begin
  if(rst) begin out_left<=0;out_right<=0;end
  else begin
    out_left<=address<3 ? (WRITE_FIRST && write ? left : l[address]) : 0;
    out_right<=address<3 ? (WRITE_FIRST && write ? right : r[address]) : 0;
    if(write && address<3) begin l[address]<=left;r[address]<=right;end
  end
end
endmodule
VERILOG
for mode in good bad; do
  write_first=0; [[ "$mode" == good ]] || write_first=1
  cat > "$evidence/miter_$mode.v" <<VERILOG
module miter_$mode(input clk,rst,input [1:0] address,input [3:0] left,right,input write,output same);
wire [3:0] a,b,c,d;
h_MemoryCollisionAuto dut(.clk(clk),.rst(rst),.s_address(address),.s_left(left),.s_right(right),.s_write(write),.s_out_left(a),.s_out_right(b));
collision_reference #(.WRITE_FIRST($write_first)) reference(.clk(clk),.rst(rst),.address(address),.left(left),.right(right),.write(write),.out_left(c),.out_right(d));
assign same=(a==c)&&(b==d);
endmodule
VERILOG
  check='-verify'; [[ "$mode" == good ]] || check="-falsify -dump_json $evidence/collision-counterexample.json"
  dotnet "$runner" run "$evidence/sat-$mode" "$campaign/vectors" 120000 yosys -Q -T -p \
    "read_verilog $campaign/vectors/MemoryCollisionAuto.v $evidence/reference.v $evidence/miter_$mode.v; prep -top miter_$mode -flatten; memory_map; opt; sat -seq 3 -prove-skip 2 -set-at 1 rst 1 -set-at 2 rst 0 -set-at 3 rst 0 -prove same 1 -show-inputs -show-outputs -timeout 20 $check"
  cat "$evidence/sat-$mode/stdout.bin"
done
grep -F 'model found: FAIL!' "$evidence/sat-bad/stdout.bin"
test -s "$evidence/collision-counterexample.json"
for design in MemoryCollisionAuto MemoryCollisionXilinxBlock; do
  dotnet "$runner" run "$evidence/map-$design" "$campaign/vectors" 120000 yosys -Q -T -p \
    "read_verilog $campaign/vectors/$design.v; synth_xilinx -family xc7 -top h_$design; check -assert; stat; write_json $evidence/$design-xc7.json"
  cat "$evidence/map-$design/stdout.bin"
done
printf 'PASS independent three-step SAT/reference and collision counterexample; xc7 mapping retained at %s\n' "$evidence"
printf 'Placement/routing and device timing remain unverified without an installed target backend and constraints.\n'
