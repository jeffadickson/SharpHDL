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
sdk=max((sdkroot/'sdk').glob('8.*'),key=version)
pack=max((sdkroot/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version)
runtime=max((sdkroot/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)
host=sdkroot/('dotnet.exe' if sys.platform=='win32' else 'dotnet')
build=root/'build/behavioral';build.mkdir(parents=True,exist_ok=True)
source=root/'examples/Behavioral'
refs=['-r:'+str(p) for p in sorted((pack/'ref/net8.0').glob('*.dll'))]
lib=build/'SharpHDL.dll'
def run(args):subprocess.run([str(x) for x in args],check=True)
def cs(out,files,extra=(),target='library'):
 run([host,sdk/'Roslyn/bincore/csc.dll','-nologo','-optimize+','-nostdlib+','-target:'+target,'-out:'+str(out)]+refs+list(extra)+list(files))
cs(lib,sorted((root/'src/SharpHDL.Core').glob('*.cs'))+sorted((root/'src/SharpHDL.Simulation').glob('*.cs'))+sorted((root/'src/SharpHDL.HLS').glob('*.cs'))+sorted((root/'src/SharpHDL').glob('*.cs')))
cs(build/'CSharpFunctions.dll',[source/'Functions.cs'],['-r:'+str(lib)])
run([host,sdk/'Roslyn/bincore/csc.dll','-nologo','-optimize-','-nostdlib+','-target:library','-out:'+str(build/'CSharpDebug.dll')]+refs+['-r:'+str(lib),source/'Functions.cs'])
fs=sdk/'FSharp/FSharp.Core.dll'
run([host,sdk/'FSharp/fsc.dll','--target:library','--targetprofile:netcore','--noframework','--optimize+',
     '--out:'+str(build/'FSharpFunctions.dll'),'-r:'+str(lib),'-r:'+str(fs)]+refs+[source/'Functions.fs'])
# Keep VB's normal overflow checks enabled; the CIL compiler must preserve faults.
run([host,sdk/'Roslyn/bincore/vbc.dll','-nologo','-optimize+','-target:library','-nostdlib','-vbruntime*',
     '-out:'+str(build/'VisualBasicFunctions.dll'),'-r:'+str(lib)]+refs+[source/'Functions.vb'])
cs(build/'Behavioral.dll',[source/'Program.cs'],
   ['-r:'+str(lib),'-r:'+str(fs)]+['-r:'+str(build/name) for name in ['CSharpFunctions.dll','FSharpFunctions.dll','VisualBasicFunctions.dll']],target='exe')
shutil.copy2(fs,build/'FSharp.Core.dll')
(build/'Behavioral.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name}}},indent=2))
print('Built',build/'Behavioral.dll')
