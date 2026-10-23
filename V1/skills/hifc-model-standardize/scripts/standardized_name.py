"""Plan standardized RVT names without opening, creating, or modifying any model.

Filename identity is a hint; a Revit scan must confirm stage/type and model identity.
The ifc_stem retains the existing IFC naming policy, independently of RVT naming.
"""
import argparse
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import re


_INVALID = re.compile(r'[<>:"/\\|?*\x00-\x1f]')
_RESERVED = re.compile(r"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", re.I)
_STANDARD = re.compile(
    r"^(?P<prefix>3D|标准化)_(?P<project>.+)_报规_(?P<subject>.+)_"
    r"(?P<date>[0-9]{8})_V(?P<version>[1-9][0-9]*)$", re.I)


def _component(value):
    if (not value or value != value.strip() or value.endswith(".")
            or _INVALID.search(value) or _RESERVED.match(value)
            or "_报规_" in value):
        raise ValueError("项目名或模型名称含空白边界、保留字或非法字符：" + repr(value))
    return value


def _day(value):
    if not re.fullmatch(r"[0-9]{8}", value):
        raise ValueError("日期必须为 YYYYMMDD")
    datetime.strptime(value, "%Y%m%d")
    return value


def _identity(source):
    stem = source.stem
    if stem.startswith("标准化_") or (stem[:3].lower() == "3d_" and ("_报规_" in stem or "_施工图_" in stem)):
        match = _STANDARD.fullmatch(stem)
        if not match:
            raise ValueError("标准化文件名无效或阶段不在本技能的报规范围内")
        result = match.groupdict()
        _component(result["project"])
        _component(result["subject"])
        _day(result["date"])
        result["version"] = int(result["version"])
        return result
    if stem[:3].lower() == "3d_":
        stem = stem[3:]
    if "_" not in stem:
        return None
    project, subject = stem.rsplit("_", 1)
    # A dated legacy name is not an unambiguous project/building pair.
    if (re.fullmatch(r"[0-9]+|V[0-9]+", subject, re.I)
            or "_报规_" in stem or "_施工图_" in stem):
        return None
    return {"project": project, "subject": subject}


def plan(source, *, project=None, subject=None, directory=None, day=None,
         new_version=False):
    source = Path(source).resolve(strict=True)
    if not source.is_file() or source.suffix.lower() != ".rvt":
        raise ValueError("来源必须是存在的 RVT 文件；未保存模型先在 Revit 中确定来源")
    day = _day(day or datetime.now(timezone(timedelta(hours=8))).strftime("%Y%m%d"))
    destination = Path(directory).resolve(strict=True) if directory else source.parent
    if not destination.is_dir():
        raise ValueError("目标文件夹必须已经存在")
    identity = _identity(source)
    project = project if project is not None else (identity or {}).get("project")
    subject = subject if subject is not None else (identity or {}).get("subject")
    if project is None or subject is None:
        raise ValueError("文件名不能可靠拆出项目与模型名称；请用 --project 和 --subject 提供已确认信息")
    _component(project)
    _component(subject)
    if subject in ("单体", "单体建筑"):
        raise ValueError("请填写综合楼、大门等具体单体名称，或总图")
    standardized = identity is not None and "version" in identity
    same_identity = (identity is not None and identity["project"] == project
                     and identity["subject"] == subject)
    if standardized and not same_identity and not new_version:
        raise ValueError("更换已标准化文件的项目或楼栋身份时，请明确使用 --new-version")
    if (standardized and identity["prefix"].lower() == "3d" and same_identity
            and destination == source.parent and not new_version):
        target = source
        version = identity["version"]
        rvt_day = identity["date"]
        action = "reuse"
    else:
        stem = f"3D_{project}_报规_{subject}_{day}"
        pattern = re.compile(re.escape(stem) + r"_V([1-9][0-9]*)\.rvt$", re.I)
        versions = [int(match.group(1)) for entry in destination.iterdir()
                    if (match := pattern.fullmatch(entry.name))]
        version = max(versions, default=0) + 1
        target = destination / f"{stem}_V{version}.rvt"
        rvt_day = day
        action = "save_as"
    if len(target.name) > 255:
        raise ValueError("标准化文件名超过 255 个字符，请缩短已确认的项目或模型名称")
    site = subject == "总图"
    return {
        "source": str(source), "target": str(target), "action": action,
        "project": project, "subject": subject, "stage": "报规",
        "filename_kind_hint": "总图" if site else "单体建筑",
        "date": rvt_day, "version": version,
        "ifc_stem": f"OUT_{project}_报规_{subject}_{day}",
        "writes_files": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, help="存在的原始或标准化 RVT 路径")
    parser.add_argument("--project", help="已确认的项目名称；用于无法可靠拆分的旧文件名")
    parser.add_argument("--subject", help="具体单体名称或总图，不填写泛称单体")
    parser.add_argument("--directory", help="已有的目标目录；默认与来源同级")
    parser.add_argument("--date", help="YYYYMMDD；省略时使用 Asia/Shanghai 当日日期")
    parser.add_argument("--new-version", action="store_true", help="从标准化成果另存新版本，而非继续使用该文件")
    args = parser.parse_args()
    try:
        result = plan(args.source, project=args.project, subject=args.subject,
                      directory=args.directory, day=args.date, new_version=args.new_version)
    except (OSError, ValueError) as error:
        parser.error(str(error))
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
