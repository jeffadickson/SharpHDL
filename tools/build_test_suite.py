"""Direct .NET 10 compiler route for the core or learning suite, without MSBuild."""
from pathlib import Path
import sys, subprocess, json
root=Path(__file__).resolve().parents[1]
sdkroot=Path(sys.argv[1]).resolve(); suite=sys.argv[2]
if len(sys.argv)>3 and sys.argv[3:]!=["--debug"]: raise SystemExit("optional flag: --debug")
optimization="-optimize-" if sys.argv[3:]==["--debug"] else "-optimize+"
if suite not in ('core','learning'): raise SystemExit('suite must be core or learning')
version=lambda p:tuple(map(int,p.name.split('.')))
sdk=max((sdkroot/'sdk').glob('10.*'),key=version)
pack=max((sdkroot/'packs/Microsoft.NETCore.App.Ref').glob('10.*'),key=version)
runtime=max((sdkroot/'shared/Microsoft.NETCore.App').glob('10.*'),key=version)
b=root/'build'/suite;b.mkdir(parents=True,exist_ok=True)
sources=list((root/'src/SharpHDL.Core').glob('*.cs'))+list((root/'src/SharpHDL.Simulation').glob('*.cs'))+list((root/'src/SharpHDL.HLS').glob('*.cs'))+list((root/'src/SharpHDL.LegacyRtl').glob('*.cs'))+list((root/'src/SharpHDL').glob('*.cs'))
sources+=list((root/'examples').glob('*.cs'))+list((root/'tests').glob('*.cs')) if suite=='core' else list((root/'examples/LearningLab').glob('*.cs'))
subprocess.run([str(sdkroot/'dotnet'),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo',optimization,'-nostdlib+','-target:exe','-out:'+str(b/(suite+'.dll'))]+['-r:'+str(p) for p in sorted((pack/'ref/net10.0').glob('*.dll'))]+[str(p) for p in sorted(sources)],check=True)
(b/(suite+'.runtimeconfig.json')).write_text(json.dumps({'runtimeOptions':{'tfm':'net10.0','framework':{'name':'Microsoft.NETCore.App','version':runtime.name}}}))
