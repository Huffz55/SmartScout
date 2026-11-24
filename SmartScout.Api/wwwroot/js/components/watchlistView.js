import {
  createBoardApi,
  deleteBoardApi,
  fetchBoardsApi,
  fetchWatchlistItemsApi,
  seedDemoWatchlistApi,
  toggleWatchlistItemApi
} from '../api.js';
import { state } from '../state.js';
import { exportWatchlistCsv } from '../utils/csv.js';
import {
  escapeHtml,
  formatNationality,
  formatTeamName,
  numFmt,
  pctFmt
} from '../utils/formatters.js';

let handlers = {};
let watchlistAbort = null;

const SLOT_COORDS = {
  PG: { left: 'calc(50% - 74px)', top: '198px' },
  SG: { left: '14%', top: '124px' },
  SF: { right: '14%', top: '124px' },
  PF: { left: '18%', top: '34px' },
  C: { right: '18%', top: '34px' }
};

export function initWatchlistView(h) {
  handlers = h;
  document.getElementById('watchlist-board-select')?.addEventListener('change', (e) => {
    state.watchlist.activeBoardId = e.target.value || 'default';
    updateBoardDeleteBtn();
    fetchWatchlist();
  });
  document.getElementById('btn-board-new-toggle')?.addEventListener('click', () => {
    document.getElementById('board-create-bar')?.classList.toggle('hidden');
  });
  document.getElementById('btn-board-create')?.addEventListener('click', createBoard);
  document.getElementById('btn-board-delete')?.addEventListener('click', deleteActiveBoard);
  document.getElementById('watchlist-search-input')?.addEventListener('input', renderWatchlistTable);
  document.getElementById('btn-watchlist-seed')?.addEventListener('click', seedDemoWatchlist);
  document.getElementById('btn-watchlist-csv')?.addEventListener('click', exportWatchlistCsv);
  document.getElementById('btn-watchlist-empty-sleepers')?.addEventListener('click', () => handlers.switchTab('sleepers'));
  document.getElementById('btn-watchlist-empty-directory')?.addEventListener('click', () => handlers.switchTab('players'));

  document.getElementById('btn-lineup-toggle')?.addEventListener('click', () => {
    state.watchlist.lineupExpanded = !state.watchlist.lineupExpanded;
    document.getElementById('lineup-court-body')?.classList.toggle('hidden', !state.watchlist.lineupExpanded);
    const btn = document.getElementById('btn-lineup-toggle');
    if (btn) btn.textContent = state.watchlist.lineupExpanded ? 'Collapse Court ▲' : 'Expand Court ▼';
  });
  document.getElementById('btn-lineup-autofill')?.addEventListener('click', autoFillLineup);
  document.getElementById('btn-lineup-clear')?.addEventListener('click', clearLineup);
}

export async function refreshWatchlistCount() {
  try {
    const items = await fetchWatchlistItemsApi(state.watchlist.activeBoardId);
    state.watchlist.items = items || [];
    const badge = document.getElementById('nav-watchlist-count');
    if (!badge) return;
    const count = state.watchlist.items.length;
    badge.textContent = String(count);
    badge.classList.toggle('hidden', count === 0);
  } catch {
    // non-fatal
  }
}

export async function fetchScoutBoards() {
  try {
    const boards = await fetchBoardsApi();
    state.watchlist.boards = boards || [];
    const sel = document.getElementById('watchlist-board-select');
    if (sel) {
      sel.innerHTML = `<option value="default">Default Watchlist</option>` +
        state.watchlist.boards.map(b => `<option value="${escapeHtml(b.id)}">${escapeHtml(b.name)} (${b.playerCount ?? 0})</option>`).join('');
      sel.value = state.watchlist.activeBoardId;
    }
    updateBoardDeleteBtn();
  } catch {
    // fallback to default
  }
}

function updateBoardDeleteBtn() {
  const btn = document.getElementById('btn-board-delete');
  if (btn) btn.classList.toggle('hidden', state.watchlist.activeBoardId === 'default');
}

