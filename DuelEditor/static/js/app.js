/* Heroes 5 Duel Preset Editor - Frontend Logic */

let allCreatures = {};       // {faction: [creature, ...]}
let factions = [];            // [{name, creature_count}]
let presetState = {
    name: "",
    player1: {
        name: "Игрок 1",
        hero: { level: 1, attack: 0, defense: 0, spellpower: 0, knowledge: 0 },
        army: [null, null, null, null, null, null, null]
    },
    player2: {
        name: "Игрок 2",
        hero: { level: 1, attack: 0, defense: 0, spellpower: 0, knowledge: 0 },
        army: [null, null, null, null, null, null, null]
    }
};

let currentModalPlayer = 0;
let currentModalSlot = 0;
let currentModalFaction = "";

/* ---------- Init ---------- */
document.addEventListener("DOMContentLoaded", async () => {
    await loadFactions();
    populateFactionSelectors();
});

async function loadFactions() {
    const resp = await fetch("/api/factions");
    factions = await resp.json();
}

async function loadAllCreatures() {
    if (Object.keys(allCreatures).length > 0) return;
    const resp = await fetch("/api/creatures");
    allCreatures = await resp.json();
}

function populateFactionSelectors() {
    for (const playerId of [1, 2]) {
        const select = document.getElementById(`player${playerId}-faction`);
        for (const f of factions) {
            const opt = document.createElement("option");
            opt.value = f.name;
            opt.textContent = `${f.name} (${f.creature_count})`;
            select.appendChild(opt);
        }
    }
}

/* ---------- Army Management ---------- */
function loadCreatures(playerId) {
    // Faction selector changed - just cosmetic, real selection in modal
}

function openCreatureSelector(playerId, slotIndex) {
    currentModalPlayer = playerId;
    currentModalSlot = slotIndex;
    loadAllCreatures().then(() => {
        renderModal();
        document.getElementById("creature-modal").style.display = "flex";
    });
}

function closeModal() {
    document.getElementById("creature-modal").style.display = "none";
    document.getElementById("creature-search-input").value = "";
}

function renderModal(filterFaction) {
    const tabsContainer = document.getElementById("modal-faction-tabs");
    tabsContainer.innerHTML = "";

    // "All" tab
    const allTab = document.createElement("div");
    allTab.className = `modal-tab ${!filterFaction ? "active" : ""}`;
    allTab.textContent = "Все";
    allTab.onclick = () => { currentModalFaction = ""; renderModal(); };
    tabsContainer.appendChild(allTab);

    for (const f of factions) {
        const tab = document.createElement("div");
        tab.className = `modal-tab ${filterFaction === f.name ? "active" : ""}`;
        tab.textContent = f.name;
        tab.onclick = () => { currentModalFaction = f.name; renderModal(f.name); };
        tabsContainer.appendChild(tab);
    }

    renderCreatureGrid(filterFaction);
}

function renderCreatureGrid(filterFaction) {
    const grid = document.getElementById("creature-grid");
    grid.innerHTML = "";

    const searchVal = document.getElementById("creature-search-input").value.toLowerCase();

    const factionsToShow = filterFaction ? [filterFaction] : Object.keys(allCreatures).sort();

    for (const faction of factionsToShow) {
        const creatures = allCreatures[faction] || [];
        for (const c of creatures) {
            if (searchVal && !c.name.toLowerCase().includes(searchVal) &&
                !c.id.toLowerCase().includes(searchVal)) {
                continue;
            }
            const card = createCreatureCard(c);
            grid.appendChild(card);
        }
    }
}

function createCreatureCard(creature) {
    const card = document.createElement("div");
    card.className = `creature-card ${creature.upgrade ? "upgrade-creature" : ""}`;

    const tierColor = `tier-${creature.tier}`;

    let iconHtml;
    if (creature.has_icon) {
        iconHtml = `<img src="/api/icon/${creature.id}" alt="${creature.name}" loading="lazy">`;
    } else {
        iconHtml = `<div class="no-icon">?</div>`;
    }

    card.innerHTML = `
        ${iconHtml}
        <div class="card-name">${creature.name}</div>
        <div class="card-tier ${tierColor}">Тир ${creature.tier} ${creature.upgrade ? "▲" : ""}</div>
        <div class="card-stats">
            <span>⚔${creature.attack}</span>
            <span>🛡${creature.defense}</span>
            <span>❤${creature.health}</span>
            <span>⚡${creature.speed}</span>
        </div>
        <div class="creature-count-input">
            <input type="number" min="1" max="9999" value="1" id="count-${creature.id}"
                   onclick="event.stopPropagation()">
            <button onclick="event.stopPropagation(); selectCreature('${creature.id}')">OK</button>
        </div>
    `;

    card.addEventListener("mouseenter", (e) => showTooltip(e, creature));
    card.addEventListener("mouseleave", hideTooltip);
    card.addEventListener("mousemove", moveTooltip);

    card.addEventListener("dblclick", () => {
        document.getElementById(`count-${creature.id}`).value = 1;
        selectCreature(creature.id);
    });

    return card;
}

