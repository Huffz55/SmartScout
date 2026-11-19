const API_KEY_STORAGE = 'smartscout_sync_api_key';

export function getSyncApiKey() {
  return sessionStorage.getItem(API_KEY_STORAGE) || '';
}

export function setSyncApiKey(key) {
  if (key) sessionStorage.setItem(API_KEY_STORAGE, key);
  else sessionStorage.removeItem(API_KEY_STORAGE);
}

async function requestJson(url, options = {}) {
  const res = await fetch(url, options);
  const text = await res.text();
  let data = null;
  if (text) {
    try { data = JSON.parse(text); } catch { data = { message: text }; }
  }
  if (!res.ok) {
    const err = new Error(data?.detail || data?.message || data?.title || `HTTP ${res.status} ${res.statusText}`);
    err.status = res.status;
    err.data = data;
    throw err;
  }
  return data;
}

export function fetchSleepersApi({ season, category, league, position, maxAge, top = 100 }) {
  const params = new URLSearchParams({ season: String(season ?? 0), top: String(top) });
  if (category) params.set('category', category);
  if (league) params.set('league', league);
  if (position && position !== 'ALL') params.set('position', position);
  if (maxAge && maxAge !== 'ALL') params.set('maxAge', maxAge);
  return requestJson(`/api/players/sleepers?${params.toString()}`);
}

export function smartSearchApi(prompt, top = 18) {
  return requestJson(`/api/players/smart-search?prompt=${encodeURIComponent(prompt)}&top=${top}`);
}

export function fetchPlayersApi(page = 1, pageSize = 25, search = '') {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search) params.set('search', search);
  return requestJson(`/api/players?${params.toString()}`);
}

export function fetchPlayerProfileApi(playerId, season) {
  const qs = season ? `?season=${season}` : '';
  return requestJson(`/api/players/${playerId}/profile${qs}`);
}

export function fetchSimilarPlayersApi(playerId, season, top = 4, minGamesPlayed = 10) {
  return requestJson(`/api/players/${playerId}/similar?season=${season}&top=${top}&minGamesPlayed=${minGamesPlayed}`);
}

export function comparePlayersApi(idA, seasonA, idB, seasonB) {
  return requestJson(`/api/players/${idA}/compare?season=${seasonA}&targetPlayerId=${idB}&targetSeason=${seasonB}`);
}

export function fetchTeamRosterApi(team, league, season) {
  const params = new URLSearchParams({ team });
  if (league) params.set('league', league);
  if (season) params.set('season', String(season));
  return requestJson(`/api/players/teams/roster?${params.toString()}`);
}

export function fetchBoardsApi() {
  return requestJson('/api/boards');
}

export function createBoardApi(name) {
  return requestJson('/api/boards', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name })
  });
}

export function deleteBoardApi(boardId) {
  return requestJson(`/api/boards/${boardId}`, { method: 'DELETE' });
}

export function fetchWatchlistItemsApi(boardId) {
  const url = boardId && boardId !== 'default'
    ? `/api/boards/${boardId}/players`
    : '/api/players/watchlist';
  return requestJson(url);
}

export function toggleWatchlistItemApi(boardId, playerId) {
  const url = boardId && boardId !== 'default'
    ? `/api/boards/${boardId}/players/${playerId}`
    : `/api/players/${playerId}/watchlist`;
  return requestJson(url, { method: 'POST' });
}

export function saveNotesApi(boardId, playerId, notes) {
  const url = boardId && boardId !== 'default'
    ? `/api/boards/${boardId}/players/${playerId}/notes`
    : `/api/players/${playerId}/notes`;
  return requestJson(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ notes })
  });
}

export function seedDemoWatchlistApi() {
  return requestJson('/api/players/watchlist/seed-demo', { method: 'POST' });
}

export function fetchSyncStatusApi() {
  return requestJson('/api/players/sync-status');
}

export function syncLeagueStatsApi(league, season, apiKey) {
  return requestJson(`/api/players/sync-stats?league=${encodeURIComponent(league)}&season=${encodeURIComponent(season)}&force=true`, {
    method: 'POST',
    headers: { 'X-Api-Key': apiKey }
  });
}

export function enrichBiosApi(apiKey, limit = 50) {
  return requestJson(`/api/players/enrich-bios?limit=${limit}`, {
    method: 'POST',
    headers: { 'X-Api-Key': apiKey }
  });
}

export function syncInjuriesApi(apiKey) {
  return requestJson('/api/players/sync-injuries', {
    method: 'POST',
    headers: { 'X-Api-Key': apiKey }
  });
}
