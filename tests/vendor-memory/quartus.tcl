# Run from repository root:
# quartus_sh -t tests/vendor-memory/quartus.tcl "Cyclone V" <exact-device>
# or: quartus_sh -t tests/vendor-memory/quartus.tcl "Arria 10" <exact-device>
# An actual installed/supported device must be supplied; no silent family fallback.
package require ::quartus::project
package require ::quartus::flow
if {$argc != 2} { error "Supply family (Cyclone V or Arria 10) and exact device" }
set family [lindex $argv 0]
set device [lindex $argv 1]
switch -- $family {
    "Cyclone V" { set stem RamIntelCycloneVBlock }
    "Arria 10" { set stem RamIntelArria10Block }
    default { error "Unsupported memory target family" }
}
set rtl [file normalize generated/memory-targets/$stem.v]
set dest [file normalize build/memory-targets/quartus-$stem]
file mkdir $dest
cd $dest
project_new memory -overwrite
set_global_assignment -name FAMILY $family
set_global_assignment -name DEVICE $device
set_global_assignment -name TOP_LEVEL_ENTITY h_$stem
set_global_assignment -name VERILOG_FILE $rtl
execute_flow -compile
project_close
puts "Quartus compile completed. Inspect fitter RAM type/count and collision implementation; vendor primitive simulation remains required."
