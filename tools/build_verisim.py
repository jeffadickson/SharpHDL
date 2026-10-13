"""Documented VeriSim fragment links; no custom browser HDL engine."""
from pathlib import Path
import base64,json
root=Path(__file__).resolve().parents[1]
encode=lambda s:base64.urlsafe_b64encode(s.encode()).decode().rstrip('=')
fixtures=[('fsm','generated/learning/DrillController','''reg s_start=0,s_enable=1,s_stop=0;
wire [1:0] s_phase; wire [3:0] s_ticks; wire s_running,s_done;
h_DrillController dut(.*);
initial begin rst=1; #2; clk=1; #2; clk=0;rst=0;s_start=1;
#2;clk=1;#2;clk=0;s_start=0;
repeat(4) begin #2;clk=1;#2;clk=0;end
if(s_done!==1 || s_ticks!==4)$fatal(1,"controller");
'''),('forloop','generated/behavioral/CilCsLoop12','''reg [31:0] s_a=2,s_b=3;wire [31:0] s_y;wire s_fault;
h_CilCsLoop12 dut(.*);
initial begin #5;if(s_y!==38 || s_fault!==0)$fatal(1,"unrolled");
'''),('sequential','generated/advanced/CsSeqRuntimeWhile','''reg s_start=0;reg [31:0] s_arg0=3,s_arg1=10;
wire s_busy,s_done,s_fault,s_assertion_failed,s_timeout,s_accept;wire [31:0] s_result;
h_CsSeqRuntimeWhile dut(.*);
initial begin rst=1;#2;clk=1;#2;clk=0;rst=0;s_start=1;
#2;clk=1;#2;clk=0;s_start=0;
repeat(250) begin #2;clk=1;#2;clk=0;end
if(s_done!==1 || s_fault!==0 || s_result!==16)$fatal(1,"sequential");
''')]
items={};out=root/'generated/learning/verisim';out.mkdir(exist_ok=True)
for key,stem,body in fixtures:
 design=(root/(stem+'.v')).read_text()
 tb='`timescale 1ns/1ps\nmodule tb;reg clk=0,rst=0;\n'+body+'$display("PASS VeriSim '+key+'");$finish;end\ninitial begin $dumpfile("dump.vcd");$dumpvars(0,tb);end\nendmodule\n'
 url='https://senolgulgonul.github.io/verisim/#d='+encode(design)+'&t='+encode(tb)
 items[key]={'url':url,'design':design,'testbench':tb}
 (out/(key+'_tb.v')).write_text(tb)
(out/'links.json').write_text(json.dumps(items))
print('Built',len(items),'VeriSim links')
