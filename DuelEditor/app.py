"""Flask + SocketIO server for Heroes 5 Duel Builder.

Supports LAN multiplayer and offline mode.
"""

import json
import os
import random
import sys
import threading
import socket
import struct
import time
from pathlib import Path

from flask import Flask, jsonify, render_template, request, send_from_directory
from flask_socketio import SocketIO, emit, join_room

from pak_parser import MultiPakParser
from preset_generator import generate_h5u

app = Flask(__name__)
app.config["SECRET_KEY"] = os.urandom(24).hex()
socketio = SocketIO(app, cors_allowed_origins="*", async_mode="threading")

parser: MultiPakParser = None
ICONS_DIR = Path(__file__).parent / "static" / "icons"
GAME_DIR = Path(".")

# Room state
rooms: dict[str, dict] = {}
UDP_PORT = 5001
DISCOVERY_RUNNING = False


def init_parser(game_dir: str):
    global parser, GAME_DIR
    GAME_DIR = Path(game_dir)
    parser = MultiPakParser(game_dir)
    parser.load_all()
    ICONS_DIR.mkdir(parents=True, exist_ok=True)
    _extract_all_icons()


def _extract_all_icons():
    """Extract icons for creatures, spells, skills, artifacts, heroes."""
    for cid, creature in parser.creatures.items():
        _extract_one_icon(f"creature_{cid}", creature.icon_path)
    for sid, spell in parser.spells.items():
        _extract_one_icon(f"spell_{sid}", spell.icon_path)
    for sid, skill in parser.skills.items():
        _extract_one_icon(f"skill_{sid}", skill.icon_path)
    for aid, art in parser.artifacts.items():
        _extract_one_icon(f"artifact_{aid}", art.icon_path)
    for hid, hero in parser.heroes.items():
        _extract_one_icon(f"hero_{hid}", hero.icon_path)


def _extract_one_icon(name: str, icon_path: str):
    if not icon_path:
        return
    icon_file = ICONS_DIR / f"{name}.png"
    if icon_file.exists():
        return
    png_data = parser.extract_icon_png(icon_path)
    if png_data:
        icon_file.write_bytes(png_data)


def get_local_ip():
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"


# ---- UDP Discovery ----

def start_udp_broadcast(room_name: str, port: int):
    """Broadcast room availability on LAN."""
    global DISCOVERY_RUNNING
    DISCOVERY_RUNNING = True

    def broadcast():
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        sock.settimeout(1)
        msg = json.dumps({"room": room_name, "ip": get_local_ip(), "port": port}).encode()
        while DISCOVERY_RUNNING:
            try:
                sock.sendto(msg, ("<broadcast>", UDP_PORT))
            except Exception:
                pass
            time.sleep(2)
        sock.close()

    t = threading.Thread(target=broadcast, daemon=True)
    t.start()


# ---- Flask Routes ----

@app.route("/")
def index():
    return render_template("index.html")


@app.route("/api/icon/<path:icon_name>")
def api_icon(icon_name):
    icon_file = ICONS_DIR / f"{icon_name}.png"
    if icon_file.exists():
        return send_from_directory(str(ICONS_DIR), f"{icon_name}.png", mimetype="image/png")
    return "", 404


