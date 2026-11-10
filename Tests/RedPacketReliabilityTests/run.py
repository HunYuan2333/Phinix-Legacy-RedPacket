#!/usr/bin/env python3
"""Run against local private references; no network or game server access."""
import argparse, os, re, shutil, subprocess
from pathlib import Path
p = argparse.ArgumentParser()
p.add_argument('--phinix-package', type=Path, required=True)
p.add_argument('--game-references', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parent
subprocess.run(['dotnet', 'build', str(root), '--ignore-failed-sources',
                '-p:PhinixPackage=' + str(a.phinix_package.resolve()),
                '-p:GameReferences=' + str(a.game_references.resolve()), '-v:q'], check=True)
out = root / 'bin/Debug/net472'
# Host distribution names have ordering prefixes; Mono resolves assembly identities.
for source in out.glob('*.dll'):
    name = re.sub(r'^\d+-', '', source.name)
    if name != source.name:
        target = out / name
        if target.is_symlink(): target.unlink()
        shutil.copyfile(source, target)
subprocess.run(['mono', str(out / 'RedPacketReliabilityTests.exe')], check=True)
