import { fetchPlayersApi } from '../api.js';
import { state } from '../state.js';
import { escapeHtml, formatNationality, formatTeamName } from '../utils/formatters.js';

let handlers = {};
let searchTimer = null;
let directoryAbort = null;

export function initDirectoryView(h) {
  handlers = h;
  document.getElementById('directory-search-input')?.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      state.directory.page = 1;
      fetchDirectory();
    }, 300);
  });
  document.getElementById('directory-pagesize')?.addEventListener('change', (e) => {
    state.directory.pageSize = Number(e.target.value) || 25;
    state.directory.page = 1;
    fetchDirectory();
  });
  document.getElementById('directory-prev-btn')?.addEventListener('click', () => {
    if (state.directory.page > 1) {
      state.directory.page--;
      fetchDirectory();
    }
  });
  document.getElementById('directory-next-btn')?.addEventListener('click', () => {
    if (state.directory.page < state.directory.totalPages) {
      state.directory.page++;
      fetchDirectory();
    }
  });
}

export async function fetchDirectory() {
  const q = document.getElementById('directory-search-input')?.value.trim() || '';
  const tbody = document.getElementById('directory-tbody');
  const infoEl = document.getElementById('directory-page-info');
  if (tbody) tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:28px;color:var(--text-muted)">Loading player directory...</td></tr>`;

  if (directoryAbort) directoryAbort.abort();
  directoryAbort = new AbortController();

  try {
    const data = await fetchPlayersApi(state.directory.page, state.directory.pageSize, q, directoryAbort.signal);
    state.directory.items = data.items || [];
    state.directory.totalCount = data.totalCount || 0;
    state.directory.totalPages = Math.ceil(state.directory.totalCount / (data.pageSize || state.directory.pageSize)) || 1;

    if (infoEl) {
      infoEl.textContent = `Page ${data.page || state.directory.page} of ${state.directory.totalPages} (${state.directory.totalCount.toLocaleString()} prospects)`;
    }

    if (state.directory.items.length === 0) {
      tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:28px;color:var(--text-muted)">No players found.</td></tr>`;
      return;
    }

    tbody.innerHTML = state.directory.items.map(p => {
      const teamDisplay = formatTeamName(p.teamName, p.league);
      const natDisplay = formatNationality(p.nationality, p.league, p.fullName);
      return `
        <tr>
          <td><button type="button" class="player-link" data-open-player="${escapeHtml(p.id)}">${escapeHtml(p.fullName)}</button></td>
          <td class="center"><span class="badge">${escapeHtml(p.position || 'F')}</span></td>
          <td><button type="button" class="team-link" data-open-team="${escapeHtml(p.teamName || '')}" data-league="${escapeHtml(p.league || '')}">${escapeHtml(teamDisplay)}</button></td>
          <td class="center"><span class="badge badge-sky">${escapeHtml(p.league || '—')}</span></td>
          <td class="center mono">${escapeHtml(natDisplay)}</td>
          <td style="text-align:right">
            <button type="button" class="btn btn-sm" data-open-player="${escapeHtml(p.id)}">Dossier</button>
          </td>
        </tr>
      `;
    }).join('');

    tbody.querySelectorAll('[data-open-player]').forEach(btn => {
      btn.addEventListener('click', () => handlers.openDossier(btn.dataset.openPlayer, null));
    });
    tbody.querySelectorAll('[data-open-team]').forEach(btn => {
      btn.addEventListener('click', () => handlers.openTeamRoster(btn.dataset.openTeam, btn.dataset.league, null));
    });
  } catch (err) {
    if (err.name === 'AbortError') return;
    if (tbody) tbody.innerHTML = `<tr><td colspan="6" style="text-align:center;padding:24px;color:var(--accent-rose)">Error: ${escapeHtml(err.message)}</td></tr>`;
  }
}
