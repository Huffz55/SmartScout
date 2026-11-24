import {
  fetchPlayerProfileApi,
  fetchSimilarPlayersApi,
  saveNotesApi,
  toggleWatchlistItemApi
} from '../api.js';
import { state } from '../state.js';
import {
  escapeHtml,
  formatNationality,
  formatTeamName,
  numFmt,
  openFilmRoom,
  pctFmt
} from '../utils/formatters.js';

let onWatchlistMutated = null;
let onLaunchCompare = null;
let onOpenTeamRoster = null;

export function configureDossierHandlers({ watchlistMutated, launchCompare, openTeamRoster }) {
  onWatchlistMutated = watchlistMutated;
  onLaunchCompare = launchCompare;
  onOpenTeamRoster = openTeamRoster;
}

export function closeDossier() {
  document.getElementById('dossier-drawer')?.classList.add('hidden');
  state.dossier.historyStack = [];
  if (state.dossier.radarChart) {
    state.dossier.radarChart.destroy();
    state.dossier.radarChart = null;
  }
  if (state.dossier.trajectoryChart) {
    state.dossier.trajectoryChart.destroy();
    state.dossier.trajectoryChart = null;
  }
}

export function dossierGoBack() {
  if (state.dossier.historyStack.length === 0) return;
  const prev = state.dossier.historyStack.pop();
  openDossier(prev.playerId, prev.season, false);
}

export async function openDossier(playerId, season, pushHistory = true) {
  const drawer = document.getElementById('dossier-drawer');
  if (!drawer) return;

  if (pushHistory && state.dossier.currentPlayer && !drawer.classList.contains('hidden')) {
    state.dossier.historyStack.push({
      playerId: state.dossier.currentPlayer.playerId || state.dossier.currentPlayer.id,
      fullName: state.dossier.currentPlayer.fullName || 'Previous',
      season: state.dossier.currentPlayer.season
    });
  } else if (!pushHistory && state.dossier.historyStack.length === 0) {
    state.dossier.historyStack = [];
  }

  const navRow = document.getElementById('dossier-nav-row');
  if (navRow) navRow.classList.toggle('hidden', state.dossier.historyStack.length === 0);

  drawer.classList.remove('hidden');
  document.getElementById('dossier-name').textContent = 'Loading profile...';
  document.getElementById('dossier-subtitle').textContent = '';

  try {
    const p = await fetchPlayerProfileApi(playerId, season);
    state.dossier.currentPlayer = { ...p, playerId: p.playerId || playerId };
    renderDossierProfile(p, playerId, season);
    loadSimilarPlayers(playerId, p.season || season || 2027);
  } catch (err) {
    document.getElementById('dossier-name').textContent = 'Profile Unavailable';
    document.getElementById('dossier-subtitle').textContent = err.message;
  }
}

