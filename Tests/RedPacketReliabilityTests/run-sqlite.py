#!/usr/bin/env python3
"""Run real red-packet codec + production Inventory ledger/store against SQLite, without a game session."""
import argparse,json,shutil,subprocess,xml.etree.ElementTree as E
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
for key in ['host','graph','game','output']:p.add_argument('--'+key,type=Path,required=True)
a=p.parse_args();plugin=Path(__file__).resolve().parents[2];out=a.output.resolve();graph=a.graph.resolve()
if out.exists() or out.is_relative_to(plugin):p.error('Choose a fresh isolated output directory.')
out.mkdir(parents=True);project=E.Element('Project',Sdk='Microsoft.NET.Sdk');props=E.SubElement(project,'PropertyGroup')
for k,v in {'TargetFramework':'net10.0','OutputType':'Exe','EnableDefaultCompileItems':'false','NuGetAudit':'false','DefineConstants':'SQLITE_TEST','AssemblyName':'RedPacketInventoryRuntimeTests','NoWarn':'CS0436'}.items():E.SubElement(props,k).text=v
items=E.SubElement(project,'ItemGroup')
files=[*Path(__file__).parent.glob('*.cs'),*[a.host.resolve()/'Extensions/Inventory/Client'/name for name in ['InventoryLedger.cs','InventoryDatabaseStore.cs']]]
for file in files:E.SubElement(items,'Compile',Include=str(file.resolve()))
refs={f.stem:f for f in (graph/'infrastructure').glob('*.dll')}
for name in ['Assembly-CSharp','UnityStubs','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule']:
 f=a.game.resolve()/(name+'.dll')
 if f.is_file():refs[name]=f
for name,f in refs.items():node=E.SubElement(items,'Reference',Include=name);E.SubElement(node,'HintPath').text=str(f)
E.ElementTree(project).write(out/'Probe.csproj',encoding='unicode');(out/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
results=[]
for phase,cmd in [('build',['dotnet','build',str(out/'Probe.csproj'),'-c','Release','-v:q']),('test',['dotnet',str(out/'bin/Release/net10.0/RedPacketInventoryRuntimeTests.dll')])]:
 if phase=='test':
  for relative,entry in json.loads((graph/'native-assets.json').read_text()).items():
   target=out/'bin/Release/net10.0'/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(entry['source'],target)
 with (out/(phase+'.log')).open('w') as log:r=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT,timeout=180)
 results.append({'phase':phase,'exit':r.returncode});print(phase,r.returncode,flush=True)
 if r.returncode:print((out/(phase+'.log')).read_text()[-5000:]);break
(out/'results.json').write_text(json.dumps(results,indent=2)+'\n')
raise SystemExit(0 if len(results)==2 and all(row['exit']==0 for row in results) else 1)
