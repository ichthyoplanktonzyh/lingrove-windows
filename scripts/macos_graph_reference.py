"""Build the unchanged pinned Mac graph package plus a caller-owned observation executable.

Only the manifest receives a driver target. Optional source declarations are original app values
or its complete actor; this module contains no force, cache, epoch or expected-result algorithm.
"""
import hashlib
from pathlib import Path
import subprocess
from macos_vocabulary_reference import BASELINE, source


def build_graph_reference(repo, root, driver, declarations=()):
    root = Path(root); package = 'Packages/LingroveGraphLayout/'
    paths = subprocess.check_output(['git','-C',str(repo),'ls-tree','-r','--name-only',BASELINE,package+'Sources'],text=True).splitlines()
    originals = {p:source(repo,p) for p in paths if p.endswith('.swift')}
    checks = 0
    for line in source(repo,package+'UPSTREAM.md').splitlines():
        if len(line.split()) == 2 and line.split()[1].startswith('Sources/ForceSimulation/'):
            digest, relative = line.split();checks += 1
            if hashlib.sha256(originals[package+relative].encode()).hexdigest() != digest:raise RuntimeError('Original Grape hash mismatch: '+relative)
    if checks != len([p for p in originals if '/ForceSimulation/' in p]):raise RuntimeError('Every original force source must have a checksum')
    for path, text in originals.items():
        target = root/path.removeprefix(package);target.parent.mkdir(parents=True,exist_ok=True);target.write_text(text,encoding='utf-8')
    manifest = source(repo,package+'Package.swift');marker = '.target(name: "LingroveGraphLayout", dependencies: ["ForceSimulation"])'
    if manifest.count(marker) != 1:raise RuntimeError('Pinned package target boundary changed')
    (root/'Package.swift').write_text(manifest.replace(marker,marker+',\n        .executableTarget(name: "GraphParity", dependencies: ["LingroveGraphLayout"])'),encoding='utf-8')
    executable = root/'Sources/GraphParity';executable.mkdir()
    (executable/('Driver.swift' if declarations else 'main.swift')).write_text(driver,encoding='utf-8')
    for i, text in enumerate(declarations):(executable/f'Original{i}.swift').write_text(text,encoding='utf-8')
    result = subprocess.run(['swift','build','--package-path',str(root),'--scratch-path',str(root/'build'),'-c','release','--product','GraphParity'],capture_output=True,text=True)
    if result.returncode:raise RuntimeError(result.stdout+result.stderr)
    return root/'build/release/GraphParity'