async function createBoard() {
  const input = document.getElementById('board-name-input');
  const name = input?.value.trim();
  if (!name) return;
  try {
    const created = await createBoardApi(name);
    if (input) input.value = '';
    document.getElementById('board-create-bar')?.classList.add('hidden');
    state.watchlist.activeBoardId = created.id || 'default';
    await fetchScoutBoards();
    await fetchWatchlist();
  } catch (err) {
    alert(`Create board failed: ${err.message}`);
  }
}

async function deleteActiveBoard() {
  if (state.watchlist.activeBoardId === 'default') return;
  if (!confirm('Delete this custom scout board?')) return;
  try {
    await deleteBoardApi(state.watchlist.activeBoardId);
    state.watchlist.activeBoardId = 'default';
    await fetchScoutBoards();
    await fetchWatchlist();
  } catch (err) {
    alert(`Delete board failed: ${err.message}`);
  }
}

export async function fetchWatchlist() {
  const tbody = document.getElementById('watchlist-tbody');
  if (tbody) tbody.innerHTML = `<tr><td colspan="10" style="text-align:center;padding:28px;color:var(--text-muted)">Loading watchlist...</td></tr>`;
  if (watchlistAbort) watchlistAbort.abort();
  watchlistAbort = new AbortController();
  try {
    const items = await fetchWatchlistItemsApi(state.watchlist.activeBoardId, watchlistAbort.signal);
    state.watchlist.items = items || [];
    const badge = document.getElementById('nav-watchlist-count');
    if (badge) {
      badge.textContent = String(state.watchlist.items.length);
      badge.classList.toggle('hidden', state.watchlist.items.length === 0);
    }
    renderWatchlistTable();
    renderLineupSimulator();
  } catch (err) {
    if (err.name === 'AbortError') return;
    if (tbody) tbody.innerHTML = `<tr><td colspan="10" style="text-align:center;padding:24px;color:var(--accent-rose)">Error: ${escapeHtml(err.message)}</td></tr>`;
  }
}

async function seedDemoWatchlist() {
  const btn = document.getElementById('btn-watchlist-seed');
  if (btn) btn.disabled = true;
  try {
    await seedDemoWatchlistApi(state.watchlist.activeBoardId);
    await fetchScoutBoards();
    await fetchWatchlist();
  } catch (err) {
    alert(`Seed demo failed: ${err.message}`);
  } finally {
    if (btn) btn.disabled = false;
  }
}