function renderDossierProfile(p, playerId, reqSeason) {
  const activeSeason = p.season || reqSeason || 2027;
  document.getElementById('dossier-name').textContent = p.fullName || 'Unknown Player';
  const crumbEl = document.getElementById('dossier-crumb');
  if (crumbEl) {
    crumbEl.textContent = state.dossier.historyStack.length > 0
      ? state.dossier.historyStack.map(h => h.fullName).filter(Boolean).concat(p.fullName || 'Player').join(' › ')
      : '';
  }
  document.getElementById('dossier-pos').textContent = p.position || 'F';

  const subEl = document.getElementById('dossier-subtitle');
  const teamFormatted = formatTeamName(p.teamName, p.league);
  const natStr = (p.nationality && p.nationality !== '--') ? formatNationality(p.nationality, p.league, p.fullName) : '';
  const heightStr = p.heightCm ? `${p.heightCm} cm` : '';
  const ageStr = p.age ? `Age ${p.age}` : '';

  subEl.innerHTML = '';
  if (p.teamName && p.teamName !== '-' && p.teamName !== 'Free Agent / Unassigned') {
    const teamBtn = document.createElement('button');
    teamBtn.type = 'button';
    teamBtn.className = 'team-link';
    teamBtn.textContent = teamFormatted;
    teamBtn.addEventListener('click', () => {
      if (onOpenTeamRoster) onOpenTeamRoster(p.teamName, p.league, activeSeason);
    });
    subEl.appendChild(teamBtn);
  } else {
    const span = document.createElement('span');
    span.textContent = teamFormatted;
    subEl.appendChild(span);
  }

  const tail = [p.league || '—', `${activeSeason} Season`, natStr, heightStr, ageStr].filter(Boolean).join(' • ');
  const tailSpan = document.createElement('span');
  tailSpan.style.color = 'var(--text-secondary)';
  tailSpan.textContent = ` • ${tail}`;
  subEl.appendChild(tailSpan);

  document.getElementById('dossier-archetype').textContent = p.archetype || 'Two-Way Prospect';
  document.getElementById('dossier-pdf-btn').href = `/api/players/${playerId}/report.pdf?season=${activeSeason}`;

  const eci = Number(p.europeanCompatibilityIndex || 0);
  const dpi = Number(p.defensivePressureIndex || 0);
  const eti = Number(p.euroLeagueTransitionIndex || 0);

  document.getElementById('dossier-eci-score').textContent = `${eci.toFixed(1)}%`;
  document.getElementById('dossier-eci-bar').style.width = `${Math.min(100, Math.max(0, eci))}%`;
  document.getElementById('dossier-dpi-score').textContent = `${dpi.toFixed(1)}%`;
  document.getElementById('dossier-dpi-bar').style.width = `${Math.min(100, Math.max(0, dpi))}%`;
  document.getElementById('dossier-eti-score').textContent = eti > 0 ? eti.toFixed(1) : '—';
  document.getElementById('dossier-eti-bar').style.width = `${Math.min(100, Math.max(0, eti))}%`;

  updateWatchlistBtn(Boolean(p.isWatchlisted));

  document.getElementById('dossier-market-tier').textContent = p.estimatedMarketValueTier || 'Tier 4 • Core Rotation';
  document.getElementById('dossier-contract-expiry').textContent = p.contractExpirySeason ? `${p.contractExpirySeason} Season` : 'Current / 2027';
  document.getElementById('dossier-agency').textContent = p.agencyName || 'Independent / Unassigned';
  document.getElementById('dossier-physicals').textContent = (p.wingspanCm || p.standingReachCm)
    ? `${p.wingspanCm ? p.wingspanCm + 'cm WS' : ''} ${p.standingReachCm ? '/ ' + p.standingReachCm + 'cm SR' : ''}`.trim()
    : 'Not measured';

  document.getElementById('dossier-gp').textContent = `${p.gamesPlayed || 0} GP`;
  document.getElementById('dossier-mpg').textContent = numFmt(p.minutes);
  document.getElementById('dossier-ppg').textContent = numFmt(p.points);
  document.getElementById('dossier-pts36').textContent = numFmt(p.ptsPer36);
  document.getElementById('dossier-ts').textContent = pctFmt(p.trueShootingPct);
  document.getElementById('dossier-asttov').textContent = numFmt(p.assistToTurnoverRatio, 2);
  document.getElementById('dossier-stocks36').textContent = numFmt(p.stocksPer36);
  document.getElementById('dossier-3prate').textContent = pctFmt(p.threePointRate, 0);

  const pcts = p.percentiles || {};
  renderPctBar('dossier-pct-ppg', pcts.Points ?? pcts.points);
  renderPctBar('dossier-pct-pts36', pcts.PtsPer36 ?? pcts.ptsPer36);
  renderPctBar('dossier-pct-ts', pcts.TrueShootingPct ?? pcts.trueShootingPct);
  renderPctBar('dossier-pct-asttov', pcts.AssistToTurnoverRatio ?? pcts.assistToTurnoverRatio);
  renderPctBar('dossier-pct-stocks36', pcts.StocksPer36 ?? pcts.stocksPer36);
  renderPctBar('dossier-pct-3prate', pcts.ThreePointRate ?? pcts.threePointRate);

  const s2 = p.twoPointScoringShare != null ? p.twoPointScoringShare * 100 : 50;
  const s3 = p.threePointScoringShare != null ? p.threePointScoringShare * 100 : 30;
  const sFt = p.freeThrowScoringShare != null ? p.freeThrowScoringShare * 100 : 20;
  document.getElementById('dossier-diet-2p-label').textContent = `2PT: ${s2.toFixed(0)}%`;
  document.getElementById('dossier-diet-3p-label').textContent = `3PT: ${s3.toFixed(0)}%`;
  document.getElementById('dossier-diet-ft-label').textContent = `FT: ${sFt.toFixed(0)}%`;
  document.getElementById('dossier-diet-2p-bar').style.width = `${s2}%`;
  document.getElementById('dossier-diet-3p-bar').style.width = `${s3}%`;
  document.getElementById('dossier-diet-ft-bar').style.width = `${sFt}%`;

  document.getElementById('dossier-ftr').textContent = pctFmt(p.freeThrowRate);
  document.getElementById('dossier-pf36').textContent = numFmt(p.foulsCommittedPer36);
  document.getElementById('dossier-foul-diff').textContent = p.netFoulDifferential != null
    ? `${p.netFoulDifferential >= 0 ? '+' : ''}${p.netFoulDifferential.toFixed(1)}`
    : '—';
  document.getElementById('dossier-orb-share').textContent = pctFmt(p.offensiveReboundShare);

  const riskBadge = document.getElementById('dossier-foul-risk');
  const pf = p.foulsCommittedPer36 || 0;
  if (pf >= 4.5) {
    riskBadge.className = 'badge badge-rose';
    riskBadge.textContent = 'High Foul Risk';
  } else if (pf >= 3.3) {
    riskBadge.className = 'badge badge-amber';
    riskBadge.textContent = 'Moderate Fouls';
  } else {
    riskBadge.className = 'badge badge-emerald';
    riskBadge.textContent = 'Disciplined';
  }

  renderTraits('dossier-strengths-list', p.strengths || [], 'emerald');
  renderTraits('dossier-weaknesses-list', p.weaknesses || [], 'amber');
  const strengthsCount = (p.strengths || []).length;
  const weaknessesCount = (p.weaknesses || []).length;
  document.getElementById('dossier-strengths-count').textContent = String(strengthsCount);
  const weakBadge = document.getElementById('dossier-weaknesses-count');
  if (weakBadge) {
    weakBadge.className = weaknessesCount > 0 ? 'badge badge-amber' : 'badge badge-emerald';
    weakBadge.textContent = weaknessesCount > 0 ? String(weaknessesCount) : '0';
  }

  renderCareerTrajectory(p.careerHistory || [], playerId, activeSeason);
  renderInjuries(p.injuries || []);

  const notesEl = document.getElementById('dossier-scout-notes');
  const notesStatus = document.getElementById('dossier-notes-status');
  if (notesEl) notesEl.value = p.scoutNotes || '';
  if (notesStatus) notesStatus.textContent = p.scoutNotes ? 'Saved' : 'Auto-saves';

  renderRadar(p.radarMetrics);
}