function selectCreature(creatureId) {
    const countInput = document.getElementById(`count-${creatureId}`);
    const count = parseInt(countInput?.value) || 1;

    let creature = null;
    for (const faction of Object.values(allCreatures)) {
        for (const c of faction) {
            if (c.id === creatureId) {
                creature = c;
                break;
            }
        }
        if (creature) break;
    }

    if (!creature) return;

    const player = currentModalPlayer === 1 ? presetState.player1 : presetState.player2;
    player.army[currentModalSlot] = {
        id: creature.id,
        name: creature.name,
        count: count,
        tier: creature.tier,
        upgrade: creature.upgrade,
        has_icon: creature.has_icon,
        power: creature.power,
        faction: creature.faction
    };

    closeModal();
    renderArmySlots(currentModalPlayer);
    updateArmyPower(currentModalPlayer);
}

function removeCreature(playerId, slotIndex, event) {
    event.stopPropagation();
    const player = playerId === 1 ? presetState.player1 : presetState.player2;
    player.army[slotIndex] = null;
    renderArmySlots(playerId);
    updateArmyPower(playerId);
}

function renderArmySlots(playerId) {
    const player = playerId === 1 ? presetState.player1 : presetState.player2;
    const container = document.getElementById(`player${playerId}-army`);
    const slots = container.querySelectorAll(".army-slot");

    slots.forEach((slot, idx) => {
        const creature = player.army[idx];
        if (!creature) {
            slot.className = "army-slot";
            slot.innerHTML = `<div class="slot-empty">+</div>`;
            slot.onclick = () => openCreatureSelector(playerId, idx);
        } else {
            slot.className = "army-slot filled";
            const tierColor = `tier-${creature.tier}`;
            let iconHtml;
            if (creature.has_icon) {
                iconHtml = `<img src="/api/icon/${creature.id}" alt="${creature.name}">`;
            } else {
                iconHtml = `<div class="slot-no-icon">?</div>`;
            }
            slot.innerHTML = `
                <span class="slot-tier-badge ${tierColor}">${creature.tier}</span>
                <button class="slot-remove" onclick="removeCreature(${playerId}, ${idx}, event)">×</button>
                <div class="slot-creature">
                    ${iconHtml}
                    <div class="creature-name">${creature.name}</div>
                    <input type="number" class="creature-count" value="${creature.count}" min="1" max="9999"
                           onclick="event.stopPropagation()"
                           onchange="updateCreatureCount(${playerId}, ${idx}, this.value)"
                           style="width:50px;background:var(--bg-dark);border:1px solid var(--border);border-radius:3px;color:var(--accent-gold);text-align:center;font-size:11px;font-weight:bold;padding:1px;">
                </div>
            `;
            slot.onclick = () => openCreatureSelector(playerId, idx);
        }
    });
}

function updateCreatureCount(playerId, slotIndex, value) {
    const player = playerId === 1 ? presetState.player1 : presetState.player2;
    if (player.army[slotIndex]) {
        player.army[slotIndex].count = parseInt(value) || 1;
        updateArmyPower(playerId);
    }
}

function updateArmyPower(playerId) {
    const player = playerId === 1 ? presetState.player1 : presetState.player2;
    let totalPower = 0;
    for (const slot of player.army) {
        if (slot) {
            totalPower += slot.power * slot.count;
        }
    }
    document.getElementById(`player${playerId}-power`).textContent =
        `Сила армии: ${totalPower.toLocaleString()}`;
}

