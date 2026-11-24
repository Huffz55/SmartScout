import { comparePlayersApi, fetchPlayersApi, fetchSleepersApi } from '../api.js';
import { state } from '../state.js';
import { escapeHtml, formatTeamName, numFmt, pctFmt } from '../utils/formatters.js';

let searchTimers = { A: null, B: null };

export function initCompareView() {
  ['A', 'B'].forEach(slot => {
    const input = document.getElementById(`compare-search-${slot.toLowerCase()}`);
    input?.addEventListener('input', () => {
      clearTimeout(searchTimers[slot]);
      searchTimers[slot] = setTimeout(() => searchComparePlayer(slot), 250);
    });
    document.getElementById(`compare-season-${slot.toLowerCase()}`)?.addEventListener('change', () => {
      if (state.compare.selectedA && state.compare.selectedB) runComparison();
    });
  });

  document.querySelectorAll('[data-compare-preset]').forEach(btn => {
    btn.addEventListener('click', () => autoSelectTopComparison(btn.getAttribute('data-compare-preset') || ''));
  });

  document.addEventListener('click', (e) => {
    if (!e.target.closest('.search-dropdown-wrap')) {
      document.getElementById('compare-results-a')?.classList.add('hidden');
      document.getElementById('compare-results-b')?.classList.add('hidden');
    }
  });
}

export function launchCompareWith(playerId, fullName, season = 2027) {
  selectCompareCandidate('A', playerId, fullName, season);
}

async function searchComparePlayer(slot) {
  const key = slot.toLowerCase();
  const input = document.getElementById(`compare-search-${key}`);
  const menu = document.getElementById(`compare-results-${key}`);
  if (!input || !menu) return;
  const q = input.value.trim();
  if (q.length < 2) {
    menu.classList.add('hidden');
    return;
  }

  try {
    const data = await fetchPlayersApi(1, 8, q);
    const items = data.items || [];
    if (items.length === 0) {
      menu.innerHTML = `<div class="search-dropdown-item" style="color:var(--text-muted)">No matches found</div>`;
      menu.classList.remove('hidden');
      return;
    }

    menu.innerHTML = items.map(p => `
      <div class="search-dropdown-item" data-pick-id="${escapeHtml(p.id)}" data-pick-name="${escapeHtml(p.fullName)}" data-pick-season="${p.latestSeason || 2027}">
        <div>
          <strong style="color:var(--text-primary)">${escapeHtml(p.fullName)}</strong>
          <div style="font-size:11px;color:var(--text-secondary)">${escapeHtml(formatTeamName(p.teamName, p.league))} • ${escapeHtml(p.league || '')}</div>
        </div>
        <span class="badge">${escapeHtml(p.position || 'F')}</span>
      </div>
    `).join('');
    menu.classList.remove('hidden');

    menu.querySelectorAll('[data-pick-id]').forEach(row => {
      row.addEventListener('click', () => {
        selectCompareCandidate(slot, row.dataset.pickId, row.dataset.pickName, Number(row.dataset.pickSeason));
      });
    });
  } catch {
    menu.classList.add('hidden');
  }
}

async function autoSelectTopComparison(league) {
  try {
    const list = await fetchSleepersApi({ league: league || '', season: 0, top: 10 });
    if (!list || list.length < 2) {
      alert('Not enough candidates in this league to auto-compare.');
      return;
    }
    const a = list[0];
    const b = list[1];
    selectCompareCandidate('A', a.playerId, a.fullName, a.season || 2027);
    selectCompareCandidate('B', b.playerId, b.fullName, b.season || 2027);
  } catch (err) {
    alert(`Auto-compare failed: ${err.message}`);
  }
}

export function selectCompareCandidate(slot, id, fullName, season = 2027) {
  const key = slot.toLowerCase();
  const input = document.getElementById(`compare-search-${key}`);
  const menu = document.getElementById(`compare-results-${key}`);
  const seasonSel = document.getElementById(`compare-season-${key}`);
  if (input) input.value = fullName;
  if (menu) menu.classList.add('hidden');
  if (seasonSel && season) seasonSel.value = String(season);

  if (slot === 'A') state.compare.selectedA = { id, fullName, season };
  else state.compare.selectedB = { id, fullName, season };

  if (state.compare.selectedA && state.compare.selectedB) {
    runComparison();
  }
}

export async function runComparison() {
  const a = state.compare.selectedA;
  const b = state.compare.selectedB;
  if (!a || !b) return;

  const sA = Number(document.getElementById('compare-season-a')?.value || a.season || 2027);
  const sB = Number(document.getElementById('compare-season-b')?.value || b.season || 2027);

  const emptyEl = document.getElementById('compare-empty');
  const contentEl = document.getElementById('compare-content');
  if (emptyEl) emptyEl.textContent = 'Running head-to-head comparison...';

  try {
    const data = await comparePlayersApi(a.id, sA, b.id, sB);
    emptyEl?.classList.add('hidden');
    contentEl?.classList.remove('hidden');

    const pA = data.playerA;
    const pB = data.playerB;

    document.getElementById('comp-similarity').textContent = `${(data.similarityScore || 0).toFixed(1)}% Similarity`;
    document.getElementById('comp-verdict').textContent = data.scoutingVerdict || `${pA.fullName} vs ${pB.fullName}`;
    document.getElementById('comp-header-a').textContent = `${pA.fullName} (${pA.league || ''})`;
    document.getElementById('comp-header-b').textContent = `${pB.fullName} (${pB.league || ''})`;

    const pdfBtn = document.getElementById('btn-compare-pdf');
    if (pdfBtn) {
      pdfBtn.href = `/api/players/${a.id}/compare.pdf?season=${sA}&targetPlayerId=${b.id}&targetSeason=${sB}`;
      pdfBtn.classList.remove('hidden');
    }

    const winners = data.categoryWinners || {};
    const formatDim = (key) => String(key).replace(/([a-z0-9])([A-Z])/g, '$1 $2');
    document.getElementById('comp-winners').innerHTML = Object.entries(winners)
      .map(([cat, win]) => `<span class="badge badge-sky">${escapeHtml(formatDim(cat))}: <strong>${escapeHtml(win)}</strong></span>`)
      .join('');

    renderDualRadar(pA, pB);
    renderTapeRows(pA, pB);
    renderCompareTraits('comp-sw-a', pA);
    renderCompareTraits('comp-sw-b', pB);
  } catch (err) {
    if (emptyEl) {
      emptyEl.classList.remove('hidden');
      emptyEl.textContent = `Comparison failed: ${err.message}`;
    }
    contentEl?.classList.add('hidden');
  }
}

