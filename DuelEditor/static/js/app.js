/* Heroes 5 Duel Builder — Client (Phase 2 with all corrections) */

const socket = io();
let gameData = null;
let roomId = null;
let playerNum = 0;
let myFaction = '';
let isOffline = false;

// Player state
const STARTING_GOLD = 120000;
const MAX_ADDITIONAL_SKILLS = 5; // TL allows up to 5 additional
const BL_MAX_ADDITIONAL = 4;     // Fix 4: BL stops offering new base at 4 additional
let state = {
    gold: STARTING_GOLD,
    level: 1,
    stats: { offence: 0, defence: 0, spellpower: 0, knowledge: 0 },
    baseStats: { offence: 0, defence: 0, spellpower: 0, knowledge: 0 },
    skills: [],      // [{skill_id, mastery}]
    perks: [],       // [perk_id, ...]
    army: [null, null, null, null, null, null, null],
    tierUsed: {},    // Fix 6: {tier: count_used} - shared tier pool
    artifacts: [],   // [artifact_id, ...]
    equippedSlots: {},
    spells: [],      // [spell_id, ...]
    extraLevelsBought: 0,
    statsBonusBought: false,
    heroClass: '',
    hero: null,
    levelingDone: false,
    guildSpells: null,
    artifactShop: null,
};

let p2State = null;

// Leveling state
let currentLevelOptions = null;
let isExtraLevelLevelup = false; // Fix 5: true when doing extra level levelup

function escapeHtml(text) {
    const d = document.createElement('div');
    d.textContent = text;
    return d.innerHTML;
}

// ---- Right-Click Description Modal ----
function showDescriptionModal(title, description) {
    closeDescriptionModal();
    const modal = document.createElement('div');
    modal.className = 'description-modal';
    modal.id = 'desc-modal';
    modal.onclick = (e) => { if (e.target === modal) closeDescriptionModal(); };
    modal.innerHTML = `<div class="desc-modal-content">
        <div class="desc-modal-header">
            <h3>${escapeHtml(title)}</h3>
            <button onclick="closeDescriptionModal()" class="desc-close-btn">×</button>
        </div>
        <div class="desc-modal-body">${escapeHtml(description || 'Нет описания')}</div>
    </div>`;
    document.body.appendChild(modal);
}

function closeDescriptionModal() {
    const existing = document.getElementById('desc-modal');
    if (existing) existing.remove();
}

// Show creature stats modal on right-click (Fix 7)
function showCreatureStatsModal(creature) {
    closeDescriptionModal();
    const modal = document.createElement('div');
    modal.className = 'description-modal';
    modal.id = 'desc-modal';
    modal.onclick = (e) => { if (e.target === modal) closeDescriptionModal(); };

    const abilities = (creature.abilities || []).map(a => {
        const name = gameData.ability_names[a] || a.replace('ABILITY_', '').replace(/_/g, ' ');
        return `<span class="ability-tag">${escapeHtml(name)}</span>`;
    }).join(' ');

    modal.innerHTML = `<div class="desc-modal-content">
        <div class="desc-modal-header">
            <h3>${escapeHtml(creature.name)}</h3>
            <button onclick="closeDescriptionModal()" class="desc-close-btn">×</button>
        </div>
        <div class="desc-modal-body">
            <div class="creature-stats-grid">
                <div><b>Атака:</b> ${creature.attack}</div>
                <div><b>Защита:</b> ${creature.defense}</div>
                <div><b>Урон:</b> ${creature.min_damage}-${creature.max_damage}</div>
                <div><b>Здоровье:</b> ${creature.health}</div>
                <div><b>Скорость:</b> ${creature.speed}</div>
                <div><b>Инициатива:</b> ${creature.initiative}</div>
                ${creature.shots ? `<div><b>Выстрелы:</b> ${creature.shots}</div>` : ''}
                ${creature.flying ? `<div><b>Летает:</b> Да</div>` : ''}
            </div>
            ${abilities ? `<div class="creature-abilities"><b>Способности:</b><br>${abilities}</div>` : ''}
        </div>
    </div>`;
    document.body.appendChild(modal);
}

// ---- Socket Events ----
socket.on('connected', () => console.log('Connected'));

socket.on('room_created', (data) => {
    roomId = data.room_id;
    playerNum = data.player_num;
    showScreen('waiting-screen');
    const localIp = data.local_ip || '127.0.0.1';
    const externalIp = data.external_ip || localIp;
    document.getElementById('waiting-info').innerHTML =
        `Локальный: <b>${localIp}:${data.port}</b> | Внешний: <b>${externalIp}:${data.port}</b><br>Комната: ${escapeHtml(data.name || '')}`;
});

socket.on('room_joined', (data) => {
    roomId = data.room_id;
    playerNum = data.player_num;
    showScreen('waiting-screen');
    document.getElementById('waiting-title').textContent = 'Подключено! Ожидание...';
});

socket.on('rooms_list', (data) => {
    const list = document.getElementById('rooms-list');
    if (!data.rooms.length) {
        list.innerHTML = '<p style="color:#888">Нет доступных комнат</p>';
        return;
    }
    list.innerHTML = data.rooms.map(r =>
        `<div class="room-item"><span>${escapeHtml(r.name)}</span>
         <button onclick="joinRoom('${r.room_id}')">Войти</button></div>`
    ).join('');
});

socket.on('factions_assigned', (data) => {
    roomId = data.room_id;
    const assignments = data.assignments;
    myFaction = assignments[String(playerNum)];
    loadGameData().then(() => {
        initBuilder();
        showScreen('builder-screen');
    });
});

socket.on('player_status', (data) => {
    console.log('Player status:', data);
});

socket.on('preset_generated', (data) => {
    showScreen('done-screen');
    const msg = data.message || 'Пресет сгенерирован!';
    // Fix 13: P2 downloads via browser
    if (data.download_url) {
        document.getElementById('done-message').innerHTML =
            `${escapeHtml(msg)}<br><br><a href="${data.download_url}" class="download-btn" download>Скачать пресет</a>`;
        // Auto-trigger download for P2
        if (playerNum === 2 || isOffline) {
            setTimeout(() => { window.location.href = data.download_url; }, 500);
        }
    } else {
        document.getElementById('done-message').textContent = msg;
    }
});

socket.on('error', (data) => {
    alert(data.message);
});

