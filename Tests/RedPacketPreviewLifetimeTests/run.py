#!/usr/bin/env python3
"""Probe actual packaged plugin/game binaries; synthetic objects, no HTTP, no player data."""
import argparse,subprocess,zipfile,re,shutil,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
for key in ['package','host-package','game','output']:p.add_argument('--'+key,type=Path,required=True)
p.add_argument('--game-version',choices=['1.6','1.7'],required=True);p.add_argument('--expect-bug',action='store_true');a=p.parse_args()
out=a.output.resolve()
if out.exists():p.error('Choose fresh output.')
out.mkdir(parents=True);tfm='net472' if a.game_version=='1.6' else 'net10.0'
with zipfile.ZipFile(a.package) as z:
 for name in ['LegacyRedPacketExtension.dll','LegacyRedPacketExtension.Client.dll']:(out/name).write_bytes(z.read(a.game_version+'/Assemblies/'+name))
project=E.Element('Project',Sdk='Microsoft.NET.Sdk');g=E.SubElement(project,'PropertyGroup')
for k,v in {'TargetFramework':tfm,'OutputType':'Exe','AssemblyName':'RedPacketInventoryRuntimeTests','EnableDefaultCompileItems':'false','NuGetAudit':'false','LangVersion':'latest'}.items():E.SubElement(g,k).text=v
items=E.SubElement(project,'ItemGroup');E.SubElement(items,'Compile',Include=str(Path(__file__).parent/'Program.cs'))
refs={};(out/'refs').mkdir()
for folder in ['Assemblies','Extensions']:
 for f in (a.host_package.resolve()/a.game_version/folder).rglob('*.dll'):
  canonical=re.sub(r'^\d+-','',f.name)
  if canonical=='Protobuf.dll':canonical='Google.Protobuf.dll'
  dest=out/'refs'/canonical;shutil.copy2(f,dest);refs[dest.stem]=dest
for f in out.glob('*.dll'):refs[f.stem]=f
for name in ['Assembly-CSharp','UnityStubs','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule','Unity.Burst','Assembly-CSharp-firstpass']:
 f=a.game.resolve()/(name+'.dll')
 if f.is_file():refs[name]=f
for name,f in refs.items():r=E.SubElement(items,'Reference',Include=name);E.SubElement(r,'HintPath').text=str(f)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');(out/'NuGet.Config').write_text('<configuration><packageSources><clear /><add key="local" value="'+str(Path.home()/'.nuget/packages')+'" /></packageSources></configuration>')
for phase,cmd in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-v:q']),('test',(['mono'] if tfm=='net472' else ['dotnet'])+[str(out/'bin/Release'/tfm/('RedPacketInventoryRuntimeTests.exe' if tfm=='net472' else 'RedPacketInventoryRuntimeTests.dll'))]+(['--expect-bug'] if a.expect_bug else []))]:
 if phase=='test':
  for source in (out/'bin/Release'/tfm).glob('*.dll'):
   canonical=re.sub(r'^\d+-','',source.name)
   if canonical=='Protobuf.dll':canonical='Google.Protobuf.dll'
   if canonical!=source.name:shutil.copy2(source,source.with_name(canonical))
 with (out/(phase+'.log')).open('w') as log:r=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,timeout=120)
 print(phase,r.returncode,flush=True)
 if r.returncode:print((out/(phase+'.log')).read_text()[-5000:]);raise SystemExit(r.returncode)
print((out/'test.log').read_text()[-4500:])
