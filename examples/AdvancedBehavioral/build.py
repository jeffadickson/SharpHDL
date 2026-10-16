#!/usr/bin/env python3
"""Compile ordinary C#, F# and VB functions and the CIL-to-hardware test harness.
Usage: python3 examples/Behavioral/build.py /path/to/dotnet-sdk-root
Then: dotnet build/behavioral/Behavioral.dll generated/behavioral
No downloaded NuGet packages or MSBuild required.
"""
from pathlib import Path
import sys,subprocess,shutil,json
root=Path(__file__).resolve().parents[2]
sdkroot=Path(sys.argv[1]).resolve()
version=lambda p:tuple(map(int,p.name.split('.')))
sdk=max((sdkroot/'sdk').glob('10.*'),key=version)
pack=max((sdkroot/'packs/Microsoft.NETCore.App.Ref').glob('10.*'),key=version)
runtime=max((sdkroot/'shared/Microsoft.NETCore.App').glob('10.*'),key=version)
host=sdkroot/('dotnet.exe' if sys.platform=='win32' else 'dotnet')
build=root/'build/advanced';build.mkdir(parents=True,exist_ok=True)
source=root/'examples/AdvancedBehavioral'
refs=['-r:'+str(p) for p in sorted((pack/'ref/net10.0').glob('*.dll'))]
lib=build/'SharpHDL.dll'
def run(args):subprocess.run([str(x) for x in args],check=True)
def cs(out,files,extra=(),target='library'):
 run([host,sdk/'Roslyn/bincore/csc.dll','-nologo','-optimize+','-nostdlib+','-target:'+target,'-out:'+str(out)]+refs+list(extra)+list(files))
cs(lib,sorted((root/'src/SharpHDL.Core').glob('*.cs'))+sorted((root/'src/SharpHDL.Simulation').glob('*.cs'))+sorted((root/'src/SharpHDL.HLS').glob('*.cs'))+sorted((root/'src/SharpHDL.LegacyRtl').glob('*.cs'))+sorted((root/'src/SharpHDL').glob('*.cs')))
cs(build/'AdvancedFunctions.dll',[source/'Functions.cs'],['-r:'+str(lib)])
run([host,sdk/'Roslyn/bincore/csc.dll','-nologo','-optimize-','-nostdlib+','-target:library','-out:'+str(build/'AdvancedDebug.dll')]+refs+['-r:'+str(lib),source/'Functions.cs'])
cs(build/'Advanced.dll',[source/'Program.cs'],['-r:'+str(lib),'-r:'+str(build/'AdvancedFunctions.dll')],target='exe')
(build/'Advanced.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net10.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name}}},indent=2))
