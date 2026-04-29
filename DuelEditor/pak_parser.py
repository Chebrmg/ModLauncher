"""Parser for Heroes of Might and Magic 5 .pak (zip) game data files."""

import io
import re
import zipfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import PurePosixPath
from typing import Optional

try:
    from PIL import Image
except ImportError:
    Image = None


@dataclass
class CreatureStats:
    creature_id: str = ""
    attack: int = 0
    defense: int = 0
    min_damage: int = 0
    max_damage: int = 0
    shots: int = 0
    speed: int = 0
    initiative: int = 0
    health: int = 0
    flying: bool = False
    exp: int = 0
    power: int = 0
    tier: int = 0
    upgrade: bool = False
    pair_creature: str = ""
    base_creature: str = ""
    town: str = ""
    weekly_growth: int = 0
    combat_size: int = 1
    range_val: int = 0
    spell_points: int = 0
    gold_cost: int = 0
    abilities: list = field(default_factory=list)
    upgrades: list = field(default_factory=list)
    known_spells: list = field(default_factory=list)
    name: str = ""
    description: str = ""
    abilities_text: str = ""
    icon_path: str = ""
    faction: str = ""
    xdb_path: str = ""


class PakParser:
    TOWN_MAP = {
        "TOWN_HEAVEN": "Haven",
        "TOWN_INFERNO": "Inferno",
        "TOWN_NECROMANCY": "Necropolis",
        "TOWN_PRESERVE": "Preserve",
        "TOWN_DUNGEON": "Dungeon",
        "TOWN_ACADEMY": "Academy",
        "TOWN_FORTRESS": "Dwarf",
        "TOWN_DWARVES": "Dwarf",
        "TOWN_ORCS": "Orcs",
        "TOWN_STRONGHOLD": "Orcs",
        "TOWN_NO_TYPE": "Neutrals",
    }

    FACTION_DIR_MAP = {
        "Haven": "Heaven",
        "Inferno": "Inferno",
        "Necropolis": "Necropolis",
        "Preserve": "Preserve",
        "Dungeon": "Dungeon",
        "Academy": "Academy",
        "Dwarf": "Dwarf",
        "Orcs": "Orcs",
        "Neutrals": "Neutrals",
    }

    def __init__(self, pak_path: str):
        self.pak_path = pak_path
        self.zip_file = zipfile.ZipFile(pak_path, "r")
        self._name_list = set(self.zip_file.namelist())
        self.creatures: dict[str, CreatureStats] = {}

    def close(self):
        self.zip_file.close()

    def _read_file(self, path: str) -> Optional[bytes]:
        clean = path.lstrip("/")
        if clean in self._name_list:
            return self.zip_file.read(clean)
        return None

    def _read_xml(self, path: str) -> Optional[ET.Element]:
        data = self._read_file(path)
        if data is None:
            return None
        try:
            return ET.fromstring(data)
        except ET.ParseError:
            return None

    def _read_text(self, path: str) -> str:
        data = self._read_file(path)
        if data is None:
            return ""
        for enc in ("utf-16", "utf-16-le", "utf-8", "cp1251"):
            try:
                return data.decode(enc).strip()
            except (UnicodeDecodeError, UnicodeError):
                continue
        return ""

    def _resolve_href(self, href: str) -> str:
        if not href:
            return ""
        path = href.split("#")[0]
        return path.lstrip("/")

    def _get_text(self, elem: Optional[ET.Element], tag: str, default: str = "") -> str:
        if elem is None:
            return default
        child = elem.find(tag)
        if child is not None and child.text:
            return child.text.strip()
        return default

    def _get_int(self, elem: Optional[ET.Element], tag: str, default: int = 0) -> int:
        text = self._get_text(elem, tag)
        try:
            return int(text)
        except (ValueError, TypeError):
            return default

    def _get_bool(self, elem: Optional[ET.Element], tag: str, default: bool = False) -> bool:
        text = self._get_text(elem, tag)
        return text.lower() == "true" if text else default

    def parse_creatures(self):
        ref_table = self._read_xml("GameMechanics/RefTables/Creatures.xdb")
        if ref_table is None:
            return

        for item in ref_table.findall(".//Item"):
            cid = self._get_text(item, "ID")
            if not cid or cid == "CREATURE_UNKNOWN":
                continue

            obj_elem = item.find("Obj")
            if obj_elem is None:
                continue
            href = obj_elem.get("href", "")
            xdb_path = self._resolve_href(href)
            if not xdb_path:
                continue

            creature = self._parse_creature_xdb(cid, xdb_path)
            if creature:
                self.creatures[cid] = creature

    def _parse_creature_xdb(self, cid: str, xdb_path: str) -> Optional[CreatureStats]:
        root = self._read_xml(xdb_path)
        if root is None:
            return None

        c = CreatureStats()
        c.creature_id = cid
        c.xdb_path = xdb_path
        c.attack = self._get_int(root, "AttackSkill")
        c.defense = self._get_int(root, "DefenceSkill")
        c.min_damage = self._get_int(root, "MinDamage")
        c.max_damage = self._get_int(root, "MaxDamage")
        c.shots = self._get_int(root, "Shots")
        c.speed = self._get_int(root, "Speed")
        c.initiative = self._get_int(root, "Initiative")
        c.health = self._get_int(root, "Health")
        c.flying = self._get_bool(root, "Flying")
        c.exp = self._get_int(root, "Exp")
        c.power = self._get_int(root, "Power")
        c.tier = self._get_int(root, "CreatureTier")
        c.upgrade = self._get_bool(root, "Upgrade")
        c.pair_creature = self._get_text(root, "PairCreature")
        c.base_creature = self._get_text(root, "BaseCreature")
        c.town = self._get_text(root, "CreatureTown")
        c.weekly_growth = self._get_int(root, "WeeklyGrowth")
        c.combat_size = self._get_int(root, "CombatSize", 1)
        c.range_val = self._get_int(root, "Range")
        c.spell_points = self._get_int(root, "SpellPoints")

        cost_elem = root.find("Cost")
        c.gold_cost = self._get_int(cost_elem, "Gold")

        for ab_item in root.findall(".//Abilities/Item"):
            if ab_item.text:
                c.abilities.append(ab_item.text.strip())

        for up_item in root.findall(".//Upgrades/Item"):
            if up_item.text:
                c.upgrades.append(up_item.text.strip())

        for spell_item in root.findall(".//KnownSpells/Item"):
            spell_name = self._get_text(spell_item, "Spell")
            mastery = self._get_text(spell_item, "Mastery")
            if spell_name:
                c.known_spells.append({"spell": spell_name, "mastery": mastery})

        faction = self.TOWN_MAP.get(c.town, "Unknown")
        c.faction = faction

        visual_elem = root.find("Visual")
        if visual_elem is not None:
            visual_href = visual_elem.get("href", "")
            visual_path = self._resolve_href(visual_href)
            if visual_path:
                self._parse_visual(c, visual_path)

        if not c.name:
            c.name = cid.replace("CREATURE_", "").replace("_", " ").title()

        return c

    def _parse_visual(self, creature: CreatureStats, visual_path: str):
        root = self._read_xml(visual_path)
        if root is None:
            return

        name_ref = root.find("CreatureNameFileRef")
        if name_ref is not None:
            href = name_ref.get("href", "")
            txt_path = self._resolve_href(href)
            if txt_path:
                creature.name = self._read_text(txt_path)

        desc_ref = root.find("DescriptionFileRef")
        if desc_ref is not None:
            href = desc_ref.get("href", "")
            txt_path = self._resolve_href(href)
            if txt_path:
                creature.description = self._read_text(txt_path)

        abilities_ref = root.find("CreatureAbilitiesFileRef")
        if abilities_ref is not None:
            href = abilities_ref.get("href", "")
            txt_path = self._resolve_href(href)
            if txt_path:
                creature.abilities_text = self._read_text(txt_path)

        icon128 = root.find("Icon128")
        if icon128 is not None:
            href = icon128.get("href", "")
            icon_xdb_path = self._resolve_href(href)
            if icon_xdb_path:
                creature.icon_path = self._resolve_icon_dds(icon_xdb_path)

    def _resolve_icon_dds(self, icon_xdb_path: str) -> str:
        root = self._read_xml(icon_xdb_path)
        if root is None:
            return ""
        dest = root.find("DestName")
        if dest is not None:
            href = dest.get("href", "")
            if href:
                parent = str(PurePosixPath(icon_xdb_path).parent)
                return f"{parent}/{href}"
        return ""

    def extract_icon_png(self, creature: CreatureStats) -> Optional[bytes]:
        if not creature.icon_path:
            return None
        dds_data = self._read_file(creature.icon_path)
        if dds_data is None:
            return None
        if Image is None:
            return None
        try:
            img = Image.open(io.BytesIO(dds_data))
            buf = io.BytesIO()
            img.save(buf, format="PNG")
            return buf.getvalue()
        except Exception:
            return None

    def get_creatures_by_faction(self) -> dict[str, list[CreatureStats]]:
        result: dict[str, list[CreatureStats]] = {}
        for c in self.creatures.values():
            faction = c.faction
            if faction not in result:
                result[faction] = []
            result[faction].append(c)
        for faction in result:
            result[faction].sort(key=lambda x: (x.tier, x.upgrade, x.name))
        return result

    def to_dict(self, creature: CreatureStats) -> dict:
        return {
            "id": creature.creature_id,
            "name": creature.name,
            "faction": creature.faction,
            "tier": creature.tier,
            "upgrade": creature.upgrade,
            "attack": creature.attack,
            "defense": creature.defense,
            "min_damage": creature.min_damage,
            "max_damage": creature.max_damage,
            "shots": creature.shots,
            "speed": creature.speed,
            "initiative": creature.initiative,
            "health": creature.health,
            "flying": creature.flying,
            "exp": creature.exp,
            "power": creature.power,
            "gold_cost": creature.gold_cost,
            "weekly_growth": creature.weekly_growth,
            "combat_size": creature.combat_size,
            "range": creature.range_val,
            "abilities": creature.abilities,
            "upgrades": creature.upgrades,
            "known_spells": creature.known_spells,
            "pair_creature": creature.pair_creature,
            "base_creature": creature.base_creature,
            "town": creature.town,
            "description": creature.description,
            "abilities_text": creature.abilities_text,
            "has_icon": bool(creature.icon_path),
        }
