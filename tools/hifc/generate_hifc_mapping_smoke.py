#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
import uuid
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Mapping, Sequence


ROOT = Path(__file__).resolve().parents[2]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools.build_hbr_rulepack import (  # noqa: E402
    canonical_source_sha256,
    effective_ifc_identity,
    load_validated_rule_source,
)


GENERATOR_VERSION = "1.0.0"
FIXED_FILE_TIMESTAMP = "2026-08-07T18:00:00+08:00"
IFCFLUX_B_SHA256 = (
    "570f5a554478535cb13638549b89f596d749be3ca4c66392de22f5617254c632"
)
IFC_GUID_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$"
ENTITY_RE = re.compile(r"^#(?P<id>\d+)=(?P<type>[A-Z0-9_]+)\((?P<args>.*)\);$")


@dataclass(frozen=True)
class FixtureSummary:
    step_entities: int
    properties: int
    property_sets: int
    attachments: int
    owner_types: Sequence[str]
    extruded_solids: int


class StepIdAllocator:
    def __init__(self) -> None:
        self._next_id = 1

    def allocate(self) -> int:
        entity_id = self._next_id
        self._next_id += 1
        return entity_id


def canonical_json_bytes(document: object) -> bytes:
    return (
        json.dumps(
            document,
            ensure_ascii=False,
            indent=2,
            sort_keys=False,
        ).rstrip("\n")
        + "\n"
    ).encode("utf-8")


def repository_root(start: Path | str = __file__) -> Path:
    candidate = Path(start).resolve()
    if candidate.is_file():
        candidate = candidate.parent
    for directory in (candidate, *candidate.parents):
        marker = directory / ".git"
        if marker.is_dir() or marker.is_file():
            return directory
    raise ValueError(f"repository root not found from {candidate}")


def atomic_replace_bytes(path: Path | str, payload: bytes) -> None:
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        dir=str(destination.parent),
        prefix=f".{destination.name}.",
        suffix=".tmp",
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, destination)
    except BaseException:
        try:
            os.unlink(temporary_name)
        except FileNotFoundError:
            pass
        raise


def _ifc_string(value: str) -> str:
    if any(ord(character) > 0x7F for character in value):
        encoded = value.encode("utf-16-be").hex().upper()
        return f"'\\X2\\{encoded}\\X0\\'"
    return "'" + value.replace("'", "''") + "'"


def _ifc_guid(value: uuid.UUID) -> str:
    number = value.int
    characters = ["0"] * 22
    for index in range(21, -1, -1):
        number, remainder = divmod(number, 64)
        characters[index] = IFC_GUID_ALPHABET[remainder]
    return "".join(characters)


def _typed_sample(rule: Mapping[str, object], property_name: str) -> str:
    declared_type = str(rule["ifc"]["declaredType"]).upper()
    property_id = str(rule["propertyId"])
    if property_id == "6b407894-09d4-529a-9f9f-a031219cdeaa":
        return "IFCREAL(3353559.52)"
    if property_id == "1a64ef8d-e97c-5fa1-b53f-52b969b6198a":
        return "IFCREAL(38345264.397)"
    if declared_type == "IFCBOOLEAN":
        value = ".T."
    elif declared_type == "IFCINTEGER":
        value = "1"
    elif declared_type == "IFCREAL":
        value = "1.0"
    elif declared_type == "IFCDATE":
        value = _ifc_string("2026-08-07")
    elif declared_type == "IFCDATETIME":
        value = _ifc_string(FIXED_FILE_TIMESTAMP)
    elif declared_type in {"IFCLABEL", "IFCTEXT"}:
        value = _ifc_string(f"HBR验证：{property_name}")
    else:
        raise ValueError(f"unsupported IFC declared type: {declared_type}")
    return f"{declared_type}({value})"