// ---- Lobby ----
function createRoom() {
    document.getElementById('room-name-input').style.display = 'block';
}

function confirmCreateRoom() {
    const name = document.getElementById('room-name').value || 'Дуэль';
    socket.emit('create_room', { name });
}

function refreshRooms() {
    socket.emit('list_rooms', {});
}

function joinRoom(rid) {
    socket.emit('join_room', { room_id: rid });
}

function startOffline() {
    isOffline = true;
    playerNum = 1;
    socket.emit('start_offline', {});
}

function showScreen(id) {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    document.getElementById(id).classList.add('active');
}

// ---- Data Loading ----
async function loadGameData() {
    const resp = await fetch('/api/game-data');
    gameData = await resp.json();
}

// ---- Builder Init ----
function initBuilder() {
    const classMap = {
        'Haven': 'HERO_CLASS_KNIGHT', 'Inferno': 'HERO_CLASS_DEMON_LORD',
        'Necropolis': 'HERO_CLASS_NECROMANCER', 'Preserve': 'HERO_CLASS_RANGER',
        'Academy': 'HERO_CLASS_WIZARD', 'Dungeon': 'HERO_CLASS_WARLOCK',
        'Dwarf': 'HERO_CLASS_RUNEMAGE', 'Orcs': 'HERO_CLASS_BARBARIAN',
    };
    state.heroClass = classMap[myFaction] || '';

    // Pick a random hero from faction
    const factionHeroes = gameData.heroes[myFaction] || [];
    if (factionHeroes.length) {
        state.hero = factionHeroes[Math.floor(Math.random() * factionHeroes.length)];
        state.baseStats = {
            offence: state.hero.offence || 0,
            defence: state.hero.defence || 0,
            spellpower: state.hero.spellpower || 0,
            knowledge: state.hero.knowledge || 0,
        };
        state.stats = { ...state.baseStats };
        if (state.hero.starting_skills) {
            state.skills = state.hero.starting_skills.map(s => ({
                skill_id: s.skill_id, mastery: s.mastery
            }));
        }
        if (state.hero.starting_perks) {
            state.perks = [...state.hero.starting_perks];
        }
        if (state.hero.starting_spells) {
            state.spells = [...state.hero.starting_spells];
        }
    }

    // Fix 3: Ensure racial skill is present at start
    const classSkillId = getClassSkillId();
    if (classSkillId && !state.skills.find(s => s.skill_id === classSkillId)) {
        state.skills.unshift({ skill_id: classSkillId, mastery: 'MASTERY_BASIC' });
    }

    generateArtifactShop();

    // UI setup
    document.getElementById('player-faction').textContent = myFaction;
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.onclick = () => switchTab(btn.dataset.tab);
    });

    updateUI();
    startLevelUp();
}

function switchTab(tab) {
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
    document.querySelector(`[data-tab="${tab}"]`).classList.add('active');
    document.getElementById(`tab-${tab}`).classList.add('active');
    if (tab === 'army') renderArmy();
    if (tab === 'artifacts') renderArtifacts();
    if (tab === 'spells') renderSpells();
}

// ---- UI Update ----
function updateUI() {
    document.getElementById('player-gold').textContent = `${state.gold.toLocaleString()} zoloto`;
    document.getElementById('player-level').textContent = `Ур. ${state.level}`;
    document.getElementById('player-stats').innerHTML = [
        `<span class="stat stat-atk">Атк ${state.stats.offence}</span>`,
        `<span class="stat stat-def">Деф ${state.stats.defence}</span>`,
        `<span class="stat stat-sp">СМ ${state.stats.spellpower}</span>`,
        `<span class="stat stat-kn">Зн ${state.stats.knowledge}</span>`,
    ].join('');

    // Skills list (Fix 2.1: right-click for description)
    const skillsList = document.getElementById('skills-list');
    let html = '';
    for (const sk of state.skills) {
        const skillData = findSkill(sk.skill_id);
        const name = skillData ? skillData.name : sk.skill_id;
        const masteryLabel = sk.mastery.replace('MASTERY_', '');
        const mc = `mastery-${masteryLabel.toLowerCase()}`;
        html += `<span class="skill-tag" oncontextmenu="event.preventDefault(); showSkillDescription('${sk.skill_id}', '${sk.mastery}')">
            ${skillData && skillData.has_icon ? `<img src="/api/icon/skill_${sk.skill_id}" onerror="this.style.display='none'">` : ''}
            ${escapeHtml(name)} <span class="mastery-badge ${mc}">${masteryLabel}</span>
        </span>`;
    }
    for (const pid of state.perks) {
        const perkData = findSkill(pid);
        const name = perkData ? perkData.name : pid;
        html += `<span class="skill-tag perk-tag" oncontextmenu="event.preventDefault(); showPerkDescription('${pid}')">
            ${perkData && perkData.has_icon ? `<img src="/api/icon/skill_${pid}" onerror="this.style.display='none'">` : ''}
            ${escapeHtml(name)}
        </span>`;
    }
    skillsList.innerHTML = html;
}

// Fix 2: Show skill description based on mastery level
function showSkillDescription(skillId, mastery) {
    const skillData = findSkill(skillId);
    if (!skillData) return;
    const masteryIdx = { 'MASTERY_BASIC': 0, 'MASTERY_ADVANCED': 1, 'MASTERY_EXPERT': 2 };
    const idx = masteryIdx[mastery] || 0;
    const desc = (skillData.descriptions_by_level && skillData.descriptions_by_level[String(idx)])
        || skillData.description || 'Нет описания';
    const name = (skillData.names_by_level && skillData.names_by_level[String(idx)])
        || skillData.name || skillId;
    showDescriptionModal(name, desc);
}

function showPerkDescription(perkId) {
    const perkData = findSkill(perkId);
    if (!perkData) return;
    showDescriptionModal(perkData.name || perkId, perkData.description || 'Нет описания');
}

// ---- Helpers ----
function findSkill(skillId) {
    return (gameData.skills || []).find(s => s.id === skillId);
}

function getHeroClassData() {
    return gameData.hero_classes[state.heroClass] || { skill_probs: {}, attribute_probs: {} };
}