@app.route("/api/game-data")
def api_game_data():
    """Return all game data for the client."""
    creatures_by_faction = {}
    for c in parser.creatures.values():
        if c.faction not in creatures_by_faction:
            creatures_by_faction[c.faction] = []
        creatures_by_faction[c.faction].append({
            "id": c.creature_id, "name": c.name, "faction": c.faction,
            "tier": c.tier, "upgrade": c.upgrade, "attack": c.attack,
            "defense": c.defense, "min_damage": c.min_damage,
            "max_damage": c.max_damage, "shots": c.shots, "speed": c.speed,
            "initiative": c.initiative, "health": c.health, "flying": c.flying,
            "gold_cost": c.gold_cost, "weekly_growth": c.weekly_growth,
            "abilities": c.abilities, "pair_creature": c.pair_creature,
            "base_creature": c.base_creature, "power": c.power,
            "has_icon": bool(c.icon_path),
        })
    for f in creatures_by_faction:
        creatures_by_faction[f].sort(key=lambda x: (x["tier"], x["upgrade"], x["name"]))

    spells_list = []
    for s in parser.spells.values():
        spells_list.append({
            "id": s.spell_id, "name": s.name, "description": s.description,
            "level": s.level, "school": s.school, "mana_cost": s.mana_cost,
            "has_icon": bool(s.icon_path),
        })

    skills_list = []
    for s in parser.skills.values():
        skills_list.append({
            "id": s.skill_id, "name": s.name, "description": s.description,
            "skill_type": s.skill_type, "hero_class": s.hero_class,
            "basic_skill_id": s.basic_skill_id, "prerequisites": s.prerequisites,
            "levels": s.levels, "has_icon": bool(s.icon_path),
        })

    artifacts_list = []
    for a in parser.artifacts.values():
        if not a.available_for_presets:
            continue
        artifacts_list.append({
            "id": a.artifact_id, "name": a.name, "description": a.description,
            "type": a.art_type, "slot": a.slot, "cost": a.cost,
            "stats": a.stats, "has_icon": bool(a.icon_path),
        })

    heroes_by_faction = {}
    for h in parser.heroes.values():
        if h.faction not in heroes_by_faction:
            heroes_by_faction[h.faction] = []
        heroes_by_faction[h.faction].append({
            "id": h.hero_id, "name": h.name, "hero_class": h.hero_class,
            "faction": h.faction, "specialization": h.specialization,
            "spec_name": h.spec_name, "offence": h.offence, "defence": h.defence,
            "spellpower": h.spellpower, "knowledge": h.knowledge,
            "starting_skills": h.starting_skills, "starting_perks": h.starting_perks,
            "starting_spells": h.starting_spells, "shared_href": h.shared_href,
            "has_icon": bool(h.icon_path),
        })

    hero_classes = {}
    for hc in parser.hero_classes.values():
        hero_classes[hc.class_id] = {
            "skill_probs": hc.skill_probs,
            "attribute_probs": hc.attribute_probs,
        }

    ability_names = {}
    for aid, ab in parser.combat_abilities.items():
        ability_names[aid] = ab.get("name", aid)

    return jsonify({
        "creatures": creatures_by_faction,
        "spells": spells_list,
        "skills": skills_list,
        "artifacts": artifacts_list,
        "heroes": heroes_by_faction,
        "hero_classes": hero_classes,
        "ability_names": ability_names,
        "faction_schools": parser.FACTION_SCHOOLS,
        "exp_table": parser.EXP_TABLE,
        "growth_multipliers": parser.GROWTH_MULTIPLIERS,
    })


# ---- SocketIO Events ----

@socketio.on("connect")
def on_connect():
    emit("connected", {"sid": request.sid})


@socketio.on("create_room")
def on_create_room(data):
    room_name = data.get("name", f"Room_{random.randint(1000,9999)}")
    room_id = f"room_{int(time.time())}_{random.randint(100,999)}"
    rooms[room_id] = {
        "name": room_name,
        "players": {request.sid: {"player_num": 1, "faction": None, "ready": False, "build": None}},
        "factions_assigned": False,
        "state": "waiting",
    }
    join_room(room_id)
    emit("room_created", {
        "room_id": room_id, "name": room_name,
        "player_num": 1, "ip": get_local_ip(), "port": 5000,
    })
    start_udp_broadcast(room_name, 5000)


@socketio.on("join_room")
def on_join_room(data):
    room_id = data.get("room_id")
    if room_id not in rooms:
        emit("error", {"message": "Комната не найдена"})
        return
    room = rooms[room_id]
    player_count = len(room["players"])
    if player_count >= 2:
        emit("error", {"message": "Комната полная"})
        return
    room["players"][request.sid] = {"player_num": 2, "faction": None, "ready": False, "build": None}
    join_room(room_id)
    emit("room_joined", {"room_id": room_id, "name": room["name"], "player_num": 2})
    if len(room["players"]) == 2:
        _assign_factions(room_id)


@socketio.on("start_offline")
def on_start_offline(data):
    """Start offline mode with 2 local players."""
    room_id = f"offline_{int(time.time())}"
    rooms[room_id] = {
        "name": "Offline",
        "players": {
            request.sid: {"player_num": 1, "faction": None, "ready": False, "build": None},
            f"offline_p2_{request.sid}": {"player_num": 2, "faction": None, "ready": False, "build": None},
        },
        "factions_assigned": False,
        "state": "waiting",
        "offline": True,
    }
    join_room(room_id)
    _assign_factions(room_id)


