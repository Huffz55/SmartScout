import { fetchSleepersApi, toggleWatchlistItemApi } from '../api.js';
import { state } from '../state.js';
import { exportSleepersCsv } from '../utils/csv.js';
import {
  escapeHtml,
  formatCategory,
  formatTeamName,
  getCategoryBadgeClass,
  numFmt,
  openFilmRoom,
  pctFmt
} from '../utils/formatters.js';

let handlers = {};

export function initSleepersView(h) {
  handlers = h;
  document.getElementById('btn-sleepers-scan')?.addEventListener('click', fetchSleepers);
  document.getElementById('btn-sleepers-adv-toggle')?.addEventListener('click', () => {
    document.getElementById('sleepers-adv-panel')?.classList.toggle('hidden');
  });
  document.getElementById('btn-sleepers-reset')?.addEventListener('click', resetSleeperFilters);
  document.getElementById('btn-sleepers-csv')?.addEventListener('click', exportSleepersCsv);
  document.getElementById('sleepers-prev-btn')?.addEventListener('click', () => {
    if (state.sleepers.page > 1) {
      state.sleepers.page--;
      renderSleepersTable();
    }
  });
  document.getElementById('sleepers-next-btn')?.addEventListener('click', () => {
    const totalPages = Math.ceil(state.sleepers.filtered.length / state.sleepers.pageSize) || 1;
    if (state.sleepers.page < totalPages) {
      state.sleepers.page++;
      renderSleepersTable();
    }
  });
  document.getElementById('sleepers-pagesize')?.addEventListener('change', (e) => {
    state.sleepers.pageSize = Number(e.target.value) || 50;
    state.sleepers.page = 1;
    renderSleepersTable();
  });

  ['sleepers-category', 'sleepers-league', 'sleepers-season', 'sleepers-position', 'sleepers-age'].forEach(id => {
    document.getElementById(id)?.addEventListener('change', fetchSleepers);
  });
  ['sleepers-archetype', 'sleepers-min-3pm', 'sleepers-min-3pct', 'sleepers-min-pts36', 'sleepers-min-ts', 'sleepers-sort'].forEach(id => {
    document.getElementById(id)?.addEventListener('change', () => {
      if (id === 'sleepers-sort') {
        state.sleepers.sortKey = document.getElementById(id).value || 'score';
        state.sleepers.sortDir = 'desc';
      }
      applySleeperFilters();
    });
  });
}

export async function fetchSleepers() {
  const category = document.getElementById('sleepers-category')?.value || '';
  const league = document.getElementById('sleepers-league')?.value || '';
  const season = document.getElementById('sleepers-season')?.value || '0';
  const position = document.getElementById('sleepers-position')?.value || 'ALL';
  const maxAge = document.getElementById('sleepers-age')?.value || 'ALL';

  const tbody = document.getElementById('sleepers-tbody');
  if (tbody) tbody.innerHTML = `<tr><td colspan="13" style="text-align:center;padding:28px;color:var(--text-muted)">Scanning sleeper candidates...</td></tr>`;

  try {
    state.sleepers.raw = await fetchSleepersApi({ season, category, league, position, maxAge, top: 100 });
    state.sleepers.page = 1;
    applySleeperFilters();
  } catch (err) {
    if (tbody) tbody.innerHTML = `<tr><td colspan="13" style="text-align:center;padding:24px;color:var(--accent-rose)">Error: ${escapeHtml(err.message)}</td></tr>`;
  }
}

export function applySleeperFilters() {
  const position = document.getElementById('sleepers-position')?.value || 'ALL';
  const maxAge = document.getElementById('sleepers-age')?.value || 'ALL';
  const archetype = document.getElementById('sleepers-archetype')?.value || 'ALL';
  const min3pm = parseFloat(document.getElementById('sleepers-min-3pm')?.value || '0') || 0;
  const min3pct = parseFloat(document.getElementById('sleepers-min-3pct')?.value || '0') || 0;
  const minPts36 = parseFloat(document.getElementById('sleepers-min-pts36')?.value || '0') || 0;
  const minTs = parseFloat(document.getElementById('sleepers-min-ts')?.value || '0') || 0;

  let list = state.sleepers.raw.filter(p => {
    if (position !== 'ALL') {
      const combinedPos = `${p.position || ''} ${p.secondaryPosition || ''}`.toUpperCase();
      if (!combinedPos.includes(position.toUpperCase())) return false;
    }
    if (maxAge !== 'ALL') {
      const cap = parseInt(maxAge, 10);
      if (p.age == null || typeof p.age !== 'number' || p.age > cap) return false;
    }
    const eff3pm = p.threePointsMade != null ? p.threePointsMade : ((p.threePointsAttempted || 0) * (p.fg3Pct || 0));
    if (min3pm > 0 && eff3pm < min3pm) return false;
    if (min3pct > 0 && (p.fg3Pct || 0) * 100 < min3pct) return false;
    if (minPts36 > 0 && (p.ptsPer36 || 0) < minPts36) return false;
    if (minTs > 0 && (p.trueShootingPct || 0) * 100 < minTs) return false;
    if (archetype !== 'ALL' && (p.archetype || '').toLowerCase() !== archetype.toLowerCase()) return false;
    return true;
  });

  const { sortKey, sortDir } = state.sleepers;
  const mult = sortDir === 'asc' ? 1 : -1;
  list.sort((a, b) => {
    const val = (item) => {
      switch (sortKey) {
        case 'eti': return item.euroLeagueTransitionIndex ?? -1;
        case 'ppg': return item.points ?? -1;
        case 'pts36': return item.ptsPer36 ?? -1;
        case 'ts': return item.trueShootingPct ?? -1;
        case '3pct': return item.fg3Pct ?? -1;
        case 'stocks36': return item.stocksPer36 ?? -1;
        case 'age': return item.age ?? 99;
        case 'gp': return item.gamesPlayed ?? -1;
        default: return item.sleeperScore ?? -1;
      }
    };
    return (val(a) - val(b)) * mult;
  });

  state.sleepers.filtered = list;
  renderSleepersTable();
}

