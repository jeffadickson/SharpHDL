"""Run after tests/run-tinyrisc.sh; reuse its compiled SharpHDL assembly."""
from pathlib import Path
import os, subprocess, shutil
r=Path(__file__).resolve().parents[1];os.chdir(r)
sdkroot=Path(os.environ['DOTNET_ROOT']);sdk=max((sdkroot/'sdk').glob('8.*'));pack=max((sdkroot/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))
b=r/'build/tinyrisc';host=str(sdkroot/'dotnet')
subprocess.run([host,str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-nostdlib+','-target:exe','-out:'+str(b/'NetNaming.dll'),'-r:'+str(b/'TinyRisc.dll')]+['-r:'+str(p) for p in (pack/'ref/net8.0').glob('*.dll')]+['tests/NetNaming.cs'],check=True)
shutil.copy(b/'TinyRisc.runtimeconfig.json',b/'NetNaming.runtimeconfig.json')
subprocess.run([host,str(b/'NetNaming.dll')],check=True)
subprocess.run(['iverilog','-g2012','-s','tb','-o',str(b/'names-test'),str(b/'names.v'),str(b/'names_tb.v')],check=True)
subprocess.run(['vvp',str(b/'names-test')],check=True)
rtl=(r/'generated/tinyrisc/CountRun.v').read_text()
assert "assign n_effective_address = n_source_a_value + n_signed_immediate;" in rtl
assert "assign n_uart_tx_selected = n_effective_address == 16'd65282;" in rtl
assert 'n_writeback_value' in rtl and 'n_next_instruction_pc' in rtl
cil=(r/'generated/advanced/CsAssertBranch.v').read_text()
assert 'n_AssertBranch_IL_0011_add' in cil and 'AssertBranch IL_0011 add;' in cil
print('PASS semantic TinyRisc nets and CIL method/offset provenance')
