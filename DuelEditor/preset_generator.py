"""Generator for Heroes 5 .h5u duel preset files."""

import io
import zipfile
from typing import Any


def generate_hero_xdb(player: dict) -> str:
    """Generate AdvMapHero XDB content for a player's hero."""
    hero = player.get("hero", {})
    army = player.get("army", [])
    artifacts = player.get("artifacts", [])
    skills = player.get("skills", [])
    perks = player.get("perks", [])
    spells = player.get("spells", [])
    stats = player.get("stats", {})
    experience = player.get("experience", 0)
    shared_href = hero.get("shared_href", "")

    army_xml = ""
    for slot in army:
        if slot and slot.get("creature_id") and slot.get("count", 0) > 0:
            army_xml += f"""
                <Item>
                        <Creature>{slot['creature_id']}</Creature>
                        <Count>{slot['count']}</Count>
                </Item>"""

    artifacts_xml = ""
    untransferable_xml = ""
    for art in artifacts:
        artifacts_xml += f"\n                <Item>{art}</Item>"
        untransferable_xml += "\n                <Item>0</Item>"

    skills_xml = ""
    for sk in skills:
        skills_xml += f"""
                        <Item>
                                <Mastery>{sk['mastery']}</Mastery>
                                <SkillID>{sk['skill_id']}</SkillID>
                        </Item>"""

    perks_xml = ""
    for p in perks:
        perks_xml += f"\n                        <Item>{p}</Item>"

    spells_xml = ""
    for sp in spells:
        spells_xml += f"\n                        <Item>{sp}</Item>"

    ballista = "true" if player.get("ballista", False) else "false"
    first_aid = "true" if player.get("first_aid_tent", False) else "false"
    ammo_cart = "true" if player.get("ammo_cart", False) else "false"

    primary_mastery = "MASTERY_BASIC"
    for sk in skills:
        if sk.get("is_class_skill"):
            primary_mastery = sk["mastery"]
            break

    return f"""<?xml version="1.0" encoding="UTF-8"?>
<AdvMapHero>
        <Pos>
                <x>0</x>
                <y>0</y>
                <z>0</z>
        </Pos>
        <Rot>0</Rot>
        <Floor>0</Floor>
        <Name/>
        <CombatScript/>
        <pointLights/>
        <Shared href="{shared_href}"/>
        <PlayerID>PLAYER_NONE</PlayerID>
        <Experience>{experience}</Experience>
        <armySlots>{army_xml}
        </armySlots>
        <artifactIDs>{artifacts_xml}
        </artifactIDs>
        <isUntransferable>{untransferable_xml}
        </isUntransferable>
        <Editable>
                <NameFileRef href="name.txt"/>
                <BiographyFileRef href=""/>
                <Offence>{stats.get('offence', 0)}</Offence>
                <Defence>{stats.get('defence', 0)}</Defence>
                <Spellpower>{stats.get('spellpower', 0)}</Spellpower>
                <Knowledge>{stats.get('knowledge', 0)}</Knowledge>
                <skills>{skills_xml}
                </skills>
                <perkIDs>{perks_xml}
                </perkIDs>
                <spellIDs>{spells_xml}
                </spellIDs>
                <Ballista>{ballista}</Ballista>
                <FirstAidTent>{first_aid}</FirstAidTent>
                <AmmoCart>{ammo_cart}</AmmoCart>
                <FavoriteEnemies/>
                <TalismanLevel>0</TalismanLevel>
        </Editable>
        <OverrideMask>123</OverrideMask>
        <PrimarySkillMastery>{primary_mastery}</PrimarySkillMastery>
        <LossTrigger>
                <Action>
                        <FunctionName/>
                </Action>
        </LossTrigger>
        <AllowQuickCombat>true</AllowQuickCombat>
        <Textures>
                <Icon128x128/>
                <Icon64x64/>
                <RoundedFace/>
                <LeftFace/>
                <RightFace/>
        </Textures>
        <PresetPrice>0</PresetPrice>
        <BannedRaces/>
</AdvMapHero>"""


def generate_presets_xdb(my_hero_path: str, my_name: str) -> str:
    """Generate presets.(DuelPresets).xdb with only the local player's hero."""
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<DuelPresets ObjectRecordID="1000001">
        <presets>
                <Item>
                        <PresetNameFileRef href="/Text/PresetNames/CustomPreset.txt"/>
                        <LeftFace href=""/>
                        <RightFace href=""/>
                        <RoundedFace href=""/>
                        <PresetHero href="{my_hero_path}#xpointer(/AdvMapHero)"/>
                </Item>
        </presets>