function sortByColumn(key) {
  if (state.sleepers.sortKey === key) {
    state.sleepers.sortDir = state.sleepers.sortDir === 'desc' ? 'asc' : 'desc';
  } else {
    state.sleepers.sortKey = key;
    state.sleepers.sortDir = key === 'age' ? 'asc' : 'desc';
  }
  applySleeperFilters();
}

export function renderSleepersTable() {
  const tbody = document.getElementById('sleepers-tbody');
  const countEl = document.getElementById('sleepers-count');
  const pageInfo = document.getElementById('sleepers-page-info');
  if (!tbody) return;

  const items = state.sleepers.filtered;
  const totalPages = Math.ceil(items.length / state.sleepers.pageSize) || 1;
  if (state.sleepers.page > totalPages) state.sleepers.page = totalPages;
  const start = (state.sleepers.page - 1) * state.sleepers.pageSize;
  const slice = items.slice(start, start + state.sleepers.pageSize);

  if (countEl) countEl.textContent = `${items.length} Prospects`;
  if (pageInfo) pageInfo.textContent = `Page ${state.sleepers.page} of ${totalPages} (${items.length} filtered)`;

  document.querySelectorAll('#sleepers-table thead th[data-sort]').forEach(th => {
    const key = th.getAttribute('data-sort');
    th.classList.toggle('sort-active', key === state.sleepers.sortKey);
    th.onclick = () => sortByColumn(key);
  });

  if (slice.length === 0) {
    tbody.innerHTML = `<tr><td colspan="13" style="text-align:center;padding:28px;color:var(--text-muted)">No prospects match the active filters.</td></tr>`;
    return;
  }

  const isCustomBoard = state.watchlist.activeBoardId && state.watchlist.activeBoardId !== 'default';
  const boardMemberIds = isCustomBoard ? new Set((state.watchlist.items || []).map(x => x.id)) : null;

  tbody.innerHTML = slice.map(p => {
    const teamDisplay = formatTeamName(p.teamName, p.league);
    const activeSeason = p.season || 2027;
    const isWatchlisted = isCustomBoard ? boardMemberIds.has(p.playerId) : Boolean(p.isWatchlisted);
    const rawInjStatus = p.activeInjuryStatus ? String(p.activeInjuryStatus).trim() : '';
    const injLower = rawInjStatus.toLowerCase();
    const injLabel = !rawInjStatus
      ? ''
      : (injLower === 'out' || injLower.startsWith('expected to be out'))
        ? 'Out'
        : rawInjStatus.length > 14
          ? 'Injured'
          : rawInjStatus;
    const injTitle = p.activeInjuryDetail ? `${rawInjStatus}: ${p.activeInjuryDetail}` : rawInjStatus;

    return `
      <tr>
        <td>
          <button type="button" class="player-link" data-open-player="${escapeHtml(p.playerId)}" data-season="${activeSeason}">${escapeHtml(p.fullName)}</button>
          <span style="color:var(--text-muted);font-size:11px;margin-left:6px">${p.age ? 'U' + p.age : ''}</span>
          ${p.isTwoWayPlayer ? `<span class="badge badge-amber" style="margin-left:4px;font-size:11px;padding:1px 5px" title="${escapeHtml(p.parentTeam ? '2-Way: ' + p.parentTeam : 'Two-Way Contract')}">2-Way</span>` : ''}
          ${rawInjStatus ? `<span class="badge badge-rose" style="margin-left:4px;font-size:11px;padding:1px 5px" title="${escapeHtml(injTitle)}">${escapeHtml(injLabel)}</span>` : ''}
        </td>
        <td>
          <button type="button" class="team-link" data-open-team="${escapeHtml(p.teamName || '')}" data-league="${escapeHtml(p.league || '')}" data-season="${activeSeason}">${escapeHtml(teamDisplay)}</button>
        </td>
        <td><span class="badge badge-sky">${escapeHtml(p.league || '—')}</span></td>
        <td class="center"><span class="badge">${escapeHtml(p.position || '—')}</span></td>
        <td class="num">${p.gamesPlayed ?? '—'}</td>
        <td class="num" style="font-weight:600;color:var(--text-primary)">${numFmt(p.points)}</td>
        <td class="num">${numFmt(p.ptsPer36)}</td>
        <td class="num" style="color:var(--accent-emerald)">${pctFmt(p.trueShootingPct)}</td>
        <td class="num">${pctFmt(p.fg3Pct)}</td>
        <td class="num">${numFmt(p.stocksPer36)}</td>
        <td class="num" style="color:var(--accent-sky);font-weight:600">${numFmt(p.euroLeagueTransitionIndex)}</td>
        <td class="num" style="color:var(--accent-emerald);font-weight:700">${numFmt(p.sleeperScore)}</td>
        <td>
          <div style="display:flex;align-items:center;gap:4px;justify-content:flex-end">
            <span class="badge ${getCategoryBadgeClass(p.sleeperCategory)}">${escapeHtml(formatCategory(p.sleeperCategory))}</span>
            <button type="button" class="btn btn-sm btn-icon ${isWatchlisted ? 'btn-amber' : ''}" title="${isWatchlisted ? 'Remove from Watchlist' : 'Add to Watchlist'}" aria-label="Toggle Watchlist" aria-pressed="${isWatchlisted}" data-watch-id="${escapeHtml(p.playerId)}">${isWatchlisted ? '★' : '☆'}</button>
            <button type="button" class="btn btn-sm btn-icon" title="Compare" data-cmp-id="${escapeHtml(p.playerId)}" data-cmp-name="${escapeHtml(p.fullName)}" data-cmp-season="${activeSeason}">⇄</button>
            <button type="button" class="btn btn-sm btn-icon" title="Film Room" data-film-name="${escapeHtml(p.fullName)}" data-film-team="${escapeHtml(teamDisplay)}" data-film-league="${escapeHtml(p.league || '')}">▶</button>
            <a class="btn btn-sm btn-icon btn-emerald" title="PDF Report" href="/api/players/${encodeURIComponent(p.playerId)}/report.pdf?season=${activeSeason}" target="_blank">PDF</a>
          </div>
        </td>
      </tr>
    `;
  }).join('');

  tbody.querySelectorAll('[data-open-player]').forEach(btn => {
    btn.addEventListener('click', () => handlers.openDossier(btn.dataset.openPlayer, Number(btn.dataset.season)));
  });
  tbody.querySelectorAll('[data-open-team]').forEach(btn => {
    btn.addEventListener('click', () => handlers.openTeamRoster(btn.dataset.openTeam, btn.dataset.league, Number(btn.dataset.season)));
  });
  tbody.querySelectorAll('[data-watch-id]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const playerId = btn.dataset.watchId;
      if (!playerId) return;
      btn.disabled = true;
      try {
        const res = await toggleWatchlistItemApi(state.watchlist.activeBoardId, playerId);
        const active = Boolean(res?.isWatchlisted);
        if (!isCustomBoard) updateSleeperWatchlistFlag(playerId, active);
        btn.className = `btn btn-sm btn-icon ${active ? 'btn-amber' : ''}`;
        btn.textContent = active ? '★' : '☆';
        btn.title = active ? 'Remove from Watchlist' : 'Add to Watchlist';
        btn.setAttribute('aria-pressed', String(active));
        if (handlers.onWatchlistMutated) handlers.onWatchlistMutated();
      } catch (err) {
        alert(`Watchlist update failed: ${err.message}`);
      } finally {
        btn.disabled = false;
      }
    });
  });
  tbody.querySelectorAll('[data-cmp-id]').forEach(btn => {
    btn.addEventListener('click', () => handlers.launchCompare(btn.dataset.cmpId, btn.dataset.cmpName, Number(btn.dataset.cmpSeason)));
  });
  tbody.querySelectorAll('[data-film-name]').forEach(btn => {
    btn.addEventListener('click', () => openFilmRoom(btn.dataset.filmName, btn.dataset.filmTeam, btn.dataset.filmLeague));
  });
}

export function updateSleeperWatchlistFlag(playerId, isWatchlisted) {
  const target = state.sleepers.raw.find(x => x.playerId === playerId);
  if (target) target.isWatchlisted = Boolean(isWatchlisted);
}

function resetSleeperFilters() {
  ['sleepers-category', 'sleepers-league'].forEach(id => { const el = document.getElementById(id); if (el) el.value = ''; });
  const seasonEl = document.getElementById('sleepers-season'); if (seasonEl) seasonEl.value = '0';
  ['sleepers-position', 'sleepers-age', 'sleepers-archetype'].forEach(id => { const el = document.getElementById(id); if (el) el.value = 'ALL'; });
  ['sleepers-min-3pm', 'sleepers-min-3pct', 'sleepers-min-pts36', 'sleepers-min-ts'].forEach(id => { const el = document.getElementById(id); if (el) el.value = '0'; });
  const sortEl = document.getElementById('sleepers-sort'); if (sortEl) sortEl.value = 'score';
  state.sleepers.sortKey = 'score';
  state.sleepers.sortDir = 'desc';
  fetchSleepers();
}