function renderPctBar(id, pctVal) {
  const el = document.getElementById(id);
  if (!el) return;
  const val = pctVal != null ? Math.round(pctVal) : 0;
  let color = '#64748B';
  if (val >= 80) color = '#10B981';
  else if (val >= 55) color = '#38BDF8';
  else if (val >= 35) color = '#F59E0B';
  else if (val > 0) color = '#F43F5E';

  el.innerHTML = `
    <div style="display:flex;align-items:center;gap:6px;margin-top:4px">
      <div class="bar-track"><div class="bar-fill" style="width:${Math.min(100, Math.max(0, val))}%;background:${color}"></div></div>
      <span class="mono" style="font-size:10px;color:${color};min-width:26px;text-align:right">${val > 0 ? val + '%' : '—'}</span>
    </div>
  `;
}

function renderTraits(containerId, traits, tone) {
  const el = document.getElementById(containerId);
  if (!el) return;
  if (!traits || traits.length === 0) {
    const emptyMsg = containerId === 'dossier-weaknesses-list'
      ? 'No major red flags flagged'
      : 'No standout indicators flagged';
    el.innerHTML = `<div style="color:var(--text-secondary);font-size:11.5px;padding:2px 0">${emptyMsg}</div>`;
    return;
  }
  const color = tone === 'emerald' ? 'var(--accent-emerald)' : 'var(--accent-amber)';
  el.innerHTML = traits.map(t => {
    const label = typeof t === 'string' ? t : (t.label || t.title || '');
    const detail = typeof t === 'string' ? '' : (t.detail || t.description || '');
    return `
      <div style="padding:7px 9px;border-radius:6px;background:var(--bg-canvas);border:1px solid var(--border-subtle)">
        <div style="font-weight:600;color:${color};font-size:11.5px">${escapeHtml(label)}</div>
        ${detail ? `<div style="color:var(--text-secondary);font-size:11px;margin-top:2px">${escapeHtml(detail)}</div>` : ''}
      </div>
    `;
  }).join('');
}