function weightedRandom(probs) {
    const entries = Object.entries(probs).filter(([_, p]) => p > 0);
    const total = entries.reduce((s, [_, p]) => s + p, 0);
    if (total === 0) return null;
    let r = Math.random() * total;
    for (const [key, p] of entries) {
        r -= p;
        if (r <= 0) return key;
    }
    return entries[entries.length - 1][0];
}

function getClassSkillId() {
    const classSkillMap = {
        'HERO_CLASS_KNIGHT': 'HERO_SKILL_TRAINING',
        'HERO_CLASS_DEMON_LORD': 'HERO_SKILL_GATING',
        'HERO_CLASS_NECROMANCER': 'HERO_SKILL_NECROMANCY',
        'HERO_CLASS_RANGER': 'HERO_SKILL_AVENGER',
        'HERO_CLASS_WIZARD': 'HERO_SKILL_ARTIFICIER',
        'HERO_CLASS_WARLOCK': 'HERO_SKILL_INVOCATION',
        'HERO_CLASS_RUNEMAGE': 'HERO_SKILL_RUNELORE',
        'HERO_CLASS_BARBARIAN': 'HERO_SKILL_DEMONIC_RAGE',
    };
    return classSkillMap[state.heroClass] || '';
}

function getAdditionalSkillCount() {
    const classSkill = getClassSkillId();
    return state.skills.filter(s => s.skill_id !== classSkill).length;
}

function canLearnNewSkill(skillId) {
    if (state.skills.find(s => s.skill_id === skillId)) return false;
    const skillData = findSkill(skillId);
    if (!skillData || skillData.skill_type !== 'SKILLTYPE_SKILL') return false;
    if (skillData.hero_class !== 'HERO_CLASS_NONE' && skillData.hero_class !== state.heroClass) return false;
    return true;
}

function getPromotableSkills() {
    return state.skills.filter(sk => {
        if (sk.mastery === 'MASTERY_EXPERT') return false;
        const skillData = findSkill(sk.skill_id);
        if (!skillData) return false;
        const maxLevel = skillData.levels || 3;
        const masteryOrder = ['MASTERY_BASIC', 'MASTERY_ADVANCED', 'MASTERY_EXPERT'];
        const currentIdx = masteryOrder.indexOf(sk.mastery);
        return currentIdx < maxLevel - 1;
    });
}

function getAvailablePerks(primaryFirst) {
    const allPerks = (gameData.skills || []).filter(s =>
        s.skill_type !== 'SKILLTYPE_SKILL' && s.id !== 'HERO_SKILL_NONE'
    );

    const available = allPerks.filter(perk => {
        if (state.perks.includes(perk.id)) return false;
        if (perk.hero_class !== 'HERO_CLASS_NONE' && perk.hero_class !== state.heroClass) return false;
        if (perk.basic_skill_id && perk.basic_skill_id !== 'HERO_SKILL_NONE') {
            if (!state.skills.find(s => s.skill_id === perk.basic_skill_id)) return false;
        }
        for (const prereq of (perk.prerequisites || [])) {
            if (!state.perks.includes(prereq)) return false;
        }
        return true;
    });

    const primary = available.filter(p =>
        (p.skill_type === 'SKILLTYPE_STANDART_PERK' || p.skill_type === 'SKILLTYPE_CLASS_PERK') &&
        (!p.prerequisites || p.prerequisites.length === 0)
    );
    const secondary = available.filter(p =>
        (p.skill_type === 'SKILLTYPE_SPECIAL_PERK' || p.skill_type === 'SKILLTYPE_UINQUE_PERK') ||
        (p.prerequisites && p.prerequisites.length > 0)
    );

    if (primaryFirst) {
        if (primary.length > 0) return primary[Math.floor(Math.random() * primary.length)];
        if (secondary.length > 0) return secondary[Math.floor(Math.random() * secondary.length)];
    } else {
        if (secondary.length > 0) return secondary[Math.floor(Math.random() * secondary.length)];
        if (primary.length > 0) return primary[Math.floor(Math.random() * primary.length)];
    }
    return null;
}

// ---- LEVELING ----
function startLevelUp() {
    if (state.level >= 20) {
        finishLeveling();
        return;
    }
    generateLevelOptions();
}

function generateLevelOptions() {
    const hc = getHeroClassData();
    const options = { tl: null, bl: null, tr: null, br: null };

    // Random stat gain
    const statGained = weightedRandom(hc.attribute_probs || {});
    if (statGained) {
        state.stats[statGained] = (state.stats[statGained] || 0) + 1;
    }
    const statNames = { offence: 'Нападение', defence: 'Защита', spellpower: 'Сила магии', knowledge: 'Знание' };
    document.getElementById('stat-gained').textContent =
        statGained ? `+1 ${statNames[statGained] || statGained}` : '';

    // Top-Left: New skill or random promote
    const additionalCount = getAdditionalSkillCount();
    if (additionalCount < MAX_ADDITIONAL_SKILLS) {
        const newSkillId = pickNewSkill(hc);
        if (newSkillId) {
            options.tl = { type: 'new_skill', skill_id: newSkillId };
        }
    } else {
        const promotable = getPromotableSkills();
        if (promotable.length > 0) {
            const sk = promotable[Math.floor(Math.random() * promotable.length)];
            options.tl = { type: 'promote', skill_id: sk.skill_id };
        }
    }

    // Fix 4: Bottom-Left — promote existing, or new base only if < BL_MAX_ADDITIONAL
    const promotable = getPromotableSkills();
    if (promotable.length > 1 || (promotable.length === 1 && options.tl)) {
        const sk = promotable[Math.floor(Math.random() * promotable.length)];
        if (!options.tl || options.tl.skill_id !== sk.skill_id) {
            options.bl = { type: 'promote', skill_id: sk.skill_id };
        } else if (promotable.length > 1) {
            const other = promotable.filter(s => s.skill_id !== sk.skill_id);
            options.bl = { type: 'promote', skill_id: other[0].skill_id };
        }
    } else if (promotable.length === 1 && !options.tl) {
        options.tl = { type: 'promote', skill_id: promotable[0].skill_id };
        options.bl = null;
    } else if (promotable.length === 0 && additionalCount < BL_MAX_ADDITIONAL) {
        // Fix 4: Only offer new base if < 4 additional skills (besides racial)
        const newSkillId = pickNewSkill(hc);
        if (newSkillId && (!options.tl || options.tl.skill_id !== newSkillId)) {
            options.bl = { type: 'new_skill', skill_id: newSkillId };
        }
    }

    // Top-Right: Primary perk
    options.tr = getAvailablePerks(true);
    if (options.tr) options.tr = { type: 'perk', perk_id: options.tr.id, perk: options.tr };

    // Bottom-Right: Secondary perk
    options.br = getAvailablePerks(false);
    if (options.br) {
        if (options.tr && options.br.id === options.tr.perk_id) {
            const alt = getAvailablePerks(true);
            options.br = alt && alt.id !== options.tr.perk_id
                ? { type: 'perk', perk_id: alt.id, perk: alt } : null;
        } else {
            options.br = { type: 'perk', perk_id: options.br.id, perk: options.br };
        }
    }

    currentLevelOptions = options;
    renderLevelOptions();
}