/* ---------- Tooltip ---------- */
function showTooltip(event, creature) {
    const tooltip = document.getElementById("creature-tooltip");

    let statsHtml = `
        <div class="tooltip-stats">
            <span class="stat-label">Атака:</span><span class="stat-value">${creature.attack}</span>
            <span class="stat-label">Защита:</span><span class="stat-value">${creature.defense}</span>
            <span class="stat-label">Урон:</span><span class="stat-value">${creature.min_damage}-${creature.max_damage}</span>
            <span class="stat-label">Здоровье:</span><span class="stat-value">${creature.health}</span>
            <span class="stat-label">Скорость:</span><span class="stat-value">${creature.speed}</span>
            <span class="stat-label">Инициатива:</span><span class="stat-value">${creature.initiative}</span>
            <span class="stat-label">Выстрелы:</span><span class="stat-value">${creature.shots || "—"}</span>
            <span class="stat-label">Полёт:</span><span class="stat-value">${creature.flying ? "Да" : "Нет"}</span>
            <span class="stat-label">Размер:</span><span class="stat-value">${creature.combat_size}</span>
            <span class="stat-label">Стоимость:</span><span class="stat-value">${creature.gold_cost} зол.</span>
            <span class="stat-label">Опыт:</span><span class="stat-value">${creature.exp}</span>
            <span class="stat-label">Сила:</span><span class="stat-value">${creature.power}</span>
        </div>
    `;

    let abilitiesHtml = "";
    if (creature.abilities && creature.abilities.length > 0) {
        const abilNames = creature.abilities.map(a =>
            a.replace("ABILITY_", "").replace(/_/g, " ").toLowerCase()
        ).join(", ");
        abilitiesHtml = `<div class="tooltip-abilities">Способности: ${abilNames}</div>`;
    }

    let descHtml = "";
    if (creature.description) {
        descHtml = `<div class="tooltip-desc">${creature.description}</div>`;
    }

    tooltip.innerHTML = `
        <h4>${creature.name} <span class="tier-${creature.tier}">(Тир ${creature.tier}${creature.upgrade ? " ▲" : ""})</span></h4>
        ${statsHtml}
        ${abilitiesHtml}
        ${descHtml}
    `;

    tooltip.style.display = "block";
    moveTooltip(event);
}

function moveTooltip(event) {
    const tooltip = document.getElementById("creature-tooltip");
    const x = event.clientX + 15;
    const y = event.clientY + 15;
    const maxX = window.innerWidth - tooltip.offsetWidth - 10;
    const maxY = window.innerHeight - tooltip.offsetHeight - 10;
    tooltip.style.left = Math.min(x, maxX) + "px";
    tooltip.style.top = Math.min(y, maxY) + "px";
}

function hideTooltip() {
    document.getElementById("creature-tooltip").style.display = "none";
}

/* ---------- Filter ---------- */
function filterCreatures() {
    renderCreatureGrid(currentModalFaction);
}

/* ---------- Hero Stats ---------- */
function updateHeroStats(playerId) {
    // Placeholder for future hero leveling mechanics
}

/* ---------- Preset State Management ---------- */
function gatherPresetState() {
    for (const p of [1, 2]) {
        const player = p === 1 ? presetState.player1 : presetState.player2;
        player.name = document.getElementById(`player${p}-name`).value;
        player.hero.level = parseInt(document.getElementById(`player${p}-hero-level`).value) || 1;
        player.hero.attack = parseInt(document.getElementById(`player${p}-hero-attack`).value) || 0;
        player.hero.defense = parseInt(document.getElementById(`player${p}-hero-defense`).value) || 0;
        player.hero.spellpower = parseInt(document.getElementById(`player${p}-hero-spellpower`).value) || 0;
        player.hero.knowledge = parseInt(document.getElementById(`player${p}-hero-knowledge`).value) || 0;
    }
    return presetState;
}

function applyPresetState(state) {
    presetState = state;
    for (const p of [1, 2]) {
        const player = p === 1 ? presetState.player1 : presetState.player2;
        document.getElementById(`player${p}-name`).value = player.name || `Игрок ${p}`;
        document.getElementById(`player${p}-hero-level`).value = player.hero?.level || 1;
        document.getElementById(`player${p}-hero-attack`).value = player.hero?.attack || 0;
        document.getElementById(`player${p}-hero-defense`).value = player.hero?.defense || 0;
        document.getElementById(`player${p}-hero-spellpower`).value = player.hero?.spellpower || 0;
        document.getElementById(`player${p}-hero-knowledge`).value = player.hero?.knowledge || 0;

        // Ensure army has 7 slots
        if (!player.army) player.army = [null, null, null, null, null, null, null];
        while (player.army.length < 7) player.army.push(null);

        renderArmySlots(p);
        updateArmyPower(p);
    }
}

