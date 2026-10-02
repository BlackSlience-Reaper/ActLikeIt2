#!/usr/bin/env python3
"""从同一检出依次构建两个变体与稳定加载器，复制一份已经导出的 PCK。"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile

TARGETS = ("0.107.1", "0.111.0")
root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--pck", type=Path, required=True, help="与当前源码资源对应的共享 PCK")
parser.add_argument("--refs", type=Path, default=Path.home() / "sts2-mods/HextechRunes/versioned-dll-backups")
args = parser.parse_args()
manifest_path = next(p for p in root.glob("*.json") if p.stem in ("LibraryOfRuina", "ActLikeIt2", "LibraryOfRuinaLib"))
manifest = json.loads(manifest_path.read_text())
mod_id = manifest["id"]
project = root / ("LibraryLib.csproj" if mod_id == "LibraryOfRuinaLib" else mod_id + ".csproj")
output = root / "build/dual-release" / mod_id
args.pck = args.pck.resolve()
if not args.pck.is_file():
    parser.error("PCK 不存在：" + str(args.pck))
if output in args.pck.parents:
    parser.error("PCK 输入不能位于会重建的发行目录内")
with args.pck.open("rb") as stream:
    magic, pack_format, major, minor, patch = struct.unpack("<4sIIII", stream.read(20))
if magic != b"GDPC" or (major, minor, patch) > (4, 5, 1):
    parser.error("共享 PCK 必须由 Godot 4.5.1 或更早的兼容版本导出")

def properties(target):
    return ["-p:CompatibilityTarget=" + target, "-p:GameRefsRoot=" + str(args.refs.resolve())]

def build(proj, target):
    subprocess.run(["dotnet", "build", str(proj), "-c", "Release", "--nologo", *properties(target)], check=True, cwd=root)
    path = subprocess.check_output(["dotnet", "msbuild", str(proj), "-nologo", "-getProperty:TargetPath", "-p:Configuration=Release", *properties(target)], text=True, cwd=root).strip()
    return Path(path)

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

output.parent.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix="dual-stage-", dir=output.parent) as temp:
    stage = Path(temp) / mod_id
    stage.mkdir()
    variants = []
    for target in TARGETS:
        if mod_id == "LibraryOfRuina":
            # 当前本地依赖检出由维护者准备，构建过程不移动分支或覆盖文件。
            for dependency in [root / "build/LibraryOfRuinaLib/LibraryLib.csproj", root / "build/ActLikeIt2-src/ActLikeIt2.csproj"]:
                build(dependency, target)
        dll = build(project, target)
        directory = stage / "lib" / target
        directory.mkdir(parents=True)
        dest = directory / (mod_id + ".dll")
        shutil.copy2(dll, dest)
        (directory / "compat-target.txt").write_text(target + "\n")
        variants.append({"compatTarget": target, "directory": "lib/" + target, "assembly": dest.name, "sha256": sha(dest)})
    loader = build(root / "loader" / (mod_id + ".Loader.csproj"), "0.107.1")
    shutil.copy2(loader, stage / (mod_id + ".dll"))
    shutil.copy2(manifest_path, stage / manifest_path.name)
    shutil.copy2(args.pck, stage / (mod_id + ".pck"))
    (stage / (mod_id.lower() + "-variants.manifest")).write_text(json.dumps({"variants": variants}, indent=2) + "\n")
    evidence = {"sourceCommit": subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip(), "sourceDirty": bool(subprocess.check_output(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=no"], text=True).strip()), "files": {str(p.relative_to(stage)): sha(p) for p in sorted(stage.rglob("*")) if p.is_file()}}
    (stage / "bundle-evidence.json").write_text(json.dumps(evidence, indent=2) + "\n")
    if output.exists():
        if not (output / "bundle-evidence.json").is_file():
            raise RuntimeError("拒绝覆盖非本工具生成的目录：" + str(output))
        shutil.rmtree(output)
    shutil.move(str(stage), str(output))
print("发行目录：", output)