function pickNewSkill(hc) {
    const probs = { ...(hc.skill_probs || {}) };
    for (const sk of state.skills) {
        delete probs[sk.skill_id];
    }
    for (const sid of Object.keys(probs)) {
        if (!canLearnNewSkill(sid)) delete probs[sid];
    }
    return weightedRandom(probs);
}

function renderLevelOptions() {
    document.getElementById('current-level').textContent = state.level;
    const slotIds = ['tl', 'bl', 'tr', 'br'];
    for (const id of slotIds) {
        const slot = document.getElementById(`slot-${id}`);
        const content = document.getElementById(`slot-${id}-content`);
        const opt = currentLevelOptions[id];
        slot.classList.remove('empty', 'selected');
        if (!opt) {
            slot.classList.add('empty');
            content.innerHTML = '<span style="color:#555">—</span>';
            continue;
        }
        if (opt.type === 'new_skill') {
            const sk = findSkill(opt.skill_id);
            content.innerHTML = `<div class="skill-name">${escapeHtml(sk ? sk.name : opt.skill_id)}</div>
                <span class="mastery-badge mastery-basic">BASIC</span>
                <div class="skill-desc">Новый навык</div>`;
        } else if (opt.type === 'promote') {
            const sk = findSkill(opt.skill_id);
            const current = state.skills.find(s => s.skill_id === opt.skill_id);
            const next = current?.mastery === 'MASTERY_BASIC' ? 'ADVANCED' : 'EXPERT';
            content.innerHTML = `<div class="skill-name">${escapeHtml(sk ? sk.name : opt.skill_id)}</div>
                <span class="mastery-badge mastery-${next.toLowerCase()}">${next}</span>
                <div class="skill-desc">Повышение навыка</div>`;
        } else if (opt.type === 'perk') {
            const pk = opt.perk;
            content.innerHTML = `<div class="skill-name">${escapeHtml(pk ? pk.name : opt.perk_id)}</div>
                <div class="skill-desc">Перк</div>`;
        }
    }
    document.getElementById('levelup-slots').style.display = '';
    document.getElementById('post-leveling').style.display = 'none';
    updateUI();
}

function chooseLevelOption(slotId) {
    if (!currentLevelOptions) return;
    const opt = currentLevelOptions[slotId];
    if (!opt) return;

    if (opt.type === 'new_skill') {
        state.skills.push({ skill_id: opt.skill_id, mastery: 'MASTERY_BASIC' });
    } else if (opt.type === 'promote') {
        const sk = state.skills.find(s => s.skill_id === opt.skill_id);
        if (sk) {
            if (sk.mastery === 'MASTERY_BASIC') sk.mastery = 'MASTERY_ADVANCED';
            else if (sk.mastery === 'MASTERY_ADVANCED') sk.mastery = 'MASTERY_EXPERT';
        }
    } else if (opt.type === 'perk') {
        if (!state.perks.includes(opt.perk_id)) {
            state.perks.push(opt.perk_id);
        }
    }

    state.level++;
    currentLevelOptions = null;

    if (isExtraLevelLevelup) {
        isExtraLevelLevelup = false;
        finishLeveling();
    } else if (state.level >= 20) {
        finishLeveling();
    } else {
        startLevelUp();
    }
    updateUI();
}

function finishLeveling() {
    state.levelingDone = true;
    document.getElementById('levelup-slots').style.display = 'none';
    document.getElementById('post-leveling').style.display = '';
    document.getElementById('current-level').textContent = state.level;
    document.getElementById('stat-gained').textContent = 'Прокачка завершена!';
    updateBuyLevelCost();
    if (state.statsBonusBought) {
        document.getElementById('buy-stats-btn').disabled = true;
    }
    // Refresh guild spells when leveling changes (Fix 9)
    generateGuildSpells();
    updateUI();
}

function updateBuyLevelCost() {
    const cost = 8000 + state.extraLevelsBought * 2000;
    document.getElementById('buy-level-cost').textContent = cost.toLocaleString();
}

// Fix 5: Buy extra level = show levelup window + stats
function buyExtraLevel() {
    const cost = 8000 + state.extraLevelsBought * 2000;
    if (state.gold < cost) { alert('Недостаточно золота!'); return; }
    state.gold -= cost;
    state.extraLevelsBought++;
    state.levelingDone = false;
    isExtraLevelLevelup = true;
    updateBuyLevelCost();
    // Show levelup UI with all 4 slots
    generateLevelOptions();
}

function showBuyStats() {
    if (state.statsBonusBought) { alert('Уже куплено!'); return; }
    if (state.gold < 5000) { alert('Недостаточно золота!'); return; }
    document.getElementById('buy-stats-modal').style.display = 'flex';
}

function buyStats(stat) {
    if (state.statsBonusBought || state.gold < 5000) return;
    state.gold -= 5000;
    state.stats[stat] = (state.stats[stat] || 0) + 2;
    state.statsBonusBought = true;
    document.getElementById('buy-stats-btn').disabled = true;
    closeBuyStats();
    updateUI();
}

function closeBuyStats() {
    document.getElementById('buy-stats-modal').style.display = 'none';
}

// ---- ARMY ----
// Fix 6: Helper to get tier pool info
function getTierPool(tier) {
    const creatures = gameData.creatures[myFaction] || [];
    const tierCreatures = creatures.filter(c => c.tier === tier);
    if (!tierCreatures.length) return { total: 0, used: 0, remaining: 0 };
    const baseCreature = tierCreatures.find(c => !c.upgrade) || tierCreatures[0];
    const mult = gameData.growth_multipliers[tier] || 6;
    const total = baseCreature.weekly_growth * mult;
    const used = state.tierUsed[tier] || 0;
    return { total, used, remaining: total - used };
}

