/* ── ChaosPlane Extension — viewer.js ─────────────────────────────────────── */

const EBS_URL       = 'https://chaosplane-production.up.railway.app';
const CATALOGUE_URL = 'https://quassbutreally.github.io/ChaosPlane/FailureCatalogue.json';

const BITS_COST = {
    Minor:    100,
    Moderate: 500,
    Severe:   1000,
};

// ── State ─────────────────────────────────────────────────────────────────────

let catalogue      = [];
let activeFailures = [];
let twitchAuth     = null;
let pendingTrigger = null;

// ── Init ──────────────────────────────────────────────────────────────────────

// Wire up tab buttons
document.getElementById('tabTier').addEventListener('click',   () => switchTab('tier'));
document.getElementById('tabBrowse').addEventListener('click', () => switchTab('browse'));

// Wire up tier buttons
document.getElementById('btnMinor').addEventListener('click',    () => selectTier('Minor'));
document.getElementById('btnModerate').addEventListener('click', () => selectTier('Moderate'));
document.getElementById('btnSevere').addEventListener('click',   () => selectTier('Severe'));

// Wire up modal buttons
document.getElementById('btnCancel').addEventListener('click',  closeModal);
document.getElementById('btnConfirm').addEventListener('click', confirmTrigger);

// Wire up search bar
document.getElementById('searchInput').addEventListener('input', e => filterFailures(e.target.value));

// Load catalogue and initialise status checking every 30 seconds
loadCatalogue();

checkStatus();
setInterval(checkStatus, 30000);

// ── Twitch helper ─────────────────────────────────────────────────────────────

window.Twitch.ext.onAuthorized(auth => {
    twitchAuth = auth;
    setConnected(true);
});

window.Twitch.ext.listen('broadcast', (target, contentType, message) => {
    try {
        const data = JSON.parse(message);
        if (data.type === 'active_failures') {
            activeFailures = data.failureIds || [];
            renderFailureList(document.getElementById('searchInput').value);
        }
    } catch (e) {}
});

// ── OnlineChecking ─────────────────────────────────────────────────────────────────

async function checkStatus() {
    try {
        const res  = await fetch(`${EBS_URL}/status`);
        const json = await res.json();
        setOnline(json.online);
    } catch (e) {
        setOnline(false);
    }
}

function setOnline(online) {
    document.getElementById('offlineOverlay').classList.toggle('visible', !online);
    document.querySelector('.subtitle').textContent = online
        ? '// CL650 FAILURE SYSTEM'
        : '// STREAMER OFFLINE';
}

// ── Catalogue ─────────────────────────────────────────────────────────────────

async function loadCatalogue() {
    try {
        const res  = await fetch(CATALOGUE_URL);
        const json = await res.json();

        catalogue = (json.Failures || []).map(f => ({
            id:       f.Id,
            name:     f.Name,
            category: f.Category,
            tier:     f.SuggestedTier || null,
            description: f.Description || ''
        }));

        renderFailureList('');
        updateBitsCosts();
    } catch (e) {}
}

// ── Tabs ──────────────────────────────────────────────────────────────────────

function switchTab(tab) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.pane').forEach(p => p.classList.remove('active'));
    document.getElementById('tab'  + cap(tab)).classList.add('active');
    document.getElementById('pane' + cap(tab)).classList.add('active');
}

function cap(s) { return s.charAt(0).toUpperCase() + s.slice(1); }

// ── Tier buttons ──────────────────────────────────────────────────────────────

function selectTier(tier) {
    if (!twitchAuth) return;

    pendingTrigger = {
        type: 'tier',
        tier: tier,
        name: `Random ${tier} Failure`,
        cost: BITS_COST[tier]
    };

    showModal(`RANDOM ${tier.toUpperCase()}`, pendingTrigger.name, pendingTrigger.cost);
}

function updateBitsCosts() {
    document.getElementById('costMinor').textContent    = `${BITS_COST.Minor} bits`;
    document.getElementById('costModerate').textContent = `${BITS_COST.Moderate} bits`;
    document.getElementById('costSevere').textContent   = `${BITS_COST.Severe} bits`;
}

// ── Failure list ──────────────────────────────────────────────────────────────

function filterFailures(query) {
    renderFailureList(query);
}

