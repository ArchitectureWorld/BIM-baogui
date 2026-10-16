"""Read-only spatial evidence for HIFC model standardization; never decide business counts."""
import argparse
import json
import math
import sys
from pathlib import Path

def run(inventory, tolerance_m=0.001):
    from shapely.geometry import Point, Polygon, box
    elements = inventory["elements"]
    areas = []
    for area in (e for e in elements if e["apiType"] == "Autodesk.Revit.DB.Area"):
        shape = Polygon()
        errors = []
        curved = False
        for loop in area["loops"]:
            if not loop:
                errors.append("empty boundary loop")
                continue
            points = []
            for i, segment in enumerate(loop):
                curved |= segment["kind"] != "Line"
                next_start = loop[(i + 1) % len(loop)]["points"][0]
                if math.dist(segment["points"][-1], next_start) > tolerance_m:
                    errors.append("boundary gap")
                points.extend(p[:2] for p in segment["points"][:-1])
            if len(points) < 3:
                errors.append("fewer than three boundary points")
                continue
            ring = Polygon(points)
            if not ring.is_valid or ring.area <= 0:
                errors.append("invalid boundary polygon")
                continue
            shape = shape.symmetric_difference(ring)
        native = area.get("areaM2")
        if shape.is_empty or not shape.is_valid:
            errors.append("missing or invalid area shape")
        if native is None or abs(shape.area - native) > max(0.01, abs(native) * 0.001):
            errors.append("boundary area and native Area differ")
        parking = []
        if not errors:
            for element in (e for e in elements if e["category"] == "停车场"):
                if element["levelId"] != area["levelId"] or not element.get("bbox"):
                    continue
                bounds = element["bbox"]
                footprint = box(*bounds["min"][:2], *bounds["max"][:2])
                if footprint.area <= 0:
                    continue
                ratio = shape.intersection(footprint).area / footprint.area
                if ratio <= 0:
                    continue
                parking.append({
                    "id": element["id"], "uid": element["uid"],
                    "point_inside": shape.covers(Point(element["point"][:2])) if element.get("point") else None,
                    "bbox_inside_with_tolerance": shape.buffer(tolerance_m).covers(footprint),
                    "bbox_overlap_ratio": ratio,
                    "name": element["name"],
                    "native_dimensions": {p["name"]: p["display"] for p in element["typeParameters"] if p["name"] in ("停车场长度", "停车场宽度")},
                })
        areas.append({
            "id": area["id"], "name": area["name"], "level": area["levelName"],
            "native_area_m2": native, "boundary_area_m2": shape.area,
            "curves_tessellated": curved, "errors": errors,
            "overlapping_parking": parking,
            "full_bbox_count": sum(p["bbox_inside_with_tolerance"] for p in parking),
            "partial_bbox_count": sum(not p["bbox_inside_with_tolerance"] for p in parking),
            "business_count": None,
            "note": "BBox and insertion point results are evidence only; inspect images and group membership before assigning a count.",
        })
    return {"coordinate_units": "m", "tolerance_m": tolerance_m, "areas": areas}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("inventory", type=Path)
    parser.add_argument("--deps", type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.deps:
        sys.path.insert(0, str(args.deps.resolve()))
    result = run(json.loads(args.inventory.read_text(encoding="utf-8-sig")))
    content = json.dumps(result, ensure_ascii=False, indent=2)
    if args.out:
        args.out.write_text(content, encoding="utf-8")
    print(content)

if __name__ == "__main__":
    main()