function renderArmy() {
    const slotsDiv = document.getElementById('army-slots');
    let html = '';
    for (let i = 0; i < 7; i++) {
        const slot = state.army[i];
        if (slot) {
            html += `<div class="army-slot filled" oncontextmenu="event.preventDefault(); showArmySlotStats(${i})">
                <button class="remove-btn" onclick="removeArmy(${i})">x</button>
                ${slot.has_icon ? `<img src="/api/icon/creature_${slot.creature_id}" onerror="this.style.display='none'">` : ''}
                <div class="creature-name">${escapeHtml(slot.name)}</div>
                <div class="creature-count">x${slot.count}</div>
                <div style="font-size:0.75em;color:#e94560">${(slot.gold_cost * slot.count).toLocaleString()} zol</div>
                ${getUpgradeOptions(slot, i)}
            </div>`;
        } else {
            html += `<div class="army-slot"><div style="color:#555;margin-top:40px">Слот ${i + 1}</div></div>`;
        }
    }
    slotsDiv.innerHTML = html;

    // Creature shop (Fix 6: shared tier pools, base unit first)
    const shopDiv = document.getElementById('creature-shop');
    const creatures = gameData.creatures[myFaction] || [];
    let shopHtml = '<h4>Доступные существа:</h4>';
    const tiers = {};
    for (const c of creatures) {
        if (!tiers[c.tier]) tiers[c.tier] = [];
        tiers[c.tier].push(c);
    }
    for (let t = 1; t <= 7; t++) {
        if (!tiers[t]) continue;
        const pool = getTierPool(t);
        // Fix 6: Sort so base unit (non-upgrade) is first
        const sorted = [...tiers[t]].sort((a, b) => (a.upgrade ? 1 : 0) - (b.upgrade ? 1 : 0));

        shopHtml += `<div class="tier-section"><h5>Тир ${t} — Доступно: ${pool.remaining}/${pool.total}</h5><div class="creature-grid">`;
        for (const c of sorted) {
            // Fix 6: Check if already bought THIS specific creature
            const alreadyBought = state.army.find(s => s && s.creature_id === c.id);
            const available = alreadyBought ? 0 : pool.remaining;
            shopHtml += `<div class="creature-card" onclick="buyCreature('${c.id}', ${t})"
                oncontextmenu="event.preventDefault(); showCreatureShopStats('${c.id}')"
                style="${available <= 0 ? 'opacity:0.4;pointer-events:none' : ''}">
                ${c.has_icon ? `<img src="/api/icon/creature_${c.id}" onerror="this.style.display='none'">` : ''}
                <div class="info">
                    <div class="name">${escapeHtml(c.name)}</div>
                    <div class="cost">${c.gold_cost} zol | ${available} шт</div>
                </div>
            </div>`;
        }
        shopHtml += '</div></div>';
    }
    shopDiv.innerHTML = shopHtml;
}

// Fix 7: Right-click on army slot shows stats
function showArmySlotStats(idx) {
    const slot = state.army[idx];
    if (!slot) return;
    const creatures = gameData.creatures[myFaction] || [];
    const c = creatures.find(cr => cr.id === slot.creature_id);
    if (c) showCreatureStatsModal(c);
}

function showCreatureShopStats(creatureId) {
    const creatures = gameData.creatures[myFaction] || [];
    const c = creatures.find(cr => cr.id === creatureId);
    if (c) showCreatureStatsModal(c);
}

function getUpgradeOptions(slot, idx) {
    if (slot.upgrade) return '';
    const creatures = gameData.creatures[myFaction] || [];
    const upgrades = creatures.filter(c => c.tier === slot.tier && c.upgrade && c.id !== slot.creature_id);
    if (!upgrades.length) return '';
    return upgrades.map(u => {
        const costDiff = (u.gold_cost - slot.gold_cost) * slot.count;
        return `<button class="upgrade-btn" onclick="event.stopPropagation(); upgradeCreature(${idx},'${u.id}',${costDiff})">
            ${escapeHtml(u.name)} (+${costDiff.toLocaleString()})</button>`;
    }).join('');
}

function buyCreature(creatureId, tier) {
    const emptySlot = state.army.findIndex(s => s === null);
    if (emptySlot === -1) { alert('Нет свободных слотов!'); return; }
    const creatures = gameData.creatures[myFaction] || [];
    const creature = creatures.find(c => c.id === creatureId);
    if (!creature) return;
    // Fix 6: Use shared tier pool
    const pool = getTierPool(tier);
    const maxAffordable = Math.floor(state.gold / creature.gold_cost);
    const max = Math.min(pool.remaining, maxAffordable);
    if (max <= 0) { alert('Недостаточно золота или существ!'); return; }

    showCountModal(creature.name, max, (count) => {
        if (count <= 0 || count > max) return;
        const cost = creature.gold_cost * count;
        state.gold -= cost;
        state.tierUsed[tier] = (state.tierUsed[tier] || 0) + count;
        state.army[emptySlot] = {
            creature_id: creature.id, name: creature.name, count,
            gold_cost: creature.gold_cost, has_icon: creature.has_icon,
            tier: creature.tier, upgrade: creature.upgrade
        };
        renderArmy();
        updateUI();
    });
}

function removeArmy(idx) {
    const slot = state.army[idx];
    if (slot) {
        state.gold += slot.gold_cost * slot.count;
        // Fix 6: Return to tier pool
        state.tierUsed[slot.tier] = (state.tierUsed[slot.tier] || 0) - slot.count;
        if (state.tierUsed[slot.tier] < 0) state.tierUsed[slot.tier] = 0;
        state.army[idx] = null;
    }
    renderArmy();
    updateUI();
}

function upgradeCreature(idx, newId, costDiff) {
    if (state.gold < costDiff) { alert('Недостаточно золота!'); return; }
    const creatures = gameData.creatures[myFaction] || [];
    const creature = creatures.find(c => c.id === newId);
    if (!creature) return;
    state.gold -= costDiff;
    const old = state.army[idx];
    state.army[idx] = {
        creature_id: creature.id, name: creature.name, count: old.count,
        gold_cost: creature.gold_cost, has_icon: creature.has_icon,
        tier: creature.tier, upgrade: creature.upgrade
    };
    renderArmy();
    updateUI();
}

