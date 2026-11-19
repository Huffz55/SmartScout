export const SYNC_LEAGUES = [
  { code: 'NBA', name: 'National Basketball Association', flag: '🇺🇸', coeff: '1.00' },
  { code: 'EuroLeague', name: 'Turkish Airlines EuroLeague', flag: '🇪🇺', coeff: '0.88' },
  { code: 'ACB', name: 'Liga Endesa ACB', flag: '🇪🇸', coeff: '0.82' },
  { code: 'EuroCup', name: 'BKT EuroCup', flag: '🇪🇺', coeff: '0.78' },
  { code: 'BSL', name: 'Basketbol Süper Ligi', flag: '🇹🇷', coeff: '0.76' },
  { code: 'BCL', name: 'FIBA Basketball Champions League', flag: '🏀', coeff: '0.75' },
  { code: 'VTB', name: 'VTB United League', flag: '🇷🇺', coeff: '0.75' },
  { code: 'ABA', name: 'AdmiralBet ABA League', flag: '🇦🇩', coeff: '0.72' },
  { code: 'BBL', name: 'Basketball Bundesliga', flag: '🇩🇪', coeff: '0.72' },
  { code: 'LNB', name: 'Betclic Élite LNB Pro A', flag: '🇫🇷', coeff: '0.72' },
  { code: 'LegaA', name: 'Lega Basket Serie A', flag: '🇮🇹', coeff: '0.72' },
  { code: 'GBL', name: 'Greek Basket League', flag: '🇬🇷', coeff: '0.71' },
  { code: 'GLeague', name: 'NBA G-League', flag: '🇺🇸', coeff: '0.70' },
  { code: 'LKL', name: 'Betsafe-LKL Lithuania', flag: '🇱🇹', coeff: '0.68' },
  { code: 'NCAAD1', name: 'NCAA Division I', flag: '🇺🇸', coeff: '0.65' }
];

export const state = {
  currentTab: 'sleepers',
  sleepers: {
    raw: [],
    filtered: [],
    page: 1,
    pageSize: 50,
    sortKey: 'score',
    sortDir: 'desc'
  },
  directory: {
    items: [],
    page: 1,
    pageSize: 25,
    totalPages: 1,
    totalCount: 0,
    sortKey: 'fullName',
    sortDir: 'asc'
  },
  smartSearch: {
    results: [],
    lastPrompt: ''
  },
  compare: {
    selectedA: null,
    selectedB: null,
    radarChart: null
  },
  watchlist: {
    boards: [],
    activeBoardId: 'default',
    items: [],
    lineupExpanded: false,
    lineupSlots: { PG: null, SG: null, SF: null, PF: null, C: null }
  },
  dossier: {
    historyStack: [],
    currentPlayer: null,
    radarChart: null,
    trajectoryChart: null,
    notesTimeout: null
  },
  sync: {
    selectedLeagues: new Set(SYNC_LEAGUES.map(l => l.code)),
    status: null
  }
};