function renderDualRadar(pA, pB) {
  const canvas = document.getElementById('compare-radar-chart');
  if (!canvas || typeof Chart === 'undefined') return;
  if (state.compare.radarChart) state.compare.radarChart.destroy();
  const rA = pA.radarMetrics || {};
  const rB = pB.radarMetrics || {};
  const vec = (r) => [
    r.scoring ?? r.Scoring ?? 50,
    r.efficiency ?? r.Efficiency ?? 50,
    r.playmaking ?? r.Playmaking ?? 50,
    r.rebounding ?? r.Rebounding ?? 50,
    r.defense ?? r.Defense ?? 50,
    r.perimeterThreat ?? r.PerimeterThreat ?? 50
  ];

  state.compare.radarChart = new Chart(canvas.getContext('2d'), {
    type: 'radar',
    data: {
      labels: ['Scoring', 'Efficiency', 'Playmaking', 'Rebounding', 'Defense', '3P Gravity'],
      datasets: [
        {
          label: pA.fullName,
          data: vec(rA),
          backgroundColor: 'rgba(56, 189, 248, 0.16)',
          borderColor: '#38BDF8',
          borderWidth: 2
        },
        {
          label: pB.fullName,
          data: vec(rB),
          backgroundColor: 'rgba(16, 185, 129, 0.16)',
          borderColor: '#10B981',
          borderWidth: 2
        }
      ]
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
      plugins: { legend: { labels: { color: '#CBD5E1', boxWidth: 10, font: { size: 11 } } } }
    }
  });
}

function renderTapeRows(pA, pB) {
  const rows = [
    { label: 'PPG', a: pA.points, b: pB.points, fmt: v => numFmt(v) },
    { label: 'PTS / 36', a: pA.ptsPer36, b: pB.ptsPer36, fmt: v => numFmt(v) },
    { label: 'TS%', a: pA.trueShootingPct, b: pB.trueShootingPct, fmt: v => pctFmt(v) },
    { label: '3P%', a: pA.fg3Pct, b: pB.fg3Pct, fmt: v => pctFmt(v) },
    { label: 'AST / TOV', a: pA.assistToTurnoverRatio, b: pB.assistToTurnoverRatio, fmt: v => numFmt(v, 2) },
    { label: 'Stocks / 36', a: pA.stocksPer36, b: pB.stocksPer36, fmt: v => numFmt(v) },
    { label: 'ECI', a: pA.europeanCompatibilityIndex, b: pB.europeanCompatibilityIndex, fmt: v => numFmt(v) + '%' },
    { label: 'DPI', a: pA.defensivePressureIndex, b: pB.defensivePressureIndex, fmt: v => numFmt(v) + '%' },
    { label: 'ETI', a: pA.euroLeagueTransitionIndex, b: pB.euroLeagueTransitionIndex, fmt: v => numFmt(v) }
  ];

  document.getElementById('comp-tape-rows').innerHTML = rows.map(r => {
    const aWin = (r.a ?? -Infinity) > (r.b ?? -Infinity);
    const bWin = (r.b ?? -Infinity) > (r.a ?? -Infinity);
    return `
      <tr>
        <td style="color:var(--text-secondary)">${r.label}</td>
        <td class="num" style="color:${aWin ? 'var(--accent-sky)' : 'var(--text-primary)'};font-weight:${aWin ? '700' : '400'}">${r.fmt(r.a)}</td>
        <td class="num" style="color:${bWin ? 'var(--accent-emerald)' : 'var(--text-primary)'};font-weight:${bWin ? '700' : '400'}">${r.fmt(r.b)}</td>
      </tr>
    `;
  }).join('');
}

function renderCompareTraits(elId, p) {
  const el = document.getElementById(elId);
  if (!el) return;
  const s = (p.strengths || []).slice(0, 3);
  const w = (p.weaknesses || []).slice(0, 2);
  el.innerHTML = `
    <div style="font-weight:600;margin-bottom:6px">${escapeHtml(p.fullName)}</div>
    <div style="display:flex;flex-direction:column;gap:4px">
      ${s.map(t => `<span class="badge badge-emerald">+ ${escapeHtml(typeof t === 'string' ? t : t.label || t.title)}</span>`).join('')}
      ${w.map(t => `<span class="badge badge-amber">− ${escapeHtml(typeof t === 'string' ? t : t.label || t.title)}</span>`).join('')}
    </div>
  `;
}
