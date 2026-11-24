export const SYNC_LEAGUES = [
  { code: 'NBA', name: 'National Basketball Association', region: 'USA', coeff: '1.00' },
  { code: 'EuroLeague', name: 'Turkish Airlines EuroLeague', region: 'EUR', coeff: '0.88' },
  { code: 'ACB', name: 'Liga Endesa ACB', region: 'ESP', coeff: '0.82' },
  { code: 'EuroCup', name: 'BKT EuroCup', region: 'EUR', coeff: '0.78' },
  { code: 'BSL', name: 'Basketbol Süper Ligi', region: 'TUR', coeff: '0.76' },
  { code: 'BCL', name: 'FIBA Basketball Champions League', region: 'INTL', coeff: '0.75' },
  { code: 'VTB', name: 'VTB United League', region: 'RUS', coeff: '0.75' },
  { code: 'ABA', name: 'AdmiralBet ABA League', region: 'ADR', coeff: '0.72' },
  { code: 'BBL', name: 'Basketball Bundesliga', region: 'GER', coeff: '0.72' },
  { code: 'LNB', name: 'Betclic Élite LNB Pro A', region: 'FRA', coeff: '0.72' },
  { code: 'LegaA', name: 'Lega Basket Serie A', region: 'ITA', coeff: '0.72' },
  { code: 'GBL', name: 'Greek Basket League', region: 'GRE', coeff: '0.71' },
  { code: 'GLeague', name: 'NBA G-League', region: 'USA', coeff: '0.70' },
  { code: 'LKL', name: 'Betsafe-LKL Lithuania', region: 'LTU', coeff: '0.68' },
  { code: 'NCAAD1', name: 'NCAA Division I', region: 'USA', coeff: '0.65' }
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