/* ---------- Save/Load ---------- */
function savePreset() {
    const state = gatherPresetState();
    const dialog = document.getElementById("save-dialog");
    const title = document.getElementById("save-dialog-title");
    const body = document.getElementById("save-dialog-body");

    title.textContent = "Сохранить пресет";
    body.innerHTML = `
        <div class="save-dialog-content">
            <input type="text" id="save-preset-name" placeholder="Название пресета"
                   value="${state.name || ""}">
            <div class="save-dialog-actions">
                <button class="btn" onclick="closeSaveDialog()">Отмена</button>
                <button class="btn btn-save" onclick="doSavePreset()">Сохранить</button>
            </div>
        </div>
    `;
    dialog.style.display = "flex";
    document.getElementById("save-preset-name").focus();
}

async function doSavePreset() {
    const name = document.getElementById("save-preset-name").value.trim();
    if (!name) {
        alert("Введите название пресета");
        return;
    }
    const state = gatherPresetState();
    state.name = name;
    presetState.name = name;

    const resp = await fetch("/api/preset", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(state)
    });

    if (resp.ok) {
        closeSaveDialog();
    } else {
        alert("Ошибка сохранения");
    }
}

async function showLoadDialog() {
    const dialog = document.getElementById("save-dialog");
    const title = document.getElementById("save-dialog-title");
    const body = document.getElementById("save-dialog-body");

    title.textContent = "Загрузить пресет";

    const resp = await fetch("/api/presets");
    const presets = await resp.json();

    let listHtml = "";
    if (presets.length === 0) {
        listHtml = '<p style="color:var(--text-dim);text-align:center;padding:20px;">Нет сохранённых пресетов</p>';
    } else {
        listHtml = '<div class="preset-list">';
        for (const name of presets) {
            listHtml += `
                <div class="preset-item" onclick="doLoadPreset('${name}')">
                    <span class="preset-name">${name}</span>
                    <button class="preset-delete" onclick="event.stopPropagation(); doDeletePreset('${name}')">&times;</button>
                </div>
            `;
        }
        listHtml += '</div>';
    }

    body.innerHTML = `<div class="save-dialog-content">${listHtml}</div>`;
    dialog.style.display = "flex";
}

async function doLoadPreset(name) {
    const resp = await fetch(`/api/preset/${encodeURIComponent(name)}`);
    if (resp.ok) {
        const data = await resp.json();
        applyPresetState(data);
        closeSaveDialog();
    } else {
        alert("Ошибка загрузки");
    }
}

async function doDeletePreset(name) {
    if (!confirm(`Удалить пресет "${name}"?`)) return;
    await fetch(`/api/preset/${encodeURIComponent(name)}`, { method: "DELETE" });
    showLoadDialog();
}

function closeSaveDialog() {
    document.getElementById("save-dialog").style.display = "none";
}

/* ---------- Export/Import ---------- */
function exportPreset() {
    const state = gatherPresetState();
    const json = JSON.stringify(state, null, 2);
    const blob = new Blob([json], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${state.name || "duel_preset"}.json`;
    a.click();
    URL.revokeObjectURL(url);
}

function importPreset() {
    document.getElementById("import-file-input").click();
}

function handleImport(event) {
    const file = event.target.files[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (e) => {
        try {
            const data = JSON.parse(e.target.result);
            applyPresetState(data);
        } catch (err) {
            alert("Ошибка чтения файла: " + err.message);
        }
    };
    reader.readAsText(file);
    event.target.value = "";
}

/* ---------- Reset ---------- */
function resetPreset() {
    if (!confirm("Сбросить все настройки?")) return;
    presetState = {
        name: "",
        player1: {
            name: "Игрок 1",
            hero: { level: 1, attack: 0, defense: 0, spellpower: 0, knowledge: 0 },
            army: [null, null, null, null, null, null, null]
        },
        player2: {
            name: "Игрок 2",
            hero: { level: 1, attack: 0, defense: 0, spellpower: 0, knowledge: 0 },
            army: [null, null, null, null, null, null, null]
        }
    };
    applyPresetState(presetState);
}