</DuelPresets>"""


def generate_map_xdb(hero_paths: list[str]) -> str:
    """Generate map.xdb referencing all heroes."""
    objects_xml = ""
    for path in hero_paths:
        objects_xml += f'\n                <Item href="{path}#xpointer(/AdvMapHero)"/>'

    return f"""<?xml version="1.0" encoding="UTF-8"?>
<AdvMapDesc ObjectRecordID="1001204">
        <CustomGameMap>false</CustomGameMap>
        <Version>3</Version>
        <TileX>72</TileX>
        <TileY>72</TileY>
        <HasUnderground>false</HasUnderground>
        <HasSurface>true</HasSurface>
        <InitialFloor>0</InitialFloor>
        <objects>{objects_xml}
        </objects>
        <players>
                <Item>
                        <IsActive>true</IsActive>
                        <IsHumanPlayable>true</IsHumanPlayable>
                        <Team>0</Team>
                </Item>
                <Item>
                        <IsActive>true</IsActive>
                        <IsHumanPlayable>true</IsHumanPlayable>
                        <Team>1</Team>
                </Item>
        </players>
        <CustomTeams>true</CustomTeams>
</AdvMapDesc>"""


MAP_TAG_XDB = """<?xml version="1.0" encoding="UTF-8"?>
<AdvMapDescTag>
        <AdvMapDesc href="map.xdb#xpointer(/AdvMapDesc)"/>
        <NameFileRef href=""/>
        <DescriptionFileRef href=""/>
        <TileX>72</TileX>
        <TileY>72</TileY>
        <MapGoal href=""/>
        <teams>
                <Item>1</Item>
                <Item>1</Item>
                <Item>1</Item>
                <Item>1</Item>
                <Item>1</Item>
                <Item>1</Item>
                <Item>1</Item>
                <Item>1</Item>
        </teams>
        <thumbnailImages/>
        <HasUnderground>false</HasUnderground>
        <RandomMap>false</RandomMap>
        <CustomGameMap>false</CustomGameMap>
        <Version>3</Version>
</AdvMapDescTag>"""


GROUND_TERRAIN_SIZE = 72 * 72 * 4


def generate_h5u(player1: dict, player2: dict, for_player: int,
                 template_terrain: bytes = None) -> bytes:
    """Generate a .h5u duel preset file.

    Args:
        player1: Player 1 build data
        player2: Player 2 build data
        for_player: 1 or 2 — determines which hero appears in UI presets
        template_terrain: Optional terrain binary data from a template preset
    """
    buf = io.BytesIO()

    p1_dir = "Maps/DuelMode/Heroes/player1"
    p2_dir = "Maps/DuelMode/Heroes/player2"
    p1_xdb_name = "Hero1.xdb"
    p2_xdb_name = "Hero2.xdb"
    p1_path = f"/{p1_dir}/{p1_xdb_name}"
    p2_path = f"/{p2_dir}/{p2_xdb_name}"

    hero1_xdb = generate_hero_xdb(player1)
    hero2_xdb = generate_hero_xdb(player2)
    p1_name = player1.get("hero", {}).get("name", "Player 1")
    p2_name = player2.get("hero", {}).get("name", "Player 2")

    if for_player == 1:
        presets_xdb = generate_presets_xdb(p1_path, p1_name)
    else:
        presets_xdb = generate_presets_xdb(p2_path, p2_name)

    map_xdb = generate_map_xdb([p1_path, p2_path])

    terrain = template_terrain or (b"\x00" * GROUND_TERRAIN_SIZE)

    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr(f"{p1_dir}/{p1_xdb_name}", hero1_xdb)
        zf.writestr(f"{p1_dir}/name.txt", p1_name.encode("utf-16"))
        zf.writestr(f"{p2_dir}/{p2_xdb_name}", hero2_xdb)
        zf.writestr(f"{p2_dir}/name.txt", p2_name.encode("utf-16"))
        zf.writestr("Maps/DuelMode/PresetMap/map.xdb", map_xdb)
        zf.writestr("Maps/DuelMode/PresetMap/map-tag.xdb", MAP_TAG_XDB)
        zf.writestr("Maps/DuelMode/PresetMap/GroundTerrain.bin", terrain)
        zf.writestr("UI/MPDMLobby/presets.(DuelPresets).xdb", presets_xdb)

    return buf.getvalue()