function renderWatchlistTable() {
  const tbody = document.getElementById('watchlist-tbody');
  const emptyEl = document.getElementById('watchlist-empty');
  const tableWrap = document.getElementById('watchlist-table-wrap');
  if (!tbody) return;

  const q = (document.getElementById('watchlist-search-input')?.value || '').trim().toLowerCase();
  const filtered = state.watchlist.items.filter(p =>
    !q || (p.fullName || '').toLowerCase().includes(q) || (p.teamName || '').toLowerCase().includes(q) || (p.league || '').toLowerCase().includes(q)
  );

  if (state.watchlist.items.length === 0) {
    tableWrap?.classList.add('hidden');
    emptyEl?.classList.remove('hidden');
    return;
  }

  tableWrap?.classList.remove('hidden');
  emptyEl?.classList.add('hidden');

  if (filtered.length === 0) {
    tbody.innerHTML = `<tr><td colspan="10" style="text-align:center;padding:24px;color:var(--text-muted)">No watchlist targets match "${escapeHtml(q)}".</td></tr>`;
    return;
  }

  tbody.innerHTML = filtered.map(p => {
    const rawSeason = p.latestStat?.season || 2027;
    const rawEti = p.euroLeagueTransitionIndex ?? p.latestStat?.euroLeagueTransitionIndex;
    const rawTpr = p.threePointRate ?? p.latestStat?.threePointRate;
    const rawS36 = p.stocksPer36 ?? p.latestStat?.stocksPer36;
    const rawPos = p.position || p.primaryPosition || 'F';
    const teamDisplay = formatTeamName(p.teamName, p.league);
    const natDisplay = formatNationality(p.nationality, p.league, p.fullName);

    return `
      <tr>
        <td>
          <button type="button" class="player-link" data-open-player="${escapeHtml(p.id)}" data-season="${rawSeason}">${escapeHtml(p.fullName)}</button>
          ${p.scoutNotes ? `<div style="font-size:10.5px;color:var(--text-muted);max-width:220px;overflow:hidden;text-overflow:ellipsis">${escapeHtml(p.scoutNotes)}</div>` : ''}
        </td>
        <td><button type="button" class="team-link" data-open-team="${escapeHtml(p.teamName || '')}" data-league="${escapeHtml(p.league || '')}" data-season="${rawSeason}">${escapeHtml(teamDisplay)}</button></td>
        <td><span class="badge badge-sky">${escapeHtml(p.league || '—')}</span></td>
        <td class="center"><span class="badge">${escapeHtml(rawPos)}</span></td>
        <td class="center mono">${escapeHtml(natDisplay)}</td>
        <td class="num" style="color:var(--accent-sky);font-weight:600">${numFmt(rawEti)}</td>
        <td class="num">${pctFmt(rawTpr, 0)}</td>
        <td class="num">${numFmt(rawS36)}</td>
        <td style="font-size:11px;color:var(--text-secondary)">${escapeHtml(p.estimatedMarketValueTier || 'Tier 4')}</td>
        <td style="text-align:right">
          <div style="display:flex;gap:4px;justify-content:flex-end">
            <button type="button" class="btn btn-sm" title="Assign to 5-Man Lineup" data-assign-lineup="${escapeHtml(p.id)}">+5</button>
            <a class="btn btn-sm btn-emerald" href="/api/players/${encodeURIComponent(p.id)}/report.pdf?season=${rawSeason}" target="_blank">PDF</a>
            <button type="button" class="btn btn-sm btn-rose" title="Remove" data-remove-watch="${escapeHtml(p.id)}">✕</button>
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
  tbody.querySelectorAll('[data-assign-lineup]').forEach(btn => {
    btn.addEventListener('click', () => assignPlayerToLineup(btn.dataset.assignLineup));
  });
  tbody.querySelectorAll('[data-remove-watch]').forEach(btn => {
    btn.addEventListener('click', async () => {
      await toggleWatchlistItemApi(state.watchlist.activeBoardId, btn.dataset.removeWatch);
      await fetchScoutBoards();
      fetchWatchlist();
    });
  });
}

function assignPlayerToLineup(playerId) {
  const player = state.watchlist.items.find(x => x.id === playerId);
  if (!player) return;
  const pos = (player.position || player.primaryPosition || 'SF').toUpperCase();
  const candidates = pos.includes('PG') ? ['PG', 'SG'] : pos.includes('SG') ? ['SG', 'PG', 'SF'] : pos.includes('PF') ? ['PF', 'C', 'SF'] : pos.includes('C') ? ['C', 'PF'] : ['SF', 'PF', 'SG'];
  const target = candidates.find(s => !state.watchlist.lineupSlots[s]) || candidates[0];
  state.watchlist.lineupSlots[target] = player;
  renderLineupSimulator();
}

function autoFillLineup() {
  const items = state.watchlist.items;
  if (!items || items.length === 0) return;
  const positions = ['PG', 'SG', 'SF', 'PF', 'C'];
  const used = new Set();
  for (const pos of positions) {
    const match = items.find(p => !used.has(p.id) && (p.position || p.primaryPosition || '').toUpperCase().includes(pos[0]))
      || items.find(p => !used.has(p.id));
    if (match) {
      state.watchlist.lineupSlots[pos] = match;
      used.add(match.id);
    }
  }
  renderLineupSimulator();
}

function clearLineup() {
  state.watchlist.lineupSlots = { PG: null, SG: null, SF: null, PF: null, C: null };
  renderLineupSimulator();
}

export function renderLineupSimulator() {
  const slots = state.watchlist.lineupSlots;
  const positions = ['PG', 'SG', 'SF', 'PF', 'C'];

  const summaryEl = document.getElementById('lineup-summary-pills');
  if (summaryEl) {
    summaryEl.innerHTML = positions.map(pos => {
      const p = slots[pos];
      return p
        ? `<span class="badge badge-sky">${pos}: ${escapeHtml(p.fullName.split(' ').slice(-1)[0])}</span>`
        : `<span class="badge" style="color:var(--text-muted)">${pos}: —</span>`;
    }).join('');
  }

  const active = positions.map(pos => slots[pos]).filter(Boolean);
  const quotaEl = document.getElementById('lineup-quota-pill');
  const synergyEl = document.getElementById('lineup-synergy-pill');

  if (active.length === 0) {
    if (quotaEl) { quotaEl.className = 'badge'; quotaEl.textContent = 'BSL Quota: 0/4 INTL'; }
    if (synergyEl) { synergyEl.className = 'badge'; synergyEl.textContent = 'Synergy: Empty'; }
  } else {
    const turCount = active.filter(p => {
      const nat = formatNationality(p.nationality, p.league, p.fullName);
      return nat.includes('TUR') || nat.includes('Turkey');
    }).length;
    const intlCount = active.length - turCount;
    const totalUsg = active.reduce((acc, p) => {
      const usg = p.usagePct ?? p.latestStat?.usagePct;
      const pts36 = p.ptsPer36 ?? p.latestStat?.ptsPer36;
      return acc + (usg || (pts36 ? pts36 * 0.95 : 19.5));
    }, 0);
    const avgTpr = active.reduce((acc, p) => {
      const tpr = p.threePointRate ?? p.latestStat?.threePointRate;
      return acc + (tpr != null ? tpr * 100 : 34);
    }, 0) / active.length;

    if (quotaEl) {
      quotaEl.className = intlCount <= 4 ? 'badge badge-emerald' : 'badge badge-rose';
      quotaEl.textContent = `BSL Quota: ${intlCount}/4 INTL (${turCount} TUR)`;
    }
    if (synergyEl) {
      synergyEl.className = avgTpr >= 34 && totalUsg <= 112 ? 'badge badge-emerald' : 'badge badge-amber';
      synergyEl.textContent = `USG ${totalUsg.toFixed(0)}% · 3PR ${avgTpr.toFixed(0)}%`;
    }
  }

  const courtSlots = document.getElementById('lineup-court-slots');
  if (!courtSlots) return;
  courtSlots.innerHTML = positions.map(pos => {
    const p = slots[pos];
    const coord = SLOT_COORDS[pos];
    const style = Object.entries(coord).map(([k, v]) => `${k}:${v}`).join(';');
    if (!p) {
      return `
        <div class="lineup-slot-card" style="${style}">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px">
            <span class="badge">${pos}</span>
            <span style="color:var(--text-muted);font-size:10px">Empty</span>
          </div>
          <select class="select" style="width:100%;height:24px;font-size:11px" data-slot-select="${pos}">
            <option value="">Assign...</option>
            ${state.watchlist.items.map(item => `<option value="${escapeHtml(item.id)}">${escapeHtml(item.fullName)}</option>`).join('')}
          </select>
        </div>
      `;
    }
    return `
      <div class="lineup-slot-card" style="${style};border-color:rgba(56,189,248,0.45)">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:2px">
          <span class="badge badge-sky">${pos}</span>
          <button type="button" data-slot-clear="${pos}" style="background:none;border:none;color:var(--text-muted);cursor:pointer;font-size:11px">✕</button>
        </div>
        <div style="font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${escapeHtml(p.fullName)}</div>
        <div style="font-size:10px;color:var(--text-secondary)">${escapeHtml(formatNationality(p.nationality, p.league, p.fullName))}</div>
      </div>
    `;
  }).join('');

  courtSlots.querySelectorAll('[data-slot-select]').forEach(sel => {
    sel.addEventListener('change', () => {
      const item = state.watchlist.items.find(x => x.id === sel.value);
      if (item) {
        state.watchlist.lineupSlots[sel.dataset.slotSelect] = item;
        renderLineupSimulator();
      }
    });
  });
  courtSlots.querySelectorAll('[data-slot-clear]').forEach(btn => {
    btn.addEventListener('click', () => {
      state.watchlist.lineupSlots[btn.dataset.slotClear] = null;
      renderLineupSimulator();
    });
  });
}