function showCountModal(name, max, callback) {
    const modal = document.createElement('div');
    modal.className = 'count-modal';
    modal.innerHTML = `<div class="count-modal-content">
        <h4>${escapeHtml(name)}</h4>
        <p>Максимум: ${max}</p>
        <input type="number" id="count-input" min="1" max="${max}" value="${max}">
        <div class="modal-buttons">
            <button onclick="this.closest('.count-modal').remove()" style="background:#e94560;color:white">Отмена</button>
            <button id="count-confirm" style="background:#27ae60;color:white">Купить</button>
        </div>
    </div>`;
    document.body.appendChild(modal);
    document.getElementById('count-confirm').onclick = () => {
        const count = parseInt(document.getElementById('count-input').value) || 0;
        modal.remove();
        callback(count);
    };
}

// ---- ARTIFACTS ----
function generateArtifactShop() {
    // Fix 8: Only use artifacts with AvailableForPresets = true (already filtered server-side)
    const arts = gameData.artifacts || [];
    const minor = arts.filter(a => a.type === 'ARTF_CLASS_MINOR');
    const major = arts.filter(a => a.type === 'ARTF_CLASS_MAJOR');
    const relic = arts.filter(a => a.type === 'ARTF_CLASS_RELIC');
    shuffle(minor); shuffle(major); shuffle(relic);
    state.artifactShop = [
        ...minor.slice(0, 6),
        ...major.slice(0, 4),
        ...relic.slice(0, 2),
    ];
}

function shuffle(arr) {
    for (let i = arr.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [arr[i], arr[j]] = [arr[j], arr[i]];
    }
}

// Fix 8: Reroll artifacts for 10000 gold, keep bought ones
function rerollArtifacts() {
    const REROLL_COST = 10000;
    if (state.gold < REROLL_COST) { alert('Недостаточно золота! Нужно 10,000'); return; }
    state.gold -= REROLL_COST;

    const boughtIds = new Set(state.artifacts);
    const arts = gameData.artifacts || [];
    const available = arts.filter(a => !boughtIds.has(a.id));
    const minor = available.filter(a => a.type === 'ARTF_CLASS_MINOR');
    const major = available.filter(a => a.type === 'ARTF_CLASS_MAJOR');
    const relic = available.filter(a => a.type === 'ARTF_CLASS_RELIC');
    shuffle(minor); shuffle(major); shuffle(relic);

    // Keep bought artifacts in shop, replace unbought ones
    const oldBought = state.artifactShop.filter(a => boughtIds.has(a.id));
    const newMinorCount = 6 - oldBought.filter(a => a.type === 'ARTF_CLASS_MINOR').length;
    const newMajorCount = 4 - oldBought.filter(a => a.type === 'ARTF_CLASS_MAJOR').length;
    const newRelicCount = 2 - oldBought.filter(a => a.type === 'ARTF_CLASS_RELIC').length;

    state.artifactShop = [
        ...oldBought,
        ...minor.slice(0, Math.max(0, newMinorCount)),
        ...major.slice(0, Math.max(0, newMajorCount)),
        ...relic.slice(0, Math.max(0, newRelicCount)),
    ];

    renderArtifacts();
    updateUI();
}

function renderArtifacts() {
    // Fix 8: Remove INVENTORY, add 2nd FINGER slot
    const SLOT_NAMES = {
        'HEAD': 'Голова', 'NECK': 'Шея', 'CHEST': 'Тело', 'SHOULDERS': 'Плечи',
        'FEET': 'Ноги', 'FINGER': 'Кольцо 1', 'FINGER2': 'Кольцо 2',
        'PRIMARY': 'Оружие', 'SECONDARY': 'Щит', 'MISCSLOT1': 'Разное',
    };

    // Equipped
    const equippedDiv = document.getElementById('equipped-artifacts');
    let eqHtml = '';
    for (const [slot, name] of Object.entries(SLOT_NAMES)) {
        const artId = state.equippedSlots[slot];
        const art = artId ? (gameData.artifacts || []).find(a => a.id === artId) : null;
        eqHtml += `<div class="equipped-slot" oncontextmenu="event.preventDefault(); ${art ? `showArtifactDescription('${art.id}')` : ''}">
            <div class="slot-name">${name}</div>
            ${art ? `
                ${art.has_icon ? `<img src="/api/icon/artifact_${art.id}" onerror="this.style.display='none'">` : ''}
                <div class="art-name">${escapeHtml(art.name)}</div>
                <button class="remove-btn" onclick="removeArtifact('${slot}')">x</button>
            ` : '<div style="color:#555;margin-top:20px">-</div>'}
        </div>`;
    }
    equippedDiv.innerHTML = eqHtml;

    // Fix 8: Reroll button
    const rerollDiv = document.getElementById('reroll-artifacts');
    if (rerollDiv) {
        rerollDiv.innerHTML = `<button onclick="rerollArtifacts()" class="reroll-btn">Реролл артефактов (10,000)</button>`;
    }

    // Shop (Fix 8: description hidden, shown on right-click)
    const shopDiv = document.getElementById('artifact-shop');
    let shopHtml = '';
    const groups = [
        { title: 'Minor', items: state.artifactShop.filter(a => a.type === 'ARTF_CLASS_MINOR'), cls: 'minor' },
        { title: 'Major', items: state.artifactShop.filter(a => a.type === 'ARTF_CLASS_MAJOR'), cls: 'major' },
        { title: 'Relic', items: state.artifactShop.filter(a => a.type === 'ARTF_CLASS_RELIC'), cls: 'relic' },
    ];
    for (const g of groups) {
        shopHtml += `<h4>${g.title}</h4><div class="artifact-grid">`;
        for (const a of g.items) {
            const bought = state.artifacts.includes(a.id);
            // Fix 8: Check both FINGER slots
            const artSlot = a.slot === 'FINGER' ? getFreeFingerSlot() : a.slot;
            const slotFull = !artSlot && !bought;
            shopHtml += `<div class="artifact-card ${g.cls}" onclick="buyArtifact('${a.id}')"
                oncontextmenu="event.preventDefault(); showArtifactDescription('${a.id}')"
                style="${bought || slotFull ? 'opacity:0.4;pointer-events:none' : ''}">
                <div class="art-header">
                    ${a.has_icon ? `<img src="/api/icon/artifact_${a.id}" onerror="this.style.display='none'">` : ''}
                    <div>
                        <div class="name">${escapeHtml(a.name)}</div>
                        <div class="slot-type">${SLOT_NAMES[a.slot] || a.slot}</div>
                    </div>
                </div>
                <div class="cost">${a.cost.toLocaleString()} zol</div>
            </div>`;
        }
        shopHtml += '</div>';
    }
    shopDiv.innerHTML = shopHtml;
}

