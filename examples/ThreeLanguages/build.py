#!/usr/bin/env python3
"""Compile three independent .NET 8 language assemblies and their C# test harness.
Usage: python3 examples/ThreeLanguages/build.py /path/to/dotnet-sdk-root
Then: dotnet build/three-languages/ThreeLanguages.dll generated/three-languages
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
build=root/'build/three-languages';build.mkdir(parents=True,exist_ok=True)
source=root/'examples/ThreeLanguages'
refs=['-r:'+str(p) for p in sorted((pack/'ref/net8.0').glob('*.dll'))]
lib=build/'SharpHDL.dll'
def run(args):subprocess.run([str(x) for x in args],check=True)
def cs(out,files,extra=(),target='library'):
 run([host,sdk/'Roslyn/bincore/csc.dll','-nologo','-optimize+','-nostdlib+','-target:'+target,'-out:'+str(out)]+refs+list(extra)+list(files))
cs(lib,sorted((root/'src/SharpHDL').glob('*.cs')))
cs(build/'CSharpPiece.dll',[source/'CSharpPiece.cs'],['-r:'+str(lib)])
fs=sdk/'FSharp/FSharp.Core.dll'
run([host,sdk/'FSharp/fsc.dll','--target:library','--targetprofile:netcore','--noframework','--optimize+',
     '--out:'+str(build/'FSharpPiece.dll'),'-r:'+str(lib),'-r:'+str(fs)]+refs+[source/'FSharpPiece.fs'])
# Keep VB's normal overflow checks enabled. Hardware Expr operators still call the library.
run([host,sdk/'Roslyn/bincore/vbc.dll','-nologo','-optimize+','-target:library','-nostdlib','-vbruntime*',
     '-out:'+str(build/'VisualBasicPiece.dll'),'-r:'+str(lib)]+refs+[source/'VisualBasicPiece.vb'])
cs(build/'ThreeLanguages.dll',[source/'Program.cs',root/'boards/GoBoard/ClassroomCore.cs',root/'boards/GoBoard/StudentLogic.cs'],
   ['-r:'+str(lib),'-r:'+str(fs)]+['-r:'+str(build/name) for name in ['CSharpPiece.dll','FSharpPiece.dll','VisualBasicPiece.dll']],target='exe')
shutil.copy2(fs,build/'FSharp.Core.dll')
(build/'ThreeLanguages.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name}}},indent=2))
print('Built',build/'ThreeLanguages.dll')
