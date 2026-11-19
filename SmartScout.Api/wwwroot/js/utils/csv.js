import { state } from '../state.js';
import { formatTeamName } from './formatters.js';

export function downloadCsvFile(filename, rows) {
  const csv = rows
    .map(r => r.map(v => `"${String(v ?? '').replace(/"/g, '""')}"`).join(','))
    .join('\n');
  const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

export function exportSleepersCsv() {
  const items = state.sleepers.filtered;
  if (!items || items.length === 0) {
    alert('No sleeper prospects to export.');
    return;
  }
  const rows = [
    ['Player', 'Position', 'Age', 'Team', 'League', 'Season', 'Category', 'SleeperScore', 'ETI', 'PPG', 'PTS/36', 'TS%', '3P%', 'Stocks/36', 'Archetype']
  ];
  for (const p of items) {
    rows.push([
      p.fullName,
      p.position || '',
      p.age ?? '',
      formatTeamName(p.teamName, p.league),
      p.league || '',
      p.season || '',
      p.sleeperCategory || '',
      p.sleeperScore != null ? p.sleeperScore.toFixed(1) : '',
      p.euroLeagueTransitionIndex != null ? p.euroLeagueTransitionIndex.toFixed(1) : '',
      p.points != null ? p.points.toFixed(1) : '',
      p.ptsPer36 != null ? p.ptsPer36.toFixed(1) : '',
      p.trueShootingPct != null ? (p.trueShootingPct * 100).toFixed(1) + '%' : '',
      p.fg3Pct != null ? (p.fg3Pct * 100).toFixed(1) + '%' : '',
      p.stocksPer36 != null ? p.stocksPer36.toFixed(1) : '',
      p.archetype || ''
    ]);
  }
  downloadCsvFile(`SmartScout_Sleepers_${new Date().toISOString().slice(0, 10)}.csv`, rows);
}

export function exportWatchlistCsv() {
  const items = state.watchlist.items;
  if (!items || items.length === 0) {
    alert('Watchlist is empty.');
    return;
  }
  const rows = [
    ['Player', 'Position', 'Age', 'Team', 'League', 'Nationality', 'ETI', '3P Rate', 'Stocks/36', 'Market Tier', 'Contract Expiry', 'Scout Notes']
  ];
  for (const p of items) {
    const rawEti = p.euroLeagueTransitionIndex ?? p.latestStat?.euroLeagueTransitionIndex;
    const rawS36 = p.stocksPer36 ?? p.latestStat?.stocksPer36;
    rows.push([
      p.fullName,
      p.position || p.primaryPosition || '',
      p.age ?? '',
      formatTeamName(p.teamName, p.league),
      p.league || '',
      p.nationality || '',
      rawEti != null ? rawEti.toFixed(1) : '',
      p.threePointRate != null ? (p.threePointRate * 100).toFixed(0) + '%' : '',
      rawS36 != null ? rawS36.toFixed(1) : '',
      p.estimatedMarketValueTier || '',
      p.contractExpirySeason || '',
      p.scoutNotes || ''
    ]);
  }
  downloadCsvFile(`SmartScout_Watchlist_${new Date().toISOString().slice(0, 10)}.csv`, rows);
}