class IfcFixtureDocument:
    def __init__(self, source: Mapping[str, object]) -> None:
        self.source = source
        self.namespace = uuid.UUID(str(source["guidNamespace"]))
        self.ids = StepIdAllocator()
        self.lines: list[str] = []
        self.owners: dict[str, int] = {}
        self.owner_history = 0
        self.body_context = 0
        self._spatial_relationships_requested = False

    def _add(self, entity_type: str, arguments: str) -> int:
        entity_id = self.ids.allocate()
        self.lines.append(f"#{entity_id}={entity_type}({arguments});")
        return entity_id

    def _guid(self, seed: str) -> str:
        return _ifc_guid(uuid.uuid5(self.namespace, seed))

    def add_owner_scaffold(self) -> None:
        organization = self._add("IFCORGANIZATION", "$,'ArchitectureWorld',$,$,$")
        application = self._add(
            "IFCAPPLICATION", f"#{organization},'1.0.0','BIMBaoGui HBR IFC4 Writer','BIMBaoGui'"
        )
        person = self._add("IFCPERSON", "$,'HBR','Fixture',$,$,$,$,$")
        person_org = self._add(
            "IFCPERSONANDORGANIZATION", f"#{person},#{organization},$"
        )
        self.owner_history = self._add(
            "IFCOWNERHISTORY",
            f"#{person_org},#{application},$,.ADDED.,$,$,$,1786106400",
        )
        length = self._add("IFCSIUNIT", "*,.LENGTHUNIT.,.MILLI.,.METRE.")
        area = self._add("IFCSIUNIT", "*,.AREAUNIT.,$,.SQUARE_METRE.")
        volume = self._add("IFCSIUNIT", "*,.VOLUMEUNIT.,$,.CUBIC_METRE.")
        angle = self._add("IFCSIUNIT", "*,.PLANEANGLEUNIT.,$,.RADIAN.")
        units = self._add(
            "IFCUNITASSIGNMENT", f"(#{length},#{area},#{volume},#{angle})"
        )
        origin = self._add("IFCCARTESIANPOINT", "(0.0,0.0,0.0)")
        world = self._add("IFCAXIS2PLACEMENT3D", f"#{origin},$,$")
        north = self._add("IFCDIRECTION", "(0.0,1.0)")
        context = self._add(
            "IFCGEOMETRICREPRESENTATIONCONTEXT",
            f"$,'Model',3,0.01,#{world},#{north}",
        )
        self.body_context = self._add(
            "IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
            f"'Body','Model',*,*,*,*,#{context},$,.MODEL_VIEW.,$",
        )
        site_place = self._placement(None, "0.0,0.0,0.0")
        building_place = self._placement(site_place, "0.0,0.0,0.0")
        storey_place = self._placement(building_place, "0.0,0.0,0.0")
        project = self._add(
            "IFCPROJECT",
            f"{_ifc_string(self._guid('owner:IfcProject'))},#{self.owner_history},"
            f"{_ifc_string('HBR-HIFC全映射验证项目')},$,'HBR_PLANNING_SUBMISSION',"
            f"'HBR-HIFC Mapping Smoke',{_ifc_string('规划报建')},(#{context}),#{units}",
        )
        site = self._add(
            "IFCSITE",
            f"{_ifc_string(self._guid('owner:IfcSite'))},#{self.owner_history},'SITE-001',"
            f"{_ifc_string('验证场地')},$,#{site_place},$,{_ifc_string('验证场地')},"
            ".ELEMENT.,$,$,25.8,'LAND-001',$",
        )
        building = self._add(
            "IFCBUILDING",
            f"{_ifc_string(self._guid('owner:IfcBuilding'))},#{self.owner_history},'BUILDING-001',"
            f"{_ifc_string('验证建筑')},$,#{building_place},$,{_ifc_string('验证建筑')},"
            ".ELEMENT.,0.0,25.8,$",
        )
        storey = self._add(
            "IFCBUILDINGSTOREY",
            f"{_ifc_string(self._guid('owner:IfcBuildingStorey'))},#{self.owner_history},'1F',"
            f"{_ifc_string('验证楼层')},$,#{storey_place},$,{_ifc_string('验证楼层')},.ELEMENT.,0.0",
        )
        self.owners.update(
            IfcProject=project,
            IfcSite=site,
            IfcBuilding=building,
            IfcBuildingStorey=storey,
        )
        self.storey_placement = storey_place

    def _placement(self, relative_to: int | None, coordinates: str) -> int:
        point = self._add("IFCCARTESIANPOINT", f"({coordinates})")
        axis = self._add("IFCAXIS2PLACEMENT3D", f"#{point},$,$")
        parent = "$" if relative_to is None else f"#{relative_to}"
        return self._add("IFCLOCALPLACEMENT", f"{parent},#{axis}")

    def _visible_shape(self, coordinates: str, x: float, y: float, depth: float) -> tuple[int, int]:
        placement = self._placement(self.storey_placement, coordinates)
        profile_point = self._add("IFCCARTESIANPOINT", "(0.0,0.0)")
        profile_axis = self._add("IFCAXIS2PLACEMENT2D", f"#{profile_point},$")
        profile = self._add(
            "IFCRECTANGLEPROFILEDEF", f".AREA.,$,#{profile_axis},{x:.1f},{y:.1f}"
        )
        solid_point = self._add("IFCCARTESIANPOINT", "(0.0,0.0,0.0)")
        solid_axis = self._add("IFCAXIS2PLACEMENT3D", f"#{solid_point},$,$")
        direction = self._add("IFCDIRECTION", "(0.0,0.0,1.0)")
        solid = self._add(
            "IFCEXTRUDEDAREASOLID", f"#{profile},#{solid_axis},#{direction},{depth:.1f}"
        )
        representation = self._add(
            "IFCSHAPEREPRESENTATION",
            f"#{self.body_context},'Body','SweptSolid',(#{solid})",
        )
        shape = self._add("IFCPRODUCTDEFINITIONSHAPE", f"$,$,(#{representation})")
        return placement, shape

    def add_visible_geometry(self) -> None:
        specifications = (
            ("IfcSpace", "IFCSPACE", "1000.0,1000.0,200.0", 8000, 6000, 2800),
            ("IfcSpatialZone", "IFCSPATIALZONE", "1000.0,1000.0,210.0", 8000, 6000, 20),
            ("IfcWall", "IFCWALL", "0.0,0.0,200.0", 10000, 200, 3000),
            ("IfcSlab", "IFCSLAB", "0.0,0.0,0.0", 10000, 8000, 200),
            ("IfcRoof", "IFCROOF", "0.0,0.0,3200.0", 10000, 8000, 200),
            ("IfcWindow", "IFCWINDOW", "4000.0,0.0,1000.0", 1200, 200, 1500),
            ("IfcStairFlight", "IFCSTAIRFLIGHT", "6500.0,4000.0,200.0", 2000, 3000, 1500),
            ("IfcDoor", "IFCDOOR", "1000.0,0.0,200.0", 900, 200, 2100),
            ("IfcDuctSegment", "IFCDUCTSEGMENT", "8500.0,6500.0,2500.0", 500, 500, 3000),
        )
        for name, entity_type, coordinates, x, y, depth in specifications:
            placement, shape = self._visible_shape(coordinates, x, y, depth)
            prefix = (
                f"{_ifc_string(self._guid('owner:' + name))},#{self.owner_history},"
                f"{_ifc_string(name + '-001')},{_ifc_string('HBR验证：' + name)},$,"
                f"#{placement},#{shape},{_ifc_string(name + '-TAG')}"
            )
            suffixes = {
                "IFCSPACE": ",.ELEMENT.,.INTERNAL.,200.0",
                "IFCSPATIALZONE": ",.USERDEFINED.",
                "IFCWALL": ",.NOTDEFINED.",
                "IFCSLAB": ",.FLOOR.",
                "IFCROOF": ",.NOTDEFINED.",
                "IFCWINDOW": ",1500.0,1200.0,.WINDOW.,.NOTDEFINED.,$",
                "IFCSTAIRFLIGHT": ",$,$,$,$,.NOTDEFINED.",
                "IFCDOOR": ",2100.0,900.0,.DOOR.,.NOTDEFINED.,$",
                "IFCDUCTSEGMENT": ",.NOTDEFINED.",
            }
            self.owners[name] = self._add(entity_type, prefix + suffixes[entity_type])
        organization = self._add(
            "IFCORGANIZATION",
            f"'ORG-001',{_ifc_string('武汉规划验证组织')},{_ifc_string('HBR-HIFC验证组织')},$,$",
        )
        actor = self._add(
            "IFCACTOR",
            f"{_ifc_string(self._guid('owner:IfcActor'))},#{self.owner_history},'ORG-ACTOR-001',"
            f"{_ifc_string('组织属性承载对象')},'DESIGN_ORGANIZATION',#{organization}",
        )
        self.owners["IfcActor"] = actor
        self.owners["IfcOrganization"] = actor
        if self._spatial_relationships_requested:
            self._emit_spatial_relationships()

    def add_spatial_relationships(self) -> None:
        # Relationships depend on the visible owner ids. Record the stage here;
        # add_visible_geometry emits them after those deterministic ids exist.
        self._spatial_relationships_requested = True

    def _emit_spatial_relationships(self) -> None:
        relationships = (
            ("aggregate:project-site", "IFCRELAGGREGATES", f"#{self.owners['IfcProject']},(#{self.owners['IfcSite']})"),
            ("aggregate:site-building", "IFCRELAGGREGATES", f"#{self.owners['IfcSite']},(#{self.owners['IfcBuilding']})"),
            ("aggregate:building-storey", "IFCRELAGGREGATES", f"#{self.owners['IfcBuilding']},(#{self.owners['IfcBuildingStorey']})"),
            ("aggregate:storey-space", "IFCRELAGGREGATES", f"#{self.owners['IfcBuildingStorey']},(#{self.owners['IfcSpace']})"),
        )
        for seed, entity_type, tail in relationships:
            self._add(
                entity_type,
                f"{_ifc_string(self._guid(seed))},#{self.owner_history},$,$,{tail}",
            )
        products = ",".join(
            f"#{self.owners[name]}"
            for name in ("IfcWall", "IfcSlab", "IfcRoof", "IfcWindow", "IfcStairFlight", "IfcDoor", "IfcDuctSegment")
        )
        self._add(
            "IFCRELCONTAINEDINSPATIALSTRUCTURE",
            f"{_ifc_string(self._guid('containment:storey'))},#{self.owner_history},$,$,({products}),"
            f"#{self.owners['IfcBuildingStorey']}",
        )
        self._add(
            "IFCRELREFERENCEDINSPATIALSTRUCTURE",
            f"{_ifc_string(self._guid('reference:zone-storey'))},#{self.owner_history},$,$,"
            f"(#{self.owners['IfcSpatialZone']}),#{self.owners['IfcBuildingStorey']}",
        )

    def add_rule_properties(self) -> None:
        groups: dict[tuple[str, str], list[int]] = {}
        for rule in self.source["properties"]:
            owner_type, property_set, property_name = effective_ifc_identity(rule)
            attachment_owner = "IfcActor" if owner_type == "IfcOrganization" else owner_type
            property_id = self._add(
                "IFCPROPERTYSINGLEVALUE",
                f"{_ifc_string(property_name)},$,{_typed_sample(rule, property_name)},$",
            )
            groups.setdefault((attachment_owner, property_set), []).append(property_id)
        for owner_type, property_set in sorted(
            groups, key=lambda item: (item[0].encode("utf-8"), item[1].encode("utf-8"))
        ):
            property_refs = ",".join(f"#{entity_id}" for entity_id in groups[(owner_type, property_set)])
            pset = self._add(
                "IFCPROPERTYSET",
                f"{_ifc_string(self._guid('pset:' + owner_type + '|' + property_set))},"
                f"#{self.owner_history},{_ifc_string(property_set)},$,({property_refs})",
            )
            self._add(
                "IFCRELDEFINESBYPROPERTIES",
                f"{_ifc_string(self._guid('attachment:' + owner_type + '|' + property_set))},"
                f"#{self.owner_history},$,$,(#{self.owners[owner_type]}),#{pset}",
            )

    def to_bytes(self) -> bytes:
        header = (
            "ISO-10303-21;\nHEADER;\n"
            "FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]',"
            "'HBR-HIFC Canonical Mapping Smoke Test v1.0'),'2;1');\n"
            f"FILE_NAME({_ifc_string('HBR_HIFC_全映射结构验证_v1.0.ifc')},"
            f"'{FIXED_FILE_TIMESTAMP}',('ArchitectureWorld'),('ArchitectureWorld'),"
            "'HBR IFC4 Writer 1.0.0','BIMBaoGui','');\n"
            "FILE_SCHEMA(('IFC4'));\nENDSEC;\n\nDATA;\n"
        )
        return (header + "\n".join(self.lines) + "\nENDSEC;\nEND-ISO-10303-21;\n").encode("utf-8")


