/* ── ChaosPlane Extension — viewer.js ───────────────────────────────────────
 *
 * Responsibilities:
 *   - Load failure catalogue from GitHub Pages
 *   - Render tier buttons and browsable failure list
 *   - Disable currently active failures
 *   - Handle bits transactions via Twitch Extension JS helper
 *   - Post confirmed triggers to the EBS
 *
 * Bit costs are defined in BITS_COST below — update to match your
 * Twitch Extension Products once configured.
 * ────────────────────────────────────────────────────────────────────────── */

const EBS_URL      = 'https://chaosplane-production.up.railway.app';
const CATALOGUE_URL = 'https://quassbutreally.github.io/ChaosPlane/FailureCatalogue.json';

const BITS_COST = {
    Minor:    100,
    Moderate: 500,
    Severe:   1000,
    Specific: 200   // flat cost for browsing a specific failure
};

document.addEventListener('DOMContentLoaded', async () => {
    const dot      = document.getElementById('statusDot');
    const subtitle = document.querySelector('.subtitle');

    // Check Twitch ext
    if (typeof window.Twitch === 'undefined') {
        subtitle.textContent = '// TWITCH EXT NOT LOADED';
        dot.style.background = 'red';
    } else {
        subtitle.textContent = '// TWITCH EXT OK';
        dot.style.background = 'orange';
    }

    // Test catalogue fetch independently of Twitch auth
    try {
        const res  = await fetch(CATALOGUE_URL);
        const json = await res.json();
        catalogue  = (json.Failures || []).map(f => ({
            id:       f.Id,
            name:     f.Name,
            category: f.Category,
            tier:     f.SuggestedTier || null
        }));
        subtitle.textContent += ` | CAT: ${catalogue.length}`;
        renderFailureList('');
        updateBitsCosts();
    } catch (e) {
        subtitle.textContent += ` | CAT FAIL: ${e.message}`;
    }
});

document.getElementById('tabTier').addEventListener('click', () => switchTab('tier'));
document.getElementById('tabBrowse').addEventListener('click', () => switchTab('browse'));

document.getElementById('tier-btn-minor').addEventListener('click', () => selectTier('minor'));
document.getElementById('tier-btn-moderate').addEventListener('click', () => selectTier('moderate'));
document.getElementById('tier-btn-severe').addEventListener('click', () => selectTier('severe'));


// ── State ─────────────────────────────────────────────────────────────────────

let catalogue      = [];   // all ResolvedFailure entries
let activeFailures = [];   // failure IDs currently active (pushed from EBS via pubsub)
let twitchAuth     = null; // Twitch auth context (userId, token)
let pendingTrigger = null; // { type: 'tier'|'specific', tier?, failureId?, name, cost }

// ── Twitch helper init ────────────────────────────────────────────────────────

window.Twitch.ext.onAuthorized(auth => {
    twitchAuth = auth;
    setConnected(true);
    loadCatalogue();
});

window.Twitch.ext.onContext((ctx, delta) => {
    // Nothing needed from context for now
});

// Listen for pubsub messages from EBS (active failure updates)
window.Twitch.ext.listen('broadcast', (target, contentType, message) => {
    try {
        const data = JSON.parse(message);
        if (data.type === 'active_failures') {
            activeFailures = data.failureIds || [];
            renderFailureList(document.getElementById('searchInput').value);
        }
    } catch (e) {
        // Ignore malformed messages
    }
});

// ── Catalogue loading ─────────────────────────────────────────────────────────

async function loadCatalogue() {
    try {
        const res  = await fetch(CATALOGUE_URL);
        const json = await res.json();

        catalogue = (json.Failures || []).map(f => ({
            id:       f.Id,
            name:     f.Name,
            category: f.Category,
            tier:     f.SuggestedTier || null
        }));

        console.log('fetch status:', res.status);
        console.log('catalogue length:', catalogue.length);

        renderFailureList('');
        updateBitsCosts();
    } catch (e) {
        console.error('Failed to load catalogue:', e);
    }
}

// ── Tab switching ─────────────────────────────────────────────────────────────

function switchTab(tab) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.pane').forEach(p => p.classList.remove('active'));
    document.getElementById('tab'   + cap(tab)).classList.add('active');
    document.getElementById('pane'  + cap(tab)).classList.add('active');
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
            item.onclick = () => selectSpecific(failure);
        }

        list.appendChild(item);
    }
}

function selectSpecific(failure) {
    if (!twitchAuth) return;

    pendingTrigger = {
        type:      'specific',
        failureId: failure.id,
        name:      failure.name,
        cost:      BITS_COST.Specific
    };

    showModal('PICK YOUR POISON', failure.name, BITS_COST.Specific);
}

// ── Confirm modal ─────────────────────────────────────────────────────────────

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

    // Use Twitch bits API to charge the viewer
    window.Twitch.ext.bits.useBits(getProductSku(pendingTrigger), {
        onFulfilled: () => {
            sendTriggerToEbs(pendingTrigger);
            closeModal();
        },
        onCancelled: () => {
            closeModal();
        }
    });
}

function getProductSku(trigger) {
    if (trigger.type === 'specific') return 'chaosplane_specific';
    return `chaosplane_${trigger.tier.toLowerCase()}`;
}

// ── EBS communication ─────────────────────────────────────────────────────────

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
    } catch (e) {
        console.error('Failed to send trigger:', e);
    }
}

// ── UI helpers ────────────────────────────────────────────────────────────────

function setConnected(connected) {
    const dot = document.getElementById('statusDot');
    dot.className = 'status-dot ' + (connected ? 'connected' : 'error');
}

function escHtml(str) {
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}