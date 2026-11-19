import { fetchTeamRosterApi } from '../api.js';
import { escapeHtml, formatTeamName, numFmt, pctFmt } from '../utils/formatters.js';

export function closeTeamRosterModal() {
  document.getElementById('team-roster-modal')?.classList.add('hidden');
}

export async function openTeamRosterModal(teamName, league, season, onOpenPlayer) {
  if (!teamName) return;
  const modal = document.getElementById('team-roster-modal');
  const titleEl = document.getElementById('roster-modal-team');
  const leagueEl = document.getElementById('roster-modal-league');
  const metaEl = document.getElementById('roster-modal-meta');
  const bodyEl = document.getElementById('roster-modal-body');
  if (!modal || !bodyEl) return;

  titleEl.textContent = formatTeamName(teamName, league) || teamName;
  leagueEl.textContent = league || 'Club';
  metaEl.textContent = 'Loading club roster & analytics...';
  bodyEl.innerHTML = `<div style="padding:32px;text-align:center;color:var(--text-muted)">Loading club rotation...</div>`;
  modal.classList.remove('hidden');

  try {
    const data = await fetchTeamRosterApi(teamName, league, season);
    const injBadge = data.activeInjuriesCount > 0
      ? ` • <span style="color:var(--accent-rose);font-weight:600">${data.activeInjuriesCount} Active Injuries</span>`
      : ' • Clean Injury Report';
    metaEl.innerHTML = `${data.season || season || 2027} Season • <strong>${data.playerCount ?? 0}</strong> Players${injBadge}`;

    const roster = data.roster || [];
    if (roster.length === 0) {
      bodyEl.innerHTML = `<div style="padding:32px;text-align:center;color:var(--text-muted)">No roster records found for ${escapeHtml(teamName)}.</div>`;
      return;
    }

    bodyEl.innerHTML = `
      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>Player</th>
              <th class="center">Pos</th>
              <th class="num">Age</th>
              <th class="num">GP</th>
              <th class="num">PPG</th>
              <th class="num">RPG</th>
              <th class="num">APG</th>
              <th class="num">TS%</th>
              <th class="num">ETI</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            ${roster.map(r => `
              <tr data-roster-player="${escapeHtml(r.playerId)}" data-roster-season="${r.season || data.season || 2026}" style="cursor:pointer">
                <td><span class="player-link">${escapeHtml(r.fullName)}</span></td>
                <td class="center"><span class="badge">${escapeHtml(r.position || '—')}</span></td>
                <td class="num">${r.age ?? '—'}</td>
                <td class="num">${r.gamesPlayed ?? '—'}</td>
                <td class="num" style="font-weight:600;color:var(--text-primary)">${numFmt(r.points)}</td>
                <td class="num">${numFmt(r.rebounds)}</td>
                <td class="num">${numFmt(r.assists)}</td>
                <td class="num" style="color:var(--accent-sky)">${pctFmt(r.trueShootingPct)}</td>
                <td class="num" style="color:var(--accent-emerald);font-weight:600">${numFmt(r.euroLeagueTransitionIndex)}</td>
                <td>${r.activeInjuryStatus
                  ? `<span class="badge badge-rose">${escapeHtml(r.activeInjuryStatus)}</span>`
                  : `<span class="badge badge-emerald">Active</span>`}</td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    `;

    bodyEl.querySelectorAll('tr[data-roster-player]').forEach(row => {
      row.addEventListener('click', () => {
        const pid = row.getAttribute('data-roster-player');
        const pSeason = Number(row.getAttribute('data-roster-season')) || 2026;
        closeTeamRosterModal();
        if (onOpenPlayer) onOpenPlayer(pid, pSeason, false);
      });
    });
  } catch (err) {
    bodyEl.innerHTML = `<div class="badge badge-rose" style="display:block;padding:12px">Failed to load roster: ${escapeHtml(err.message)}</div>`;
  }
}