def summarize_fixture(ifc_bytes: bytes) -> FixtureSummary:
    text = ifc_bytes.decode("utf-8")
    entities: dict[int, tuple[str, str]] = {}
    for line in text.splitlines():
        match = ENTITY_RE.match(line)
        if match:
            entities[int(match.group("id"))] = (match.group("type"), match.group("args"))
    if list(entities) != list(range(1, len(entities) + 1)):
        raise ValueError("STEP entity ids must be strictly monotonic")
    owner_types = set()
    for entity_type, arguments in entities.values():
        if entity_type != "IFCRELDEFINESBYPROPERTIES":
            continue
        match = re.search(r",\(#(\d+)\),#\d+$", arguments)
        if match:
            owner_types.add(entities[int(match.group(1))][0])
    display_names = {
        "IFCACTOR": "IfcActor",
        "IFCBUILDING": "IfcBuilding",
        "IFCBUILDINGSTOREY": "IfcBuildingStorey",
        "IFCDOOR": "IfcDoor",
        "IFCDUCTSEGMENT": "IfcDuctSegment",
        "IFCPROJECT": "IfcProject",
        "IFCROOF": "IfcRoof",
        "IFCSITE": "IfcSite",
        "IFCSLAB": "IfcSlab",
        "IFCSPACE": "IfcSpace",
        "IFCSPATIALZONE": "IfcSpatialZone",
        "IFCSTAIRFLIGHT": "IfcStairFlight",
        "IFCWALL": "IfcWall",
        "IFCWINDOW": "IfcWindow",
    }
    counts: dict[str, int] = {}
    for entity_type, _ in entities.values():
        counts[entity_type] = counts.get(entity_type, 0) + 1
    return FixtureSummary(
        step_entities=len(entities),
        properties=counts.get("IFCPROPERTYSINGLEVALUE", 0),
        property_sets=counts.get("IFCPROPERTYSET", 0),
        attachments=counts.get("IFCRELDEFINESBYPROPERTIES", 0),
        owner_types=tuple(sorted(display_names[name] for name in owner_types)),
        extruded_solids=counts.get("IFCEXTRUDEDAREASOLID", 0),
    )