// Fix 8: Get free FINGER slot (check FINGER and FINGER2)
function getFreeFingerSlot() {
    if (!state.equippedSlots['FINGER']) return 'FINGER';
    if (!state.equippedSlots['FINGER2']) return 'FINGER2';
    return null;
}

// Fix 8: Show artifact description on right-click
function showArtifactDescription(artId) {
    const art = (gameData.artifacts || []).find(a => a.id === artId);
    if (!art) return;
    showDescriptionModal(art.name, art.description || 'Нет описания');
}

function buyArtifact(artId) {
    const art = (gameData.artifacts || []).find(a => a.id === artId);
    if (!art) return;
    if (state.gold < art.cost) { alert('Недостаточно золота!'); return; }

    // Fix 8: Handle FINGER slots — auto-pick free one
    let targetSlot = art.slot;
    if (art.slot === 'FINGER') {
        targetSlot = getFreeFingerSlot();
        if (!targetSlot) { alert('Оба слота колец заняты!'); return; }
    } else {
        if (state.equippedSlots[art.slot]) { alert('Слот занят!'); return; }
    }

    state.gold -= art.cost;
    state.artifacts.push(art.id);
    state.equippedSlots[targetSlot] = art.id;
    renderArtifacts();
    updateUI();
}

function removeArtifact(slot) {
    const artId = state.equippedSlots[slot];
    if (!artId) return;
    const art = (gameData.artifacts || []).find(a => a.id === artId);
    if (art) state.gold += art.cost;
    state.artifacts = state.artifacts.filter(a => a !== artId);
    delete state.equippedSlots[slot];
    renderArtifacts();
    updateUI();
}

// ---- SPELLS ----
// Fix 9: Complete spell guild logic with skill dependencies

function getSkillMastery(skillId) {
    const sk = state.skills.find(s => s.skill_id === skillId);
    return sk ? sk.mastery : null;
}

function hasWisdom() {
    return !!state.skills.find(s => s.skill_id === 'HERO_SKILL_WISDOM');
}

function getSchoolSkillId(school) {
    const map = {
        'MAGIC_SCHOOL_LIGHT': 'HERO_SKILL_LIGHT_MAGIC',
        'MAGIC_SCHOOL_DARK': 'HERO_SKILL_DARK_MAGIC',
        'MAGIC_SCHOOL_DESTRUCTIVE': 'HERO_SKILL_DESTRUCTIVE_MAGIC',
        'MAGIC_SCHOOL_SUMMONING': 'HERO_SKILL_SUMMONING_MAGIC',
    };
    return map[school] || null;
}

function canAccessSpellCircle(school, circle) {
    if (circle <= 2) return true; // Circles 1-2 always available
    const schoolSkillId = getSchoolSkillId(school);
    const mastery = schoolSkillId ? getSkillMastery(schoolSkillId) : null;
    const wisdom = hasWisdom();

    if (circle === 3) return mastery || wisdom;
    if (circle === 4) return mastery === 'MASTERY_ADVANCED' || mastery === 'MASTERY_EXPERT' || wisdom;
    if (circle === 5) return mastery === 'MASTERY_EXPERT';
    return false;
}

function generateGuildSpells() {
    const factionSchools = gameData.faction_schools[myFaction] || [];
    const allSpells = gameData.spells || [];
    const guild = {};

    // Fix 9: Filter out MASS and EMPOWERED spells
    const filteredSpells = allSpells.filter(s => !s.is_mass && !s.is_empowered);

    if (myFaction === 'Orcs') {
        // Orcs get war cries only
        for (let floor = 1; floor <= 3; floor++) {
            const cries = filteredSpells.filter(s => s.school === 'MAGIC_SCHOOL_WARCRIES' && s.level === floor);
            // Fix 9: Level 1 warcries = 2 slots, level 2/3 = 1 slot
            if (floor === 1) {
                shuffle(cries);
                guild[floor] = cries.slice(0, 2);
            } else {
                shuffle(cries);
                guild[floor] = cries.slice(0, 1);
            }
        }
    } else {
        const isAcademy = myFaction === 'Academy';
        const isDwarf = myFaction === 'Dwarf';
        const nativeSpells = filteredSpells.filter(s => factionSchools.includes(s.school));
        const foreignSchools = ['MAGIC_SCHOOL_LIGHT', 'MAGIC_SCHOOL_DARK', 'MAGIC_SCHOOL_DESTRUCTIVE', 'MAGIC_SCHOOL_SUMMONING']
            .filter(s => !factionSchools.includes(s));
        const foreignSpells = filteredSpells.filter(s => foreignSchools.includes(s.school));

        for (let floor = 1; floor <= 5; floor++) {
            const floorSpells = [];

            // Fix 9: Check if hero can access this circle for each school
            for (const school of factionSchools) {
                if (canAccessSpellCircle(school, floor)) {
                    const schoolFloor = nativeSpells.filter(s => s.school === school && s.level === floor);
                    shuffle(schoolFloor);
                    floorSpells.push(...schoolFloor.slice(0, 1)); // 1 per native school
                }
            }

            // Foreign spells for floors 1-3 (or all for Academy)
            if (floor <= 3 || isAcademy) {
                // Pick from a foreign school where hero has access
                const accessibleForeign = foreignSpells.filter(s =>
                    s.level === floor && canAccessSpellCircle(s.school, floor)
                );
                shuffle(accessibleForeign);
                const foreignCount = isAcademy ? 2 : 1;
                floorSpells.push(...accessibleForeign.slice(0, foreignCount));
            }

            // Fix 9: Dwarves — runes depend on skill level
            if (isDwarf) {
                const runeMastery = getSkillMastery('HERO_SKILL_RUNELORE');
                const racialMastery = getSkillMastery(getClassSkillId());
                let maxRuneCircle = 2; // Default: circles 1-2 always
                if (racialMastery === 'MASTERY_EXPERT') maxRuneCircle = 5;
                else if (runeMastery === 'MASTERY_ADVANCED' || runeMastery === 'MASTERY_EXPERT') maxRuneCircle = 4;
                else if (runeMastery === 'MASTERY_BASIC') maxRuneCircle = 3;

                if (floor <= maxRuneCircle) {
                    const runes = filteredSpells.filter(s => s.school === 'MAGIC_SCHOOL_RUNIC' && s.level === floor);
                    floorSpells.push(...runes);
                }
            }

            guild[floor] = floorSpells;
        }
    }

    // Fix 9: Add starting spells
    state.guildSpells = guild;
}