def _assign_factions(room_id: str):
    room = rooms[room_id]
    all_factions = ["Haven", "Inferno", "Necropolis", "Academy", "Dungeon", "Preserve", "Dwarf", "Orcs"]
    chosen = random.sample(all_factions, 2)
    players = list(room["players"].keys())
    for i, sid in enumerate(players):
        room["players"][sid]["faction"] = chosen[i]
    room["factions_assigned"] = True
    room["state"] = "building"
    socketio.emit("factions_assigned", {
        "room_id": room_id,
        "assignments": {str(i + 1): chosen[i] for i in range(2)},
    }, room=room_id)


@socketio.on("player_ready")
def on_player_ready(data):
    room_id = data.get("room_id")
    build = data.get("build")
    if room_id not in rooms:
        return
    room = rooms[room_id]
    sid = request.sid
    if sid in room["players"]:
        room["players"][sid]["ready"] = True
        room["players"][sid]["build"] = build
    # For offline mode, also mark P2
    if room.get("offline"):
        p2_key = f"offline_p2_{sid}"
        if p2_key in room["players"]:
            room["players"][p2_key]["ready"] = True
            room["players"][p2_key]["build"] = data.get("build_p2", build)

    all_ready = all(p["ready"] for p in room["players"].values())
    if all_ready:
        _generate_preset(room_id)
    else:
        socketio.emit("player_status", {
            "room_id": room_id,
            "ready_count": sum(1 for p in room["players"].values() if p["ready"]),
        }, room=room_id)


def _generate_preset(room_id: str):
    room = rooms[room_id]
    players = sorted(room["players"].values(), key=lambda p: p["player_num"])
    p1_build = players[0].get("build", {})
    p2_build = players[1].get("build", {})

    # Load terrain template if available
    terrain = None
    template_path = GAME_DIR / "data"
    for pak_path in template_path.glob("*.pak") if template_path.exists() else []:
        try:
            import zipfile
            zf = zipfile.ZipFile(str(pak_path))
            for name in zf.namelist():
                if "GroundTerrain.bin" in name:
                    terrain = zf.read(name)
                    break
            zf.close()
            if terrain:
                break
        except Exception:
            pass

    h5u_p1 = generate_h5u(p1_build, p2_build, for_player=1, template_terrain=terrain)
    h5u_p2 = generate_h5u(p1_build, p2_build, for_player=2, template_terrain=terrain)

    # Save to UserMODs
    mods_dir = GAME_DIR / "UserMODs"
    mods_dir.mkdir(parents=True, exist_ok=True)
    preset_name = f"DuelPreset_{int(time.time())}"
    (mods_dir / f"{preset_name}_p1.h5u").write_bytes(h5u_p1)
    (mods_dir / f"{preset_name}_p2.h5u").write_bytes(h5u_p2)

    room["state"] = "done"
    socketio.emit("preset_generated", {
        "room_id": room_id,
        "preset_name": preset_name,
        "message": f"Пресет сохранён в UserMODs/{preset_name}_p1.h5u и _p2.h5u",
    }, room=room_id)


@socketio.on("list_rooms")
def on_list_rooms(data):
    available = []
    for rid, room in rooms.items():
        if room["state"] == "waiting" and len(room["players"]) < 2:
            available.append({"room_id": rid, "name": room["name"]})
    emit("rooms_list", {"rooms": available})


@socketio.on("disconnect")
def on_disconnect():
    for room_id, room in list(rooms.items()):
        if request.sid in room["players"]:
            del room["players"][request.sid]
            if not room["players"]:
                del rooms[room_id]
            else:
                socketio.emit("player_left", {"room_id": room_id}, room=room_id)


if __name__ == "__main__":
    game_dir = sys.argv[1] if len(sys.argv) > 1 else "."
    if not os.path.isdir(game_dir):
        print(f"Директория не найдена: {game_dir}")
        print("Использование: python app.py <путь_к_папке_игры>")
        sys.exit(1)

    print(f"Загрузка данных игры из {game_dir}...")
    init_parser(game_dir)
    print(f"Загружено: {len(parser.creatures)} существ, {len(parser.spells)} заклинаний, "
          f"{len(parser.skills)} навыков/перков, {len(parser.artifacts)} артефактов, "
          f"{len(parser.heroes)} героев, {len(parser.hero_classes)} классов героев")

    local_ip = get_local_ip()
    print(f"\nСервер запущен: http://{local_ip}:5000")
    print(f"Для второго игрока: откройте http://{local_ip}:5000 в браузере")

    socketio.run(app, host="0.0.0.0", port=5000, debug=False, allow_unsafe_werkzeug=True)
