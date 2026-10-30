"""Export the project's own static beetle FBX without an external engine or package.

Run: python Model/export_fbx.py ../Assets/bug.fbx Assets/beetle.mesh.json
Supports the FBX 7.4 binary nodes and zlib arrays used by this source asset.
"""
import json
import hashlib
import pathlib
import struct
import sys
import zlib


def read_fbx(path):
    data = pathlib.Path(path).read_bytes()
    if not data.startswith(b"Kaydara FBX Binary  \x00\x1a\x00"):
        raise ValueError("Expected a binary FBX file")
    version = struct.unpack_from("<I", data, 23)[0]
    large = version >= 7500
    header = "<QQQB" if large else "<IIIB"
    size = struct.calcsize(header)

    def prop(offset):
        kind = chr(data[offset])
        offset += 1
        scalar = {"Y": "h", "C": "?", "I": "i", "F": "f", "D": "d", "L": "q"}
        if kind in scalar:
            fmt = "<" + scalar[kind]
            return struct.unpack_from(fmt, data, offset)[0], offset + struct.calcsize(fmt)
        if kind in "SR":
            length = struct.unpack_from("<I", data, offset)[0]
            value = data[offset + 4:offset + 4 + length]
            if kind == "S":
                value = value.decode("utf-8", "replace")
            return value, offset + 4 + length
        if kind in "fdlibc":
            length, encoding, packed = struct.unpack_from("<III", data, offset)
            offset += 12
            raw = data[offset:offset + packed]
            if encoding:
                raw = zlib.decompress(raw)
            fmt = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "?", "c": "b"}[kind]
            return list(struct.unpack("<" + str(length) + fmt, raw)), offset + packed
        raise ValueError("Unknown FBX property " + kind)

    def node(offset):
        end, count, properties_length, name_length = struct.unpack_from(header, data, offset)
        if not end:
            return None, offset + size
        offset += size
        name = data[offset:offset + name_length].decode("utf-8")
        offset += name_length
        properties = []
        for _ in range(count):
            value, offset = prop(offset)
            properties.append(value)
        children = []
        while offset + size <= end:
            child, next_offset = node(offset)
            if child is None:
                break
            children.append(child)
            offset = next_offset
        return {"name": name, "props": properties, "children": children}, end

    roots = []
    offset = 27
    while offset < len(data):
        root, offset = node(offset)
        if root is None:
            break
        roots.append(root)
    return version, roots


def child(node, name):
    return next((entry for entry in node["children"] if entry["name"] == name), None)


def value(node, name, default=None):
    entry = child(node, name)
    return entry["props"][0] if entry and entry["props"] else default


def inspect(path):
    version, roots = read_fbx(path)
    objects = next(entry for entry in roots if entry["name"] == "Objects")
    print("FBX version", version)
    for entry in objects["children"]:
        if entry["name"] == "Geometry":
            vertices = value(entry, "Vertices")
            print(entry["props"], "vertices", len(vertices) // 3,
                  "bounds", [(min(vertices[i::3]), max(vertices[i::3])) for i in range(3)])
            for layer in entry["children"]:
                if layer["name"].startswith("LayerElement"):
                    print(" ", layer["name"], [(c["name"], len(c["props"][0]) if c["props"] and isinstance(c["props"][0], list) else c["props"]) for c in layer["children"]])
        elif entry["name"] == "Model":
            print("Model", entry["props"], child(entry, "Properties70"))


def export(source, destination):
    version, roots = read_fbx(source)
    objects = next(entry for entry in roots if entry["name"] == "Objects")
    result = {"sourceSha256": hashlib.sha256(pathlib.Path(source).read_bytes()).hexdigest(),
              "fbxVersion": version, "axes": "X = -source Z; Y = source Y - 0.5; Z = source X (dorsal)", "parts": []}
    for entry in objects["children"]:
        if entry["name"] != "Geometry":
            continue
        part_id = int(entry["props"][1].split("\x00")[0].rsplit("_", 1)[1])
        positions = value(entry, "Vertices")
        polygons = value(entry, "PolygonVertexIndex")
        normal_layer = child(entry, "LayerElementNormal")
        uv_layer = child(entry, "LayerElementUV")
        if value(normal_layer, "MappingInformationType") != "ByPolygonVertex" or value(normal_layer, "ReferenceInformationType") != "Direct":
            raise ValueError("Unexpected source normal mapping")
        if value(uv_layer, "MappingInformationType") != "ByPolygonVertex" or value(uv_layer, "ReferenceInformationType") != "IndexToDirect":
            raise ValueError("Unexpected source UV mapping")
        normals = value(normal_layer, "Normals")
        uv = value(uv_layer, "UV")
        uv_indices = value(uv_layer, "UVIndex")
        part = {"id": part_id, "positions": [], "normals": [], "uv": [], "triangles": []}
        cache, polygon = {}, []
        for corner, index in enumerate(polygons):
            end = index < 0
            index = -index - 1 if end else index
            n = normals[corner * 3:corner * 3 + 3]
            uv_index = uv_indices[corner]
            key = (index, *n, uv_index)
            if key not in cache:
                cache[key] = len(part["positions"]) // 3
                x, y, z = positions[index * 3:index * 3 + 3]
                part["positions"].extend(round(v, 7) for v in (-z, y - .5, x))
                part["normals"].extend(round(v, 7) for v in (-n[2], n[1], n[0]))
                # FBX V is bottom-up; WPF ImageBrush V is top-down.
                part["uv"].extend((round(uv[uv_index * 2], 7), round(1 - uv[uv_index * 2 + 1], 7)))
            polygon.append(cache[key])
            if end:
                for i in range(1, len(polygon) - 1):
                    part["triangles"].extend((polygon[0], polygon[i], polygon[i + 1]))
                polygon = []
        if polygon:
            raise ValueError("Unterminated polygon")
        result["parts"].append(part)
    result["parts"].sort(key=lambda p: p["id"])
    if len(result["parts"]) != 22:
        raise ValueError("Expected the project's exact 22 source parts")
    path = pathlib.Path(destination)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(result, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({"parts": len(result["parts"]), "vertices": sum(len(p["positions"]) // 3 for p in result["parts"]),
                      "triangles": sum(len(p["triangles"]) // 3 for p in result["parts"]), "bytes": path.stat().st_size,
                      "sourceSha256": result["sourceSha256"]}))


if __name__ == "__main__":
    if len(sys.argv) > 2:
        export(sys.argv[1], sys.argv[2])
    else:
        inspect(sys.argv[1])