function renderRadar(radar) {
  const canvas = document.getElementById('dossier-radar-chart');
  if (!canvas || typeof Chart === 'undefined') return;
  if (state.dossier.radarChart) state.dossier.radarChart.destroy();
  const r = radar || {};
  state.dossier.radarChart = new Chart(canvas.getContext('2d'), {
    type: 'radar',
    data: {
      labels: ['Scoring', 'Efficiency', 'Playmaking', 'Rebounding', 'Defense', '3P Gravity'],
      datasets: [{
        label: 'Percentile',
        data: [
          r.scoring ?? r.Scoring ?? 50,
          r.efficiency ?? r.Efficiency ?? 50,
          r.playmaking ?? r.Playmaking ?? 50,
          r.rebounding ?? r.Rebounding ?? 50,
          r.defense ?? r.Defense ?? 50,
          r.perimeterThreat ?? r.PerimeterThreat ?? 50
        ],
        backgroundColor: 'rgba(56, 189, 248, 0.16)',
        borderColor: '#38BDF8',
        borderWidth: 2,
        pointBackgroundColor: '#38BDF8',
        pointRadius: 3
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        r: {
          min: 0,
          max: 100,
          ticks: { display: false },
          angleLines: { color: 'rgba(255,255,255,0.08)' },
          grid: { color: 'rgba(255,255,255,0.08)' },
          pointLabels: { color: '#94A3B8', font: { size: 10.5 } }
        }
      },
      plugins: { legend: { display: false } }
    }
  });
}

function renderCareerTrajectory(history, playerId, activeSeason) {
  const countEl = document.getElementById('dossier-career-count');
  const tbody = document.getElementById('dossier-career-tbody');
  const canvas = document.getElementById('dossier-trajectory-chart');
  if (!tbody) return;

  countEl.textContent = `${history.length} Seasons`;
  if (history.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;color:var(--text-muted)">No multi-season history.</td></tr>`;
  } else {
    tbody.innerHTML = history.map(h => `
      <tr style="${h.season === activeSeason ? 'background:rgba(56,189,248,0.08)' : ''}">
        <td class="mono">${h.season}</td>
        <td>${escapeHtml(formatTeamName(h.teamName, h.league))}</td>
        <td><span class="badge">${escapeHtml(h.league || '—')}</span></td>
        <td class="num">${h.gamesPlayed ?? '—'}</td>
        <td class="num">${numFmt(h.points)}</td>
        <td class="num">${pctFmt(h.trueShootingPct)}</td>
        <td class="num" style="color:var(--accent-emerald)">${numFmt(h.euroLeagueTransitionIndex)}</td>
      </tr>
    `).join('');
  }

  if (!canvas || typeof Chart === 'undefined') return;
  if (state.dossier.trajectoryChart) state.dossier.trajectoryChart.destroy();
  const sorted = [...history].sort((a, b) => a.season - b.season);
  state.dossier.trajectoryChart = new Chart(canvas.getContext('2d'), {
    type: 'line',
    data: {
      labels: sorted.map(h => `${h.season} (${h.league || ''})`),
      datasets: [
        {
          label: 'PPG',
          data: sorted.map(h => h.points ?? 0),
          borderColor: '#38BDF8',
          backgroundColor: 'rgba(56,189,248,0.1)',
          tension: 0.25,
          borderWidth: 2
        },
        {
          label: 'TS%',
          data: sorted.map(h => h.trueShootingPct ? h.trueShootingPct * 100 : 0),
          borderColor: '#10B981',
          tension: 0.25,
          borderWidth: 2
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { labels: { color: '#94A3B8', boxWidth: 10, font: { size: 10 } } } },
      scales: {
        x: { ticks: { color: '#64748B', font: { size: 10 } }, grid: { color: 'rgba(255,255,255,0.04)' } },
        y: { ticks: { color: '#64748B', font: { size: 10 } }, grid: { color: 'rgba(255,255,255,0.06)' } }
      }
    }
  });
}

function renderInjuries(injuries) {
  const badge = document.getElementById('dossier-injury-badge');
  const list = document.getElementById('dossier-injuries-list');
  if (!list) return;
  const activeCount = injuries.filter(i => i.isActive).length;
  if (activeCount > 0) {
    badge.className = 'badge badge-rose';
    badge.textContent = `${activeCount} Active`;
  } else {
    badge.className = 'badge badge-emerald';
    badge.textContent = injuries.length > 0 ? `${injuries.length} Historical` : 'Clean';
  }
  if (injuries.length === 0) {
    list.innerHTML = `<div style="color:var(--text-muted);font-size:11.5px">No recorded injuries.</div>`;
    return;
  }
  list.innerHTML = injuries.map(inj => `
    <div style="display:flex;justify-content:space-between;gap:8px;padding:6px 8px;border-radius:6px;background:var(--bg-canvas);border:1px solid var(--border-subtle);font-size:11.5px">
      <div>
        <strong style="color:${inj.isActive ? 'var(--accent-rose)' : 'var(--text-primary)'}">${escapeHtml(inj.injuryType || inj.description || 'Injury')}</strong>
        ${inj.status ? `<span class="badge" style="margin-left:6px">${escapeHtml(inj.status)}</span>` : ''}
      </div>
      <span class="mono" style="color:var(--text-muted)">${inj.reportedDate ? String(inj.reportedDate).slice(0, 10) : ''}</span>
    </div>
  `).join('');
}

async function loadSimilarPlayers(playerId, season) {
  const box = document.getElementById('dossier-similar-list');
  if (!box) return;
  box.innerHTML = `<div style="color:var(--text-muted);font-size:11.5px">Loading comparables...</div>`;
  try {
    const sims = await fetchSimilarPlayersApi(playerId, season);
    if (!sims || sims.length === 0) {
      box.innerHTML = `<div style="color:var(--text-muted);font-size:11.5px">No close stylistic comparables found.</div>`;
      return;
    }
    box.innerHTML = sims.map(s => `
      <button type="button" data-sim-id="${escapeHtml(s.playerId)}" data-sim-season="${s.season || season}"
        style="text-align:left;padding:8px 10px;border-radius:6px;background:var(--bg-canvas);border:1px solid var(--border-subtle);cursor:pointer;display:flex;justify-content:space-between;align-items:center;color:var(--text-primary)">
        <div>
          <div style="font-weight:600;font-size:12px">${escapeHtml(s.fullName)}</div>
          <div style="font-size:11px;color:var(--text-secondary)">${escapeHtml(formatTeamName(s.teamName, s.league))} • ${escapeHtml(s.league || '')}</div>
        </div>
        <span class="badge badge-sky mono">${s.similarityScore != null ? s.similarityScore.toFixed(0) + '%' : 'Sim'}</span>
      </button>
    `).join('');

    box.querySelectorAll('button[data-sim-id]').forEach(btn => {
      btn.addEventListener('click', () => {
        openDossier(btn.getAttribute('data-sim-id'), Number(btn.getAttribute('data-sim-season')), true);
      });
    });
  } catch {
    box.innerHTML = `<div style="color:var(--text-muted);font-size:11.5px">Could not load comparables.</div>`;
  }
}

function updateWatchlistBtn(isWatchlisted) {
  const btn = document.getElementById('dossier-watchlist-btn');
  if (!btn) return;
  btn.className = isWatchlisted ? 'btn btn-sm btn-amber' : 'btn btn-sm';
  btn.textContent = isWatchlisted ? '★ Watchlisted' : '☆ Watchlist';
}

export async function toggleDossierWatchlist() {
  const p = state.dossier.currentPlayer;
  if (!p) return;
  const id = p.playerId || p.id;
  try {
    const res = await toggleWatchlistItemApi(state.watchlist.activeBoardId, id);
    const active = res?.isWatchlisted ?? res?.isAdded ?? !p.isWatchlisted;
    p.isWatchlisted = active;
    updateWatchlistBtn(active);
    if (onWatchlistMutated) onWatchlistMutated();
  } catch (err) {
    alert(`Watchlist update failed: ${err.message}`);
  }
}

export function debounceDossierNotes() {
  const statusEl = document.getElementById('dossier-notes-status');
  if (statusEl) statusEl.textContent = 'Saving...';
  clearTimeout(state.dossier.notesTimeout);
  state.dossier.notesTimeout = setTimeout(async () => {
    const p = state.dossier.currentPlayer;
    const notesEl = document.getElementById('dossier-scout-notes');
    if (!p || !notesEl) return;
    try {
      await saveNotesApi(state.watchlist.activeBoardId, p.playerId || p.id, notesEl.value);
      if (statusEl) statusEl.textContent = 'Saved';
    } catch {
      if (statusEl) statusEl.textContent = 'Error saving';
    }
  }, 500);
}

export function openDossierFilmRoom() {
  const p = state.dossier.currentPlayer;
  if (!p) return;
  openFilmRoom(p.fullName, formatTeamName(p.teamName, p.league), p.league);
}

export function sendDossierToCompare() {
  const p = state.dossier.currentPlayer;
  if (!p || !onLaunchCompare) return;
  const id = p.playerId || p.id;
  const season = p.season || 2027;
  closeDossier();
  onLaunchCompare(id, p.fullName, season);
}
