from pathlib import Path
import sys,subprocess,json
root=Path(__file__).resolve().parents[2];sdkroot=Path(sys.argv[1]).resolve();version=lambda p:tuple(map(int,p.name.split('.')))
sdk=max((sdkroot/'sdk').glob('8.*'),key=version);pack=max((sdkroot/'packs/Microsoft.NETCore.App.Ref').glob('8.*'),key=version);runtime=max((sdkroot/'shared/Microsoft.NETCore.App').glob('8.*'),key=version)
b=root/'build/tinyrisc';b.mkdir(parents=True,exist_ok=True);src=root/'examples/TinyRisc';host=sdkroot/'dotnet'
refs=['-r:'+str(p) for p in sorted((pack/'ref/net8.0').glob('*.dll'))]
subprocess.run([str(host),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-optimize+','-nostdlib+','-target:exe','-out:'+str(b/'TinyRisc.dll')]+refs+[str(p) for p in sorted((root/'src/SharpHDL.Core').glob('*.cs'))]+[str(p) for p in sorted((root/'src/SharpHDL.Simulation').glob('*.cs'))]+[str(p) for p in sorted((root/'src/SharpHDL').glob('*.cs'))]+[str(src/'TinyRisc.cs'),str(src/'Program.cs')],check=True)
(b/'TinyRisc.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name}}}))
