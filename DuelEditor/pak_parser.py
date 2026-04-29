"""Multi-pak parser for Heroes of Might and Magic 5 game data files.

Scans all .pak files in data/ and UserMODs/ directories.
When the same file exists in multiple archives, uses the one with the latest modification date.
"""

import io
import os
import re
import zipfile
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
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


@dataclass
class SpellData:
    spell_id: str = ""
    name: str = ""
    description: str = ""
    level: int = 0
    school: str = ""
    mana_cost: int = 0
    icon_path: str = ""


@dataclass
class SkillData:
    skill_id: str = ""
    skill_type: str = ""  # SKILLTYPE_SKILL, SKILLTYPE_STANDART_PERK, etc.
    hero_class: str = ""  # HERO_CLASS_NONE, HERO_CLASS_KNIGHT, etc.
    basic_skill_id: str = ""  # parent skill for perks
    prerequisites: list = field(default_factory=list)
    name: str = ""
    description: str = ""
    icon_path: str = ""
    levels: int = 0  # number of texture levels (3 or 4 for skills)
    names_by_level: dict = field(default_factory=dict)
    descriptions_by_level: dict = field(default_factory=dict)
    preset_price: int = 0


@dataclass
class ArtifactData:
    artifact_id: str = ""
    name: str = ""
    description: str = ""
    art_type: str = ""  # ARTF_CLASS_MINOR, MAJOR, RELIC, GRAIL
    slot: str = ""
    cost: int = 0
    preset_price: int = 0
    available_for_presets: bool = True
    icon_path: str = ""
    stats: dict = field(default_factory=dict)


@dataclass
class HeroData:
    hero_id: str = ""
    internal_name: str = ""
    name: str = ""
    biography: str = ""
    hero_class: str = ""
    town_type: str = ""
    faction: str = ""
    specialization: str = ""
    spec_name: str = ""
    spec_desc: str = ""
    icon_path: str = ""
    offence: int = 0
    defence: int = 0
    spellpower: int = 0
    knowledge: int = 0
    starting_skills: list = field(default_factory=list)
    starting_perks: list = field(default_factory=list)
    starting_spells: list = field(default_factory=list)
    ballista: bool = False
    first_aid_tent: bool = False
    ammo_cart: bool = False
    shared_href: str = ""


@dataclass
class HeroClassData:
    class_id: str = ""
    skill_probs: dict = field(default_factory=dict)
    attribute_probs: dict = field(default_factory=dict)
    preferred_spells: list = field(default_factory=list)


