"""Flask web application for Heroes 5 Duel Preset Editor."""

import json
import os
import sys
from pathlib import Path

from flask import Flask, jsonify, render_template, request, send_from_directory

from pak_parser import PakParser

app = Flask(__name__)

parser: PakParser = None
ICONS_DIR = Path(__file__).parent / "static" / "icons"
PRESETS_DIR = Path(__file__).parent / "presets"


def init_parser(pak_path: str):
    global parser
    parser = PakParser(pak_path)
    parser.parse_creatures()
    ICONS_DIR.mkdir(parents=True, exist_ok=True)
    PRESETS_DIR.mkdir(parents=True, exist_ok=True)
    _extract_all_icons()


def _extract_all_icons():
    for cid, creature in parser.creatures.items():
        icon_file = ICONS_DIR / f"{cid}.png"
        if icon_file.exists():
            continue
        png_data = parser.extract_icon_png(creature)
        if png_data:
            icon_file.write_bytes(png_data)


@app.route("/")
def index():
    return render_template("index.html")


@app.route("/api/creatures")
def api_creatures():
    faction = request.args.get("faction")
    by_faction = parser.get_creatures_by_faction()

    if faction:
        creatures = by_faction.get(faction, [])
        return jsonify([parser.to_dict(c) for c in creatures])

    result = {}
    for f, creatures in sorted(by_faction.items()):
        result[f] = [parser.to_dict(c) for c in creatures]
    return jsonify(result)


@app.route("/api/factions")
def api_factions():
    by_faction = parser.get_creatures_by_faction()
    factions = []
    for name in sorted(by_faction.keys()):
        factions.append({
            "name": name,
            "creature_count": len(by_faction[name]),
        })
    return jsonify(factions)


@app.route("/api/creature/<creature_id>")
def api_creature(creature_id):
    creature = parser.creatures.get(creature_id)
    if creature is None:
        return jsonify({"error": "Creature not found"}), 404
    return jsonify(parser.to_dict(creature))


@app.route("/api/icon/<creature_id>")
def api_icon(creature_id):
    icon_file = ICONS_DIR / f"{creature_id}.png"
    if icon_file.exists():
        return send_from_directory(str(ICONS_DIR), f"{creature_id}.png", mimetype="image/png")
    return "", 404


@app.route("/api/preset", methods=["POST"])
def save_preset():
    data = request.get_json()
    if not data or "name" not in data:
        return jsonify({"error": "Preset name required"}), 400

    preset_name = data["name"].replace("/", "_").replace("\\", "_")
    preset_file = PRESETS_DIR / f"{preset_name}.json"
    preset_file.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    return jsonify({"status": "ok", "path": str(preset_file)})


@app.route("/api/preset/<name>")
def load_preset(name):
    preset_file = PRESETS_DIR / f"{name}.json"
    if not preset_file.exists():
        return jsonify({"error": "Preset not found"}), 404
    data = json.loads(preset_file.read_text(encoding="utf-8"))
    return jsonify(data)


@app.route("/api/presets")
def list_presets():
    if not PRESETS_DIR.exists():
        return jsonify([])
    presets = []
    for f in sorted(PRESETS_DIR.glob("*.json")):
        presets.append(f.stem)
    return jsonify(presets)


@app.route("/api/preset/<name>", methods=["DELETE"])
def delete_preset(name):
    preset_file = PRESETS_DIR / f"{name}.json"
    if preset_file.exists():
        preset_file.unlink()
    return jsonify({"status": "ok"})


if __name__ == "__main__":
    pak_path = sys.argv[1] if len(sys.argv) > 1 else None
    if not pak_path:
        print("Usage: python app.py <path_to_data1.pak>")
        print("Example: python app.py /path/to/data1.pak")
        sys.exit(1)
    if not os.path.exists(pak_path):
        print(f"File not found: {pak_path}")
        sys.exit(1)

    print(f"Loading game data from {pak_path}...")
    init_parser(pak_path)
    print(f"Loaded {len(parser.creatures)} creatures")
    factions = parser.get_creatures_by_faction()
    for f, creatures in sorted(factions.items()):
        print(f"  {f}: {len(creatures)} creatures")

    app.run(host="127.0.0.1", port=5000, debug=False)