function renderFailureList(query) {
    const list = document.getElementById('failureList');
    list.innerHTML = '';

    const q = (query || '').toLowerCase().trim();

    const filtered = q
        ? catalogue.filter(f =>
            f.name.toLowerCase().includes(q) ||
            f.category.toLowerCase().includes(q))
        : catalogue;

    if (filtered.length === 0) {
        list.innerHTML = '<div class="empty-state">NO FAILURES MATCH</div>';
        return;
    }

    for (const failure of filtered.slice(0, 100)) {
        const isActive = activeFailures.includes(failure.id);
        const tier     = (failure.tier || '').toLowerCase();

        const item = document.createElement('div');
        item.className = 'failure-item' + (isActive ? ' active-failure' : '');

        item.innerHTML = `
      <div class="failure-item-left">
        <div class="failure-name">${escHtml(failure.name)}</div>
        <div class="failure-category">${escHtml(failure.category)}</div>
      </div>
      <div class="tier-badge ${tier || 'none'}">${tier ? tier.toUpperCase() : '—'}</div>
    `;

        if (!isActive) {
            item.addEventListener('click', () => selectSpecific(failure));
            item.addEventListener('mouseenter', () => {
                document.getElementById('descBarText').textContent = failure.description || '—';
            });
            item.addEventListener('mouseleave', () => {
                document.getElementById('descBarText').textContent = '// HOVER A FAILURE FOR DETAILS';
            });
        }

        list.appendChild(item);
    }
}

function selectSpecific(failure) {
    if (!twitchAuth) return;

    const tier = failure.tier || 'Moderate';
    const cost = BITS_COST[tier] || BITS_COST.Moderate;

    pendingTrigger = {
        type:      'specific',
        failureId: failure.id,
        tier:      tier,
        name:      failure.name,
        cost:      cost
    };

    showModal('PICK YOUR POISON', failure.name, cost);
}

// ── Modal ─────────────────────────────────────────────────────────────────────

function showModal(title, name, cost) {
    document.getElementById('modalTitle').textContent       = title;
    document.getElementById('modalFailureName').textContent = name;
    document.getElementById('modalCost').textContent        = `${cost} bits`;
    document.getElementById('modalOverlay').classList.add('visible');
}

function closeModal() {
    document.getElementById('modalOverlay').classList.remove('visible');
    pendingTrigger = null;
}

function confirmTrigger() {
    if (!pendingTrigger || !twitchAuth) return;

    window.Twitch.ext.bits.useBits(getProductSku(pendingTrigger), {
        onFulfilled: () => { sendTriggerToEbs(pendingTrigger); closeModal(); },
        onCancelled: () => { closeModal(); }
    });

    onFulfilled: () => {
        sendTriggerToEbs(pendingTrigger);
        showToast('✈ FAILURE TRIGGERED');
        closeModal();
    }
}

function getProductSku(trigger) {
    const tier = (trigger.tier || 'Moderate').toLowerCase();
    return `chaosplane_${tier}`;
}

// ── EBS ───────────────────────────────────────────────────────────────────────

async function sendTriggerToEbs(trigger) {
    if (!twitchAuth) return;

    try {
        const body = trigger.type === 'tier'
            ? { tier: trigger.tier, viewerName: twitchAuth.channelId, bitsSpent: trigger.cost }
            : { failureId: trigger.failureId, viewerName: twitchAuth.channelId, bitsSpent: trigger.cost };

        await fetch(`${EBS_URL}/trigger`, {
            method:  'POST',
            headers: {
                'Content-Type':  'application/json',
                'Authorization': `Bearer ${twitchAuth.token}`
            },
            body: JSON.stringify(body)
        });
    } catch (e) {}
}

// ── Toast ───────────────────────────────────────────────────────────────────
function showToast(message) {
    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.textContent = message;
    document.body.appendChild(toast);

    setTimeout(() => toast.classList.add('visible'), 10);
    setTimeout(() => {
        toast.classList.remove('visible');
        setTimeout(() => toast.remove(), 300);
    }, 2500);
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function setConnected(connected) {
    document.getElementById('statusDot').className =
        'status-dot ' + (connected ? 'connected' : 'error');
}

function escHtml(str) {
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}