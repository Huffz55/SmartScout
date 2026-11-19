import { smartSearchApi } from '../api.js';
import { state } from '../state.js';
import { escapeHtml, formatTeamName, numFmt, pctFmt } from '../utils/formatters.js';

let handlers = {};

export function initSmartFinderView(h) {
  handlers = h;
  const input = document.getElementById('smart-prompt-input');
  document.getElementById('btn-smart-search')?.addEventListener('click', executeSmartSearch);
  input?.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      executeSmartSearch();
    }
  });
  document.querySelectorAll('[data-smart-preset]').forEach(btn => {
    btn.addEventListener('click', () => {
      if (input) input.value = btn.getAttribute('data-smart-preset') || '';
      executeSmartSearch();
    });
  });
}

export async function executeSmartSearch() {
  const input = document.getElementById('smart-prompt-input');
  const prompt = input?.value.trim();
  if (!prompt) return;

  state.smartSearch.lastPrompt = prompt;
  const tbody = document.getElementById('smart-tbody');
  const countEl = document.getElementById('smart-count');
  const chipsEl = document.getElementById('smart-constraints');

  if (tbody) tbody.innerHTML = `<tr><td colspan="10" style="text-align:center;padding:28px;color:var(--text-muted)">Parsing scouting constraints & querying prospects...</td></tr>`;

  try {
    const data = await smartSearchApi(prompt, 18);
    state.smartSearch.results = data || [];
    if (countEl) countEl.textContent = `${data.length} Candidates`;

    const parsed = data[0]?.parsedConstraints || [];
    if (chipsEl) {
      chipsEl.innerHTML = parsed.map(c => `<span class="badge badge-sky">✓ ${escapeHtml(c)}</span>`).join('');
    }

    if (!data || data.length === 0) {
      tbody.innerHTML = `<tr><td colspan="10" style="text-align:center;padding:28px;color:var(--text-muted)">No matching prospects found for "${escapeHtml(prompt)}".</td></tr>`;
      return;
    }

    tbody.innerHTML = data.map(p => {
      const teamDisplay = formatTeamName(p.teamName, p.league);
      const reason = p.reasonForMatch || (p.matchReasons && p.matchReasons.join(' · ')) || '';
      return `
        <tr>
          <td>
            <button type="button" class="player-link" data-open-player="${escapeHtml(p.playerId)}" data-season="${p.season || 2026}">${escapeHtml(p.fullName)}</button>
            ${reason ? `<div style="font-size:10.5px;color:var(--text-muted);max-width:260px;overflow:hidden;text-overflow:ellipsis">${escapeHtml(reason)}</div>` : ''}
          </td>
          <td><button type="button" class="team-link" data-open-team="${escapeHtml(p.teamName || '')}" data-league="${escapeHtml(p.league || '')}" data-season="${p.season || 2026}">${escapeHtml(teamDisplay)}</button></td>
          <td><span class="badge badge-sky">${escapeHtml(p.league || '—')}</span></td>
          <td class="center"><span class="badge">${escapeHtml(p.position || 'F')}</span></td>
          <td><span class="badge">${escapeHtml(p.matchedArchetype || p.archetype || 'Role')}</span></td>
          <td class="num" style="color:var(--accent-emerald);font-weight:600">${numFmt(p.europeanCompatibilityIndex)}%</td>
          <td class="num" style="color:var(--accent-rose)">${numFmt(p.defensivePressureIndex)}%</td>
          <td class="num">${numFmt(p.points)}</td>
          <td class="num">${pctFmt(p.trueShootingPct)}</td>
          <td class="center mono" style="color:var(--text-muted)">${p.season || '—'}</td>
        </tr>
      `;
    }).join('');

    tbody.querySelectorAll('[data-open-player]').forEach(btn => {
      btn.addEventListener('click', () => handlers.openDossier(btn.dataset.openPlayer, Number(btn.dataset.season)));
    });
    tbody.querySelectorAll('[data-open-team]').forEach(btn => {
      btn.addEventListener('click', () => handlers.openTeamRoster(btn.dataset.openTeam, btn.dataset.league, Number(btn.dataset.season)));
    });
  } catch (err) {
    if (tbody) tbody.innerHTML = `<tr><td colspan="10" style="text-align:center;padding:24px;color:var(--accent-rose)">Search error: ${escapeHtml(err.message)}</td></tr>`;
  }
}
