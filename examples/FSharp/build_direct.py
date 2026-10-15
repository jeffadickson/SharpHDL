#!/usr/bin/env python3
"""Compile with an installed .NET 8 SDK's C#/F# compilers, without MSBuild/NuGet.
Usage: python3 examples/FSharp/build_direct.py /path/to/dotnet-install
Then:  /path/to/dotnet-install/dotnet build/fsharp/FSharpExamples.dll generated/fsharp
"""
from pathlib import Path
import json
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
sdk_root = Path(sys.argv[1]).resolve()
def version(path):
    return tuple(int(piece) for piece in path.name.split('.'))
sdk = max((p for p in (sdk_root / 'sdk').glob('8.*') if p.is_dir()), key=version)
reference_pack = max((sdk_root / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'), key=version)
runtime = max((sdk_root / 'shared/Microsoft.NETCore.App').glob('8.*'), key=version)
host = sdk_root / ('dotnet.exe' if sys.platform == 'win32' else 'dotnet')
build = root / 'build/fsharp'
build.mkdir(parents=True, exist_ok=True)
refs = ['-r:' + str(p) for p in sorted((reference_pack / 'ref/net8.0').glob('*.dll'))]
library = build / 'SharpHDL.dll'
subprocess.run([str(host), str(sdk / 'Roslyn/bincore/csc.dll'), '-nologo',
                '-target:library', '-nostdlib+', '-out:' + str(library)] + refs +
               [str(p) for p in sorted((root / 'src/SharpHDL.Core').glob('*.cs'))] +
               [str(p) for p in sorted((root / 'src/SharpHDL.Simulation').glob('*.cs'))] +
               [str(p) for p in sorted((root / 'src/SharpHDL.HLS').glob('*.cs'))] +
               [str(p) for p in sorted((root / 'src/SharpHDL').glob('*.cs'))], check=True)
fsharp_core = sdk / 'FSharp/FSharp.Core.dll'
subprocess.run([str(host), str(sdk / 'FSharp/fsc.dll'), '--target:exe',
                '--targetprofile:netcore', '--noframework',
                '--out:' + str(build / 'FSharpExamples.dll'),
                '-r:' + str(fsharp_core), '-r:' + str(library)] + refs +
               [str(root / 'examples/FSharp' / name) for name in ['Designs.fs', 'NumericDesigns.fs', 'Program.fs']], check=True)
shutil.copy2(fsharp_core, build / 'FSharp.Core.dll')
(build / 'FSharpExamples.runtimeconfig.json').write_text(json.dumps({
    'runtimeOptions': {'tfm': 'net8.0', 'framework': {
        'name': 'Microsoft.NETCore.App', 'version': runtime.name}}}, indent=2))
print('Built:', build / 'FSharpExamples.dll')
