#!/usr/bin/env python3
"""Build an API3 dual-runtime local package; never reserve or publish a release."""
import argparse,json,subprocess
from pathlib import Path
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--phinix-root',type=Path,required=True)
p.add_argument('--game-references',type=Path,required=True,help='1.6 compile references')
p.add_argument('--game17-references',type=Path,required=True)
p.add_argument('--harmony17',type=Path,required=True)
p.add_argument('--version',required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--work',type=Path)
a=p.parse_args();root=Path(__file__).resolve().parent
# Both variants have one public API range >=3.0.0 <4.0.0 and compile-client-api 3.0.0.
cmd=['python3',str(a.phinix_root.resolve()/'tools/compatibility/build-managed-dual.py'),'--host',str(a.phinix_root.resolve()),'--plugin',str(root),'--game16',str(a.game_references.resolve()),'--game17',str(a.game17_references.resolve()),'--harmony17',str(a.harmony17.resolve()),'--version',a.version,'--output',str(a.output.resolve())]
if a.work:cmd+=['--work',str(a.work.resolve())]
subprocess.run(cmd,check=True)
