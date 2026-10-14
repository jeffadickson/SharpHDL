# Run from repository root: vivado -mode batch -source tests/vendor-memory/xilinx.tcl
# Optional -tclargs <7-series-part>. Generated fixtures must already exist.
set part xc7a35tcpg236-1
if {$argc > 0} { set part [lindex $argv 0] }
file mkdir build/memory-targets/vivado
read_verilog generated/memory-targets/RamXilinx7SeriesBlock.v
synth_design -top h_RamXilinx7SeriesBlock -part $part
report_utilization -file build/memory-targets/vivado/utilization.rpt
if {[llength [get_cells -hier -filter {REF_NAME =~ RAMB*}]] == 0} {
    error "Block-memory fixture did not map to RAMB primitives"
}
write_checkpoint -force build/memory-targets/vivado/memory.dcp
puts "PASS Vivado block RAM mapping; vendor primitive simulation still required"