def build_ifc_bytes(source: Mapping[str, object]) -> tuple[bytes, FixtureSummary]:
    document = IfcFixtureDocument(source)
    document.add_owner_scaffold()
    document.add_spatial_relationships()
    document.add_visible_geometry()
    document.add_rule_properties()
    ifc_bytes = document.to_bytes()
    return ifc_bytes, summarize_fixture(ifc_bytes)


def _portable_path(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.name


def fixture_manifest_document(
    root: Path,
    source_path: Path,
    baseline_path: Path,
    generator_path: Path,
    ifc_path: Path,
    ifc_bytes: bytes,
    source: Mapping[str, object],
    summary: FixtureSummary,
) -> dict[str, object]:
    return {
        "schemaVersion": "1.0.0",
        "fixtureId": "HBR-HIFC-FULL-MAPPING-V1",
        "generator": {
            "path": _portable_path(root, generator_path),
            "version": GENERATOR_VERSION,
            "sha256": hashlib.sha256(generator_path.read_bytes()).hexdigest(),
        },
        "source": {
            "path": _portable_path(root, source_path),
            "sha256": hashlib.sha256(source_path.read_bytes()).hexdigest(),
            "canonicalSha256": canonical_source_sha256(source),
            "compatibilityBaselinePath": _portable_path(root, baseline_path),
            "compatibilityBaselineSha256": hashlib.sha256(
                baseline_path.read_bytes()
            ).hexdigest(),
            "packageId": source["packageId"],
            "packageVersion": source["packageVersion"],
        },
        "fixture": {
            "path": _portable_path(root, ifc_path),
            "sha256": hashlib.sha256(ifc_bytes).hexdigest(),
            "bytes": len(ifc_bytes),
            "encoding": "UTF-8",
            "lineEnding": "LF",
            "schema": "IFC4",
            "viewDefinition": "ReferenceView_V1.2",
        },
        "summary": {
            "stepEntities": summary.step_entities,
            "properties": summary.properties,
            "propertySets": summary.property_sets,
            "attachments": summary.attachments,
            "ownerTypes": list(summary.owner_types),
            "extrudedSolids": summary.extruded_solids,
        },
        "policies": {
            "valueProfile": "STRUCTURAL_SMOKE_V1",
            "booleanSample": "ALWAYS_TRUE_FOR_IFCFLUX_SMOKE",
        },
    }


def build_fixture_manifest(
    root: Path,
    source_path: Path,
    baseline_path: Path,
    generator_path: Path,
    ifc_path: Path,
    ifc_bytes: bytes,
    source: Mapping[str, object],
    summary: FixtureSummary,
) -> bytes:
    return canonical_json_bytes(
        fixture_manifest_document(
            root,
            source_path,
            baseline_path,
            generator_path,
            ifc_path,
            ifc_bytes,
            source,
            summary,
        )
    )


def _same_path(first: Path, second: Path) -> bool:
    if first.resolve(strict=False) == second.resolve(strict=False):
        return True
    if first.exists() and second.exists():
        try:
            return os.path.samefile(first, second)
        except OSError:
            return False
    return False


def generate_fixture(
    source_path: Path | str,
    baseline_path: Path | str,
    output_path: Path | str,
    fixture_manifest_path: Path | str,
) -> FixtureSummary:
    paths = tuple(
        Path(path)
        for path in (source_path, baseline_path, output_path, fixture_manifest_path)
    )
    for index, first in enumerate(paths):
        for second in paths[index + 1 :]:
            if _same_path(first, second):
                raise ValueError("source, baseline, output, and manifest paths must be distinct")
    source_file, baseline_file, output_file, manifest_file = paths
    source = load_validated_rule_source(source_file, baseline_file)
    ifc_bytes, summary = build_ifc_bytes(source)
    root = repository_root(Path(__file__))
    manifest_bytes = build_fixture_manifest(
        root,
        source_file,
        baseline_file,
        Path(__file__),
        output_file,
        ifc_bytes,
        source,
        summary,
    )
    atomic_replace_bytes(output_file, ifc_bytes)
    atomic_replace_bytes(manifest_file, manifest_bytes)
    return summary


def _argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the deterministic HBR HIFC mapping fixture.")
    parser.add_argument("--source", required=True)
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--manifest", required=True)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _argument_parser().parse_args(argv)
    try:
        summary = generate_fixture(
            arguments.source,
            arguments.baseline,
            arguments.output,
            arguments.manifest,
        )
    except (OSError, UnicodeError, ValueError) as error:
        print(f"HIFC fixture generation failed: {error}", file=sys.stderr)
        return 1
    print(canonical_json_bytes(asdict(summary)).decode("utf-8"), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