function renderSpells() {
    // Regenerate based on current skills (Fix 9)
    generateGuildSpells();

    if (myFaction === 'Orcs') {
        document.getElementById('spells-title').textContent = 'Кличи';
    } else if (myFaction === 'Dwarf') {
        document.getElementById('spells-title').textContent = 'Гильдия магов + Руны';
    } else {
        document.getElementById('spells-title').textContent = 'Гильдия магов';
    }

    const floorsDiv = document.getElementById('spell-floors');
    let html = '';

    // Fix 9: Show starting spells first
    if (state.hero && state.hero.starting_spells && state.hero.starting_spells.length) {
        html += '<div class="spell-floor"><h4>Стартовые заклинания</h4><div class="spell-grid">';
        for (const sid of state.hero.starting_spells) {
            const s = (gameData.spells || []).find(sp => sp.id === sid);
            if (!s) continue;
            const learned = state.spells.includes(s.id);
            html += `<div class="spell-card ${learned ? 'learned' : ''}" onclick="toggleSpell('${s.id}')"
                oncontextmenu="event.preventDefault(); showDescriptionModal('${escapeHtml(s.name)}', '${escapeHtml(s.description || '')}')">
                ${s.has_icon ? `<img src="/api/icon/spell_${s.id}" onerror="this.style.display='none'">` : ''}
                <div class="name">${escapeHtml(s.name)}</div>
                <div class="school">${getSchoolName(s.school)}</div>
                <div class="mana">MP: ${s.mana_cost}</div>
            </div>`;
        }
        html += '</div></div>';
    }

    for (let floor = 1; floor <= 5; floor++) {
        const spells = state.guildSpells[floor] || [];
        if (!spells.length) continue;
        html += `<div class="spell-floor"><h4>Круг ${floor}</h4><div class="spell-grid">`;
        for (const s of spells) {
            const learned = state.spells.includes(s.id);
            html += `<div class="spell-card ${learned ? 'learned' : ''}" onclick="toggleSpell('${s.id}')"
                oncontextmenu="event.preventDefault(); showSpellDescription('${s.id}')">
                ${s.has_icon ? `<img src="/api/icon/spell_${s.id}" onerror="this.style.display='none'">` : ''}
                <div class="name">${escapeHtml(s.name)}</div>
                <div class="school">${getSchoolName(s.school)}</div>
                <div class="mana">MP: ${s.mana_cost}</div>
            </div>`;
        }
        html += '</div></div>';
    }
    floorsDiv.innerHTML = html;

    // Learned spells list
    const learnedDiv = document.getElementById('learned-spells-list');
    learnedDiv.innerHTML = state.spells.map(sid => {
        const s = (gameData.spells || []).find(sp => sp.id === sid);
        return `<span class="learned-spell-tag">${escapeHtml(s ? s.name : sid)}</span>`;
    }).join('');
}

function showSpellDescription(spellId) {
    const s = (gameData.spells || []).find(sp => sp.id === spellId);
    if (s) showDescriptionModal(s.name, s.description || 'Нет описания');
}

function getSchoolName(school) {
    const names = {
        'MAGIC_SCHOOL_LIGHT': 'Свет', 'MAGIC_SCHOOL_DARK': 'Тьма',
        'MAGIC_SCHOOL_DESTRUCTIVE': 'Хаос', 'MAGIC_SCHOOL_SUMMONING': 'Призыв',
        'MAGIC_SCHOOL_RUNIC': 'Руны', 'MAGIC_SCHOOL_WARCRIES': 'Кличи',
    };
    return names[school] || school;
}

function toggleSpell(spellId) {
    if (state.spells.includes(spellId)) {
        state.spells = state.spells.filter(s => s !== spellId);
    } else {
        state.spells.push(spellId);
    }
    renderSpells();
}

// ---- READY ----
function playerReady() {
    const expTable = gameData.exp_table || {};
    const experience = expTable[String(state.level)] || 0;

    const heroName = state.hero ? state.hero.name : 'Hero';
    const heroShared = state.hero ? state.hero.shared_href : '';

    const build = {
        hero: { name: heroName, shared_href: heroShared },
        stats: state.stats,
        experience: experience,
        army: state.army.filter(s => s).map(s => ({
            creature_id: s.creature_id, count: s.count
        })),
        artifacts: state.artifacts,
        skills: state.skills.map(s => ({
            skill_id: s.skill_id, mastery: s.mastery,
            is_class_skill: s.skill_id === getClassSkillId(),
        })),
        perks: state.perks,
        spells: state.spells,
        ballista: false,
        first_aid_tent: false,
        ammo_cart: false,
    };

    if (isOffline) {
        socket.emit('player_ready', {
            room_id: roomId, build: build, player_num: 1,
            build_p2: generateAutoP2Build(),
        });
    } else {
        socket.emit('player_ready', { room_id: roomId, build: build });
    }

    document.getElementById('ready-btn').disabled = true;
    document.getElementById('ready-btn').textContent = 'Ожидание...';
}

function generateAutoP2Build() {
    return {
        hero: { name: 'AI Player', shared_href: '' },
        stats: { offence: 5, defence: 5, spellpower: 5, knowledge: 5 },
        experience: 81961,
        army: [], artifacts: [], skills: [], perks: [], spells: [],
        ballista: false, first_aid_tent: false, ammo_cart: false,
    };
}

function autoFillP2() {
    closeP2Modal();
}

function closeP2Modal() {
    document.getElementById('offline-p2-modal').style.display = 'none';
}
