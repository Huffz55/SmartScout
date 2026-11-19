import {
  enrichBiosApi,
  fetchSyncStatusApi,
  getSyncApiKey,
  setSyncApiKey,
  syncInjuriesApi,
  syncLeagueStatsApi
} from '../api.js';
import { SYNC_LEAGUES, state } from '../state.js';
import { escapeHtml, formatRelativeUtc } from '../utils/formatters.js';

export function initSyncDrawer() {
  document.getElementById('btn-sync-pill')?.addEventListener('click', openSyncDrawer);
  document.getElementById('btn-sync-close')?.addEventListener('click', closeSyncDrawer);
  document.getElementById('sync-modal')?.addEventListener('click', (e) => {
    if (e.target.id === 'sync-modal') closeSyncDrawer();
  });

  document.getElementById('btn-save-apikey')?.addEventListener('click', saveApiKey);
  document.getElementById('btn-clear-apikey')?.addEventListener('click', clearApiKey);
  document.getElementById('sync-league-filter')?.addEventListener('input', (e) => renderSyncLeagues(e.target.value));
  document.getElementById('btn-sync-toggle-all')?.addEventListener('click', toggleAllLeagues);
  document.getElementById('btn-sync-run')?.addEventListener('click', triggerSyncSelected);
  document.getElementById('btn-enrich-bios')?.addEventListener('click', triggerBioEnrichment);
  document.getElementById('btn-sync-injuries')?.addEventListener('click', triggerInjurySync);

  updateApiKeyBadge();
  renderSyncLeagues();
  refreshSyncStatus();
}

export function openSyncDrawer() {
  document.getElementById('sync-modal')?.classList.remove('hidden');
  refreshSyncStatus();
}

export function closeSyncDrawer() {
  document.getElementById('sync-modal')?.classList.add('hidden');
}

function updateApiKeyBadge() {
  const key = getSyncApiKey();
  const input = document.getElementById('sync-api-key-input');
  const badge = document.getElementById('sync-apikey-status');
  if (input && key) input.value = key;
  if (badge) {
    badge.className = key ? 'badge badge-emerald' : 'badge badge-amber';
    badge.textContent = key ? 'Key Ready' : 'Key Required';
  }
}

function saveApiKey() {
  const input = document.getElementById('sync-api-key-input');
  setSyncApiKey((input?.value || '').trim());
  updateApiKeyBadge();
}

function clearApiKey() {
  setSyncApiKey('');
  const input = document.getElementById('sync-api-key-input');
  if (input) input.value = '';
  updateApiKeyBadge();
}

function logConsole(msg) {
  const box = document.getElementById('sync-console');
  if (!box) return;
  box.textContent += `\n[${new Date().toLocaleTimeString()}] ${msg}`;
  box.scrollTop = box.scrollHeight;
}

function requireKey() {
  const key = getSyncApiKey();
  if (!key) {
    logConsole('ERROR: X-Api-Key is required for write/sync operations.');
    return null;
  }
  return key;
}

export async function refreshSyncStatus() {
  try {
    const data = await fetchSyncStatusApi();
    state.sync.status = data;

    const pillText = document.getElementById('sync-pill-text');
    if (pillText) {
      pillText.textContent = `${data.liveSeasonYear || 2027} • ${Number(data.totalPlayersCount || 0).toLocaleString()} Players`;
    }

    const injEl = document.getElementById('sync-stat-injuries');
    const lastSeasonEl = document.getElementById('sync-stat-season');
    const covEl = document.getElementById('sync-stat-coverage');
    if (injEl) injEl.textContent = data.enableLiveInjuryAutoSync ? `Every ${data.injurySyncIntervalHours}h (${data.activeInjuriesCount} active)` : 'Paused';
    if (lastSeasonEl) lastSeasonEl.textContent = `${data.liveSeasonYear || 2027} • ${formatRelativeUtc(data.lastSeasonStatSyncAt)}`;
    if (covEl) covEl.textContent = `${Number(data.totalPlayersCount || 0).toLocaleString()} players / ${Number(data.liveSeasonStatRowsCount || 0).toLocaleString()} live rows`;
  } catch {
    const pillText = document.getElementById('sync-pill-text');
    if (pillText) pillText.textContent = 'Sync Status';
  }
}

function renderSyncLeagues(filter = '') {
  const grid = document.getElementById('sync-leagues-grid');
  if (!grid) return;
  const q = filter.trim().toLowerCase();
  const list = SYNC_LEAGUES.filter(l => !q || l.code.toLowerCase().includes(q) || l.name.toLowerCase().includes(q));

  grid.innerHTML = list.map(l => {
    const checked = state.sync.selectedLeagues.has(l.code);
    return `
      <label style="display:flex;align-items:center;justify-content:space-between;gap:6px;padding:6px 8px;border-radius:6px;background:var(--bg-canvas);border:1px solid ${checked ? 'rgba(56,189,248,0.45)' : 'var(--border-subtle)'};cursor:pointer;font-size:11.5px">
        <span style="display:flex;align-items:center;gap:6px">
          <input type="checkbox" data-league-code="${l.code}" ${checked ? 'checked' : ''} />
          <span>${l.flag} <strong>${l.code}</strong></span>
        </span>
        <span class="mono" style="color:var(--text-muted);font-size:10.5px">${l.coeff}x</span>
      </label>
    `;
  }).join('');

  grid.querySelectorAll('input[data-league-code]').forEach(cb => {
    cb.addEventListener('change', () => {
      if (cb.checked) state.sync.selectedLeagues.add(cb.dataset.leagueCode);
      else state.sync.selectedLeagues.delete(cb.dataset.leagueCode);
    });
  });
}

function toggleAllLeagues() {
  if (state.sync.selectedLeagues.size === SYNC_LEAGUES.length) {
    state.sync.selectedLeagues.clear();
  } else {
    SYNC_LEAGUES.forEach(l => state.sync.selectedLeagues.add(l.code));
  }
  renderSyncLeagues(document.getElementById('sync-league-filter')?.value || '');
}

async function triggerSyncSelected() {
  const key = requireKey();
  if (!key) return;
  const season = document.getElementById('sync-season-select')?.value || '2027';
  const leagues = Array.from(state.sync.selectedLeagues);
  if (leagues.length === 0) {
    logConsole('Select at least one league.');
    return;
  }
  logConsole(`Starting batch sync for ${leagues.length} league(s) (Season ${season})...`);
  for (const league of leagues) {
    try {
      await syncLeagueStatsApi(league, season, key);
      logConsole(`OK: ${league} synced.`);
    } catch (err) {
      logConsole(`FAIL: ${league} — ${err.message}`);
    }
  }
  refreshSyncStatus();
}

async function triggerBioEnrichment() {
  const key = requireKey();
  if (!key) return;
  logConsole('Running bio & physicals enrichment (batch 50)...');
  try {
    const res = await enrichBiosApi(key, 50);
    logConsole(`OK: ${res.totalEnriched} of ${res.totalProcessed} players enriched.`);
  } catch (err) {
    logConsole(`FAIL: Bio enrichment — ${err.message}`);
  }
}

async function triggerInjurySync() {
  const key = requireKey();
  if (!key) return;
  logConsole('Syncing live injury feed...');
  try {
    const res = await syncInjuriesApi(key);
    logConsole(`OK: Injury sync completed (${escapeHtml(JSON.stringify(res))}).`);
    refreshSyncStatus();
  } catch (err) {
    logConsole(`FAIL: Injury sync — ${err.message}`);
  }
}