class MultiPakParser:
    """Reads files from multiple pak archives, preferring latest modification date."""

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

    HERO_CLASS_TO_FACTION = {
        "HERO_CLASS_KNIGHT": "Haven",
        "HERO_CLASS_DEMON_LORD": "Inferno",
        "HERO_CLASS_NECROMANCER": "Necropolis",
        "HERO_CLASS_RANGER": "Preserve",
        "HERO_CLASS_WIZARD": "Academy",
        "HERO_CLASS_WARLOCK": "Dungeon",
        "HERO_CLASS_RUNEMAGE": "Dwarf",
        "HERO_CLASS_BARBARIAN": "Orcs",
    }

    FACTION_SCHOOLS = {
        "Haven": ["MAGIC_SCHOOL_LIGHT", "MAGIC_SCHOOL_DARK"],
        "Inferno": ["MAGIC_SCHOOL_DESTRUCTIVE", "MAGIC_SCHOOL_DARK"],
        "Necropolis": ["MAGIC_SCHOOL_DARK", "MAGIC_SCHOOL_SUMMONING"],
        "Academy": ["MAGIC_SCHOOL_LIGHT", "MAGIC_SCHOOL_SUMMONING"],
        "Dungeon": ["MAGIC_SCHOOL_DESTRUCTIVE", "MAGIC_SCHOOL_SUMMONING"],
        "Preserve": ["MAGIC_SCHOOL_LIGHT", "MAGIC_SCHOOL_SUMMONING"],
        "Dwarf": ["MAGIC_SCHOOL_LIGHT", "MAGIC_SCHOOL_DESTRUCTIVE"],
        "Orcs": [],
    }

    EXP_TABLE = {
        1: 0, 2: 1000, 3: 2000, 4: 3200, 5: 4600,
        6: 6200, 7: 8000, 8: 10000, 9: 12200, 10: 14700,
        11: 17500, 12: 20600, 13: 24320, 14: 28784, 15: 34140,
        16: 40567, 17: 48279, 18: 57533, 19: 68637, 20: 81961,
    }

    GROWTH_MULTIPLIERS = {1: 12, 2: 11, 3: 10, 4: 9, 5: 8, 6: 7, 7: 6}

    def __init__(self, game_dir: str):
        self.game_dir = Path(game_dir)
        self._file_index: dict[str, tuple[str, float]] = {}
        self._zip_files: dict[str, zipfile.ZipFile] = {}
        self.creatures: dict[str, CreatureStats] = {}
        self.spells: dict[str, SpellData] = {}
        self.skills: dict[str, SkillData] = {}
        self.artifacts: dict[str, ArtifactData] = {}
        self.heroes: dict[str, HeroData] = {}
        self.hero_classes: dict[str, HeroClassData] = {}
        self.combat_abilities: dict[str, dict] = {}
        self._scan_paks()

    def _scan_paks(self):
        """Scan all .pak files in data/ and UserMODs/, build file index with latest dates."""
        dirs_to_scan = []
        data_dir = self.game_dir / "data"
        mods_dir = self.game_dir / "UserMODs"
        if data_dir.exists():
            dirs_to_scan.append(data_dir)
        if mods_dir.exists():
            dirs_to_scan.append(mods_dir)
        if not dirs_to_scan and self.game_dir.exists():
            dirs_to_scan.append(self.game_dir)

        for scan_dir in dirs_to_scan:
            for pak_file in sorted(scan_dir.glob("*.pak")):
                self._index_pak(str(pak_file))

    def _index_pak(self, pak_path: str):
        """Index all files in a pak, keeping track of newest version of each file."""
        try:
            zf = zipfile.ZipFile(pak_path, "r")
            self._zip_files[pak_path] = zf
            for info in zf.infolist():
                if info.is_dir():
                    continue
                fname = info.filename
                try:
                    mod_time = info.date_time
                    timestamp = (mod_time[0] * 10000000000 + mod_time[1] * 100000000 +
                                 mod_time[2] * 1000000 + mod_time[3] * 10000 +
                                 mod_time[4] * 100 + mod_time[5])
                except (IndexError, TypeError):
                    timestamp = 0

                normalized = fname.lstrip("/").lower()
                if normalized not in self._file_index or timestamp > self._file_index[normalized][1]:
                    self._file_index[normalized] = (pak_path, timestamp)
                    original_key = fname.lstrip("/")
                    self._file_index[original_key] = (pak_path, timestamp)
        except (zipfile.BadZipFile, OSError):
            pass

    def _read_file(self, path: str) -> Optional[bytes]:
        """Read file from the pak that has the latest version."""
        clean = path.lstrip("/")
        entry = self._file_index.get(clean)
        if entry is None:
            entry = self._file_index.get(clean.lower())
        if entry is None:
            return None
        pak_path, _ = entry
        zf = self._zip_files.get(pak_path)
        if zf is None:
            return None
        try:
            return zf.read(clean)
        except KeyError:
            try:
                for name in zf.namelist():
                    if name.lstrip("/").lower() == clean.lower():
                        return zf.read(name)
            except Exception:
                pass
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

    def extract_icon_png(self, icon_path: str) -> Optional[bytes]:
        if not icon_path:
            return None
        dds_data = self._read_file(icon_path)
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

    # ---- Creatures ----

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
        c.faction = self.TOWN_MAP.get(c.town, "Unknown")
        visual_elem = root.find("Visual")
        if visual_elem is not None:
            visual_href = visual_elem.get("href", "")
            visual_path = self._resolve_href(visual_href)
            if visual_path:
                self._parse_creature_visual(c, visual_path)
        if not c.name:
            c.name = cid.replace("CREATURE_", "").replace("_", " ").title()
        return c

    def _parse_creature_visual(self, creature: CreatureStats, visual_path: str):
        root = self._read_xml(visual_path)
        if root is None:
            return
        name_ref = root.find("CreatureNameFileRef")
        if name_ref is not None:
            href = name_ref.get("href", "")
            txt_path = self._resolve_href(href)
            if txt_path:
                creature.name = self._read_text(txt_path)
        icon128 = root.find("Icon128")
        if icon128 is not None:
            href = icon128.get("href", "")
            icon_xdb_path = self._resolve_href(href)
            if icon_xdb_path:
                creature.icon_path = self._resolve_icon_dds(icon_xdb_path)

    # ---- Combat Abilities ----

    def parse_combat_abilities(self):
        ref_table = self._read_xml("GameMechanics/RefTables/CombatAbilities.xdb")
        if ref_table is None:
            return
        for item in ref_table.findall(".//Item"):
            aid = self._get_text(item, "ID")
            if not aid or aid == "ABILITY_UNKNOWN":
                continue
            obj = item.find("obj")
            if obj is None:
                continue
            name_ref = obj.find("NameFileRef")
            desc_ref = obj.find("DescriptionFileRef")
            name = ""
            desc = ""
            if name_ref is not None:
                href = name_ref.get("href", "")
                if href:
                    name = self._read_text(self._resolve_href(href))
            if desc_ref is not None:
                href = desc_ref.get("href", "")
                if href:
                    desc = self._read_text(self._resolve_href(href))
            self.combat_abilities[aid] = {"name": name, "description": desc}

    # ---- Spells ----

    def parse_spells(self):
        ref_table = self._read_xml("GameMechanics/RefTables/UndividedSpells.xdb")
        if ref_table is None:
            return
        for item in ref_table.findall(".//Item"):
            sid_elem = item.find("ID")
            if sid_elem is None or not sid_elem.text:
                continue
            sid = sid_elem.text.strip()
            if sid == "SPELL_NONE":
                continue
            # Obj tag contains href to actual spell XDB
            obj_elem = item.find("Obj")
            if obj_elem is None:
                continue
            obj_href = obj_elem.get("href", "")
            spell_xdb_path = self._resolve_href(obj_href)
            spell_root = self._read_xml(spell_xdb_path) if spell_xdb_path else None
            if spell_root is None:
                # Try inline data
                spell_root = obj_elem
            spell = SpellData()
            spell.spell_id = sid
            spell.level = self._get_int(spell_root, "Level")
            spell.school = self._get_text(spell_root, "MagicSchool")
            spell.mana_cost = self._get_int(spell_root, "TrainedCost")
            name_ref = spell_root.find("NameFileRef")
            if name_ref is not None:
                href = name_ref.get("href", "")
                if href:
                    spell.name = self._read_text(self._resolve_href(href))
            desc_ref = spell_root.find("LongDescriptionFileRef")
            if desc_ref is not None:
                href = desc_ref.get("href", "")
                if href:
                    spell.description = self._read_text(self._resolve_href(href))
            tex_ref = spell_root.find("Texture")
            if tex_ref is not None:
                href = tex_ref.get("href", "")
                if href:
                    icon_xdb = self._resolve_href(href)
                    if icon_xdb:
                        spell.icon_path = self._resolve_icon_dds(icon_xdb)
            if not spell.name:
                spell.name = sid.replace("SPELL_", "").replace("_", " ").title()
            self.spells[sid] = spell

    # ---- Skills & Perks ----

    def parse_skills(self):
        ref_table = self._read_xml("GameMechanics/RefTables/Skills.xdb")
        if ref_table is None:
            return
        for item in ref_table.findall(".//Item"):
            sid = self._get_text(item, "ID")
            if not sid or sid == "HERO_SKILL_NONE":
                continue
            obj = item.find("obj")
            if obj is None:
                continue
            skill = SkillData()
            skill.skill_id = sid
            skill.skill_type = self._get_text(obj, "SkillType")
            skill.hero_class = self._get_text(obj, "HeroClass")
            skill.basic_skill_id = self._get_text(obj, "BasicSkillID")
            skill.preset_price = self._get_int(obj, "PresetPrice")
            prereqs = obj.find("SkillPrerequisites")
            if prereqs is not None:
                for p_item in prereqs.findall("Item"):
                    if p_item.text:
                        skill.prerequisites.append(p_item.text.strip())
            # Parse name/description from CommonNameFileRef or level-based names
            common_name_ref = obj.find("CommonNameFileRef")
            if common_name_ref is not None:
                href = common_name_ref.get("href", "")
                if href:
                    skill.name = self._read_text(self._resolve_href(href))
            common_desc_ref = obj.find("CommonDescriptionFileRef")
            if common_desc_ref is not None:
                href = common_desc_ref.get("href", "")
                if href:
                    skill.description = self._read_text(self._resolve_href(href))
            # Parse level-specific names
            name_file_ref = obj.find("NameFileRef")
            if name_file_ref is not None:
                for idx, name_item in enumerate(name_file_ref.findall("Item")):
                    href = name_item.get("href", "")
                    if href:
                        text = self._read_text(self._resolve_href(href))
                        if text:
                            skill.names_by_level[idx] = text
                            if not skill.name:
                                skill.name = text
            desc_file_ref = obj.find("DescriptionFileRef")
            if desc_file_ref is not None:
                for idx, desc_item in enumerate(desc_file_ref.findall("Item")):
                    href = desc_item.get("href", "")
                    if href:
                        text = self._read_text(self._resolve_href(href))
                        if text:
                            skill.descriptions_by_level[idx] = text
                            if not skill.description:
                                skill.description = text
            # Parse icon from Texture
            tex_elem = obj.find("Texture")
            if tex_elem is not None:
                items = tex_elem.findall("Item")
                skill.levels = len([i for i in items if i.get("href", "")])
                for tex_item in items:
                    href = tex_item.get("href", "")
                    if href:
                        icon_xdb = self._resolve_href(href)
                        if icon_xdb:
                            skill.icon_path = self._resolve_icon_dds(icon_xdb)
                        break
            if not skill.name:
                skill.name = sid.replace("HERO_SKILL_", "").replace("_", " ").title()
            self.skills[sid] = skill

    # ---- Artifacts ----

    def parse_artifacts(self):
        ref_table = self._read_xml("GameMechanics/RefTables/Artifacts.xdb")
        if ref_table is None:
            return
        for item in ref_table.findall(".//Item"):
            aid = self._get_text(item, "ID")
            if not aid or aid == "ARTIFACT_NONE":
                continue
            obj = item.find("obj")
            if obj is None:
                continue
            art = ArtifactData()
            art.artifact_id = aid
            art.art_type = self._get_text(obj, "Type")
            art.slot = self._get_text(obj, "Slot")
            art.cost = self._get_int(obj, "CostOfGold")
            art.preset_price = self._get_int(obj, "PresetPrice")
            art.available_for_presets = self._get_bool(obj, "AvailableForPresets", True)
            name_ref = obj.find("NameFileRef")
            if name_ref is not None:
                href = name_ref.get("href", "")
                if href:
                    art.name = self._read_text(self._resolve_href(href))
            desc_ref = obj.find("DescriptionFileRef")
            if desc_ref is not None:
                href = desc_ref.get("href", "")
                if href:
                    art.description = self._read_text(self._resolve_href(href))
            icon_ref = obj.find("Icon")
            if icon_ref is not None:
                href = icon_ref.get("href", "")
                if href:
                    icon_xdb = self._resolve_href(href)
                    if icon_xdb:
                        art.icon_path = self._resolve_icon_dds(icon_xdb)
            stats_elem = obj.find("HeroStatsModif")
            if stats_elem is not None:
                art.stats = {
                    "attack": self._get_int(stats_elem, "Attack"),
                    "defence": self._get_int(stats_elem, "Defence"),
                    "knowledge": self._get_int(stats_elem, "Knowledge"),
                    "spellpower": self._get_int(stats_elem, "SpellPower"),
                    "morale": self._get_int(stats_elem, "Morale"),
                    "luck": self._get_int(stats_elem, "Luck"),
                }
            if not art.name:
                art.name = aid.replace("_", " ").title()
            self.artifacts[aid] = art

    # ---- Heroes ----

    def parse_heroes(self):
        any_xdb = self._read_xml("MapObjects/_(AdvMapSharedGroup)/Heroes/Any.xdb")
        if any_xdb is None:
            return
        for item in any_xdb.findall(".//Item"):
            href = item.get("href", "")
            if not href:
                continue
            xdb_path = self._resolve_href(href)
            if not xdb_path:
                continue
            match = re.search(r'/MapObjects/(\w+)/(\w+)\.\(AdvMapHeroShared\)', href)
            if not match:
                continue
            faction_dir = match.group(1)
            hero_name = match.group(2)
            hero = self._parse_hero_xdb(hero_name, xdb_path, faction_dir)
            if hero:
                self.heroes[hero_name] = hero

    def _parse_hero_xdb(self, hero_name: str, xdb_path: str, faction_dir: str) -> Optional[HeroData]:
        root = self._read_xml(xdb_path)
        if root is None:
            hero = HeroData()
            hero.hero_id = hero_name
            hero.internal_name = hero_name
            hero.shared_href = f"/MapObjects/{faction_dir}/{hero_name}.(AdvMapHeroShared).xdb#xpointer(/AdvMapHeroShared)"
            hero.faction = self._faction_from_dir(faction_dir)
            name_path = f"Text/Game/Heroes/Persons/{faction_dir}/{hero_name}/Name.txt"
            hero.name = self._read_text(name_path) or hero_name
            bio_path = f"Text/Game/Heroes/Persons/{faction_dir}/{hero_name}/Bio.txt"
            hero.biography = self._read_text(bio_path)
            return hero

        hero = HeroData()
        hero.hero_id = hero_name
        hero.internal_name = self._get_text(root, "InternalName") or hero_name
        hero.hero_class = self._get_text(root, "Class")
        hero.town_type = self._get_text(root, "TownType")
        hero.faction = self.HERO_CLASS_TO_FACTION.get(hero.hero_class, self._faction_from_dir(faction_dir))
        hero.specialization = self._get_text(root, "Specialization")
        hero.shared_href = f"/MapObjects/{faction_dir}/{hero_name}.(AdvMapHeroShared).xdb#xpointer(/AdvMapHeroShared)"

        icon128 = root.find("Icon128")
        if icon128 is not None:
            href = icon128.get("href", "")
            if href:
                icon_xdb = self._resolve_href(href)
                if icon_xdb:
                    hero.icon_path = self._resolve_icon_dds(icon_xdb)

        spec_name_ref = root.find("SpecializationNameFileRef")
        if spec_name_ref is not None:
            href = spec_name_ref.get("href", "")
            if href:
                hero.spec_name = self._read_text(self._resolve_href(href))
        spec_desc_ref = root.find("SpecializationDescFileRef")
        if spec_desc_ref is not None:
            href = spec_desc_ref.get("href", "")
            if href:
                hero.spec_desc = self._read_text(self._resolve_href(href))

        editable = root.find("Editable")
        if editable is not None:
            name_ref = editable.find("NameFileRef")
            if name_ref is not None:
                href = name_ref.get("href", "")
                if href:
                    hero.name = self._read_text(self._resolve_href(href))
            bio_ref = editable.find("BiographyFileRef")
            if bio_ref is not None:
                href = bio_ref.get("href", "")
                if href:
                    hero.biography = self._read_text(self._resolve_href(href))
            hero.offence = self._get_int(editable, "Offence")
            hero.defence = self._get_int(editable, "Defence")
            hero.spellpower = self._get_int(editable, "Spellpower")
            hero.knowledge = self._get_int(editable, "Knowledge")
            for skill_item in editable.findall(".//skills/Item"):
                mastery = self._get_text(skill_item, "Mastery")
                skill_id = self._get_text(skill_item, "SkillID")
                if skill_id:
                    hero.starting_skills.append({"mastery": mastery, "skill_id": skill_id})
            for perk_item in editable.findall(".//perkIDs/Item"):
                if perk_item.text:
                    hero.starting_perks.append(perk_item.text.strip())
            for spell_item in editable.findall(".//spellIDs/Item"):
                if spell_item.text:
                    hero.starting_spells.append(spell_item.text.strip())
            hero.ballista = self._get_bool(editable, "Ballista")
            hero.first_aid_tent = self._get_bool(editable, "FirstAidTent")
            hero.ammo_cart = self._get_bool(editable, "AmmoCart")

        if not hero.name:
            name_path = f"Text/Game/Heroes/Persons/{faction_dir}/{hero_name}/Name.txt"
            hero.name = self._read_text(name_path) or hero_name

        return hero

    def _faction_from_dir(self, faction_dir: str) -> str:
        mapping = {
            "Academy": "Academy", "Dungeon": "Dungeon", "Haven": "Haven",
            "Inferno": "Inferno", "Necropolis": "Necropolis", "Preserve": "Preserve",
            "Dwarves": "Dwarf", "Stronghold": "Orcs",
        }
        return mapping.get(faction_dir, faction_dir)

    # ---- Hero Classes ----

    def parse_hero_classes(self):
        ref_table = self._read_xml("GameMechanics/RefTables/HeroClass.xdb")
        if ref_table is None:
            return
        for item in ref_table.findall(".//Item"):
            cid = self._get_text(item, "ID")
            if not cid or cid == "HERO_CLASS_NONE":
                continue
            obj = item.find("obj")
            if obj is None:
                continue
            hc = HeroClassData()
            hc.class_id = cid
            for skill_item in obj.findall(".//SkillsProbs/Item"):
                skill_id = self._get_text(skill_item, "SkillID")
                prob = self._get_int(skill_item, "Prob")
                if skill_id:
                    hc.skill_probs[skill_id] = prob
            attr_elem = obj.find("AttributeProbs")
            if attr_elem is not None:
                hc.attribute_probs = {
                    "offence": self._get_int(attr_elem, "OffenceProb"),
                    "defence": self._get_int(attr_elem, "DefenceProb"),
                    "spellpower": self._get_int(attr_elem, "SpellpowerProb"),
                    "knowledge": self._get_int(attr_elem, "KnowledgeProb"),
                }
            for spell_item in obj.findall(".//PreferredSpellsFromSpellShop/Item"):
                if spell_item.text:
                    hc.preferred_spells.append(spell_item.text.strip())
            self.hero_classes[cid] = hc

    # ---- Utility ----

    def get_creatures_by_faction(self) -> dict[str, list[CreatureStats]]:
        result: dict[str, list[CreatureStats]] = {}
        for c in self.creatures.values():
            if c.faction not in result:
                result[c.faction] = []
            result[c.faction].append(c)
        for faction in result:
            result[faction].sort(key=lambda x: (x.tier, x.upgrade, x.name))
        return result

    def get_spells_by_school(self) -> dict[str, list[SpellData]]:
        result: dict[str, list[SpellData]] = {}
        for s in self.spells.values():
            if s.school not in result:
                result[s.school] = []
            result[s.school].append(s)
        for school in result:
            result[school].sort(key=lambda x: (x.level, x.name))
        return result

    def get_skills_tree(self) -> dict:
        """Return skills organized as base skills with their perks."""
        base_skills = {}
        perks = []
        for s in self.skills.values():
            if s.skill_type == "SKILLTYPE_SKILL":
                base_skills[s.skill_id] = s
            else:
                perks.append(s)
        return {"skills": base_skills, "perks": perks}

    def get_heroes_by_faction(self) -> dict[str, list[HeroData]]:
        result: dict[str, list[HeroData]] = {}
        for h in self.heroes.values():
            if h.faction not in result:
                result[h.faction] = []
            result[h.faction].append(h)
        for faction in result:
            result[faction].sort(key=lambda x: x.name)
        return result

    def load_all(self):
        """Load all game data."""
        self.parse_creatures()
        self.parse_combat_abilities()
        self.parse_spells()
        self.parse_skills()
        self.parse_artifacts()
        self.parse_heroes()
        self.parse_hero_classes()

    def close(self):
        for zf in self._zip_files.values():
            try:
                zf.close()
            except Exception:
                pass
