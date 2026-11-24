export const TEAM_NAMES = {
  // G-League
  IND: 'Indiana Mad Ants', MNE: 'Maine Celtics', OSC: 'Osceola Magic', WCB: 'Windy City Bulls',
  SLC: 'Salt Lake City Stars', RGV: 'Rio Grande Valley Vipers', SCW: 'Santa Cruz Warriors', GOL: 'Santa Cruz Warriors',
  STO: 'Stockton Kings', SBL: 'South Bay Lakers', ONT: 'San Diego Clippers', RAP: 'Raptors 905',
  DEL: 'Delaware Blue Coats', LIN: 'Long Island Nets', WES: 'Westchester Knicks', CAP: 'Capital City Go-Go',
  CCG: 'Capital City Go-Go', COL: 'College Park Skyhawks', GLI: 'G League Ignite', GRE: 'Greensboro Swarm',
  MHV: 'Motor City Cruise', MCC: 'Motor City Cruise', SOU: 'Sioux Falls Skyforce', SXF: 'Sioux Falls Skyforce',
  TEX: 'Texas Legends', AUS: 'Austin Spurs', CTX: 'Capitanes de Ciudad de México', MXC: 'Capitanes de Ciudad de México',
  BIR: 'Birmingham Squadron', IOW: 'Iowa Wolves', WIS: 'Wisconsin Herd', CRU: 'Cleveland Charge',
  // NBA
  ATL: 'Atlanta Hawks', BOS: 'Boston Celtics', BKN: 'Brooklyn Nets', CHA: 'Charlotte Hornets',
  CHI: 'Chicago Bulls', CLE: 'Cleveland Cavaliers', DAL: 'Dallas Mavericks', DEN: 'Denver Nuggets',
  DET: 'Detroit Pistons', GSW: 'Golden State Warriors', HOU: 'Houston Rockets', LAC: 'LA Clippers',
  LAL: 'Los Angeles Lakers', MEM: 'Memphis Grizzlies', MIA: 'Miami Heat', MIL: 'Milwaukee Bucks',
  MIN: 'Minnesota Timberwolves', NOP: 'New Orleans Pelicans', NYK: 'New York Knicks', OKC: 'Oklahoma City Thunder',
  ORL: 'Orlando Magic', PHI: 'Philadelphia 76ers', PHX: 'Phoenix Suns', POR: 'Portland Trail Blazers',
  SAC: 'Sacramento Kings', SAS: 'San Antonio Spurs', TOR: 'Toronto Raptors', UTA: 'Utah Jazz', WAS: 'Washington Wizards',
  // European / BCL / EuroCup / BSL / ACB
  RIG: 'VEF Rīga', TEN: 'Lenovo Tenerife', BUR: 'Bursaspor Basketbol', TOF: 'Tofaş SK',
  BAH: 'Bahçeşehir Koleji', KSK: 'Pınar Karşıyaka', GAL: 'Galatasaray Basketbol', BES: 'Beşiktaş Emlakjet',
  FEN: 'Fenerbahçe Beko', EFE: 'Anadolu Efes', DAR: 'Darüşşafaka Lassa', MAN: 'Manisa Basket',
  PET: 'Petkim Spor', TT: 'Türk Telekom BK', BUY: 'Büyükçekmece Basketbol', MER: 'Mersin MSK',
  YAL: 'Yalovaspor Basketbol', ZAL: 'Žalgiris Kaunas', PAO: 'Panathinaikos AKTOR', OLY: 'Olympiacos Piraeus',
  RMD: 'Real Madrid', FCB: 'FC Barcelona', MCO: 'AS Monaco', ASV: 'LDLC ASVEL Villeurbanne',
  MTA: 'Maccabi Playtika Tel Aviv', BAY: 'FC Bayern Munich', ALB: 'ALBA Berlin', VIR: 'Virtus Segafredo Bologna',
  EA7: 'EA7 Emporio Armani Milan', PAR: 'Partizan Mozzart Bet', CZV: 'Crvena Zvezda Meridianbet',
  BAS: 'Baskonia Vitoria-Gasteiz', VAL: 'Valencia Basket', HAP: 'Hapoel Tel Aviv', JLM: 'Hapoel Jerusalem',
  CAN: 'Dreamland Gran Canaria', UNI: 'Unicaja Málaga', JOV: 'Joventut Badalona', BON: 'Telekom Baskets Bonn',
  LUD: 'MHP Riesen Ludwigsburg', STR: 'SIG Strasbourg', DIJ: 'JDA Dijon', CHO: 'Cholet Basket',
  NTR: 'Nanterre 92', JL: 'JL Bourg',
  // VTB
  AST: 'BC Astana', CSK: 'CSKA Moscow', ZEN: 'Zenit Saint Petersburg', LKM: 'Lokomotiv Kuban',
  NN: 'Nizhny Novgorod', ENI: 'Enisey Krasnoyarsk', SMR: 'Samara', MBA: 'MBA Moscow',
  AUT: 'Avtodor Saratov', URA: 'Uralmash Yekaterinburg', RUN: 'Runa Basket Moscow'
};

const KNOWN_NATIONALITIES = {
  'Oscar Tshiebwe': 'COD', 'Adama Sanogo': 'MLI', 'Efe Abogidi': 'NGA', 'Veniamin Abosi': 'GRE',
  'Alberto Abalde': 'ESP', 'Awudu Abass': 'ITA', 'Ruslan Abdulbasirov': 'RUS', 'Sardal Abashidze': 'GEO',
  'Riley Abercrombie': 'AUS', 'Akwasi Abeyie Yeboah': 'GBR', 'Alex Abrines': 'ESP', 'Tim Abromaitis': 'USA',
  'Jamar Abrams': 'USA', 'Charles Abouo': 'CIV', 'Abdul-Malik Abu': 'NGA', 'Max Abmas': 'USA',
  'Muhammad-Ali Abdur-Rahkman': 'USA', 'Nikola Jokic': 'SRB', 'Luka Doncic': 'SLO',
  'Giannis Antetokounmpo': 'GRE', 'Alperen Sengun': 'TUR', 'Cedi Osman': 'TUR', 'Furkan Korkmaz': 'TUR',
  'Sertac Sanli': 'TUR', 'Melih Mahmutoglu': 'TUR', 'Onuralp Bitim': 'TUR', 'Sehmus Hazer': 'TUR',
  'Ercan Osmani': 'TUR', 'Bugrahan Tuncer': 'TUR', 'Dogus Ozdemiroglu': 'TUR', 'Erkan Yilmaz': 'TUR',
  'Kenan Sipahi': 'TUR', 'Sadik Emir Kabaca': 'TUR', 'Tarik Biberovic': 'TUR', 'Samet Yigitoglu': 'TUR',
  'Omer Faruk Yurtseven': 'TUR', 'Adem Bona': 'TUR', 'Milos Teodosic': 'SRB'
};

export function escapeHtml(text) {
  if (text == null) return '';
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

export function formatTeamName(team, league) {
  if (!team || team === '-' || team === 'Free Agent / Unassigned' || team === 'Bilinmiyor') {
    return 'Free Agent / Unassigned';
  }
  const trimmed = team.trim();
  if (league === 'NBA' && trimmed === 'IND') return 'Indiana Pacers';
  return TEAM_NAMES[trimmed] || trimmed;
}

export function formatNationality(nat, league, fullName) {
  let code = nat;
  if (fullName && KNOWN_NATIONALITIES[fullName]) {
    code = KNOWN_NATIONALITIES[fullName];
  } else if (!code || code === 'INTL' || code === 'Unknown' || code === '-' || code === '—') {
    if (league === 'NBA' || league === 'GLeague' || league === 'NCAA' || league === 'NCAAD1') {
      code = 'USA';
    } else {
      return '—';
    }
  }

  if (code) {
    const lower = code.toLowerCase().trim();
    const map = {
      american: 'USA', 'united states': 'USA', usa: 'USA', us: 'USA',
      turkish: 'TUR', turkey: 'TUR', turkiye: 'TUR', 'türkiye': 'TUR', tur: 'TUR', tr: 'TUR',
      spanish: 'ESP', spain: 'ESP', esp: 'ESP', es: 'ESP',
      serbian: 'SRB', serbia: 'SRB', srb: 'SRB', rs: 'SRB',
      greek: 'GRE', greece: 'GRE', gre: 'GRE', gr: 'GRE',
      french: 'FRA', france: 'FRA', fra: 'FRA', fr: 'FRA',
      german: 'GER', germany: 'GER', deutschland: 'GER', ger: 'GER', de: 'GER',
      italian: 'ITA', italy: 'ITA', ita: 'ITA', it: 'ITA',
      lithuanian: 'LTU', lithuania: 'LTU', ltu: 'LTU', lt: 'LTU',
      latvian: 'LAT', latvia: 'LAT', lat: 'LAT', lv: 'LAT',
      estonian: 'EST', estonia: 'EST', est: 'EST', ee: 'EST',
      croatian: 'CRO', croatia: 'CRO', cro: 'CRO', hr: 'CRO',
      slovenian: 'SLO', slovenia: 'SLO', slo: 'SLO', si: 'SLO',
      montenegrin: 'MNE', montenegro: 'MNE', mne: 'MNE', me: 'MNE',
      bosnian: 'BIH', bosnia: 'BIH', bih: 'BIH', ba: 'BIH',
      israeli: 'ISR', israel: 'ISR', isr: 'ISR', il: 'ISR',
      polish: 'POL', poland: 'POL', pol: 'POL', pl: 'POL',
      finnish: 'FIN', finland: 'FIN', fin: 'FIN', fi: 'FIN',
      belgian: 'BEL', belgium: 'BEL', bel: 'BEL', be: 'BEL',
      dutch: 'NED', netherlands: 'NED', ned: 'NED', nl: 'NED',
      ukrainian: 'UKR', ukraine: 'UKR', ukr: 'UKR', ua: 'UKR',
      czech: 'CZE', czechia: 'CZE', cze: 'CZE', cz: 'CZE',
      swedish: 'SWE', sweden: 'SWE', swe: 'SWE', se: 'SWE',
      portuguese: 'POR', portugal: 'POR', por: 'POR', pt: 'POR',
      australian: 'AUS', australia: 'AUS', aus: 'AUS', au: 'AUS',
      georgian: 'GEO', georgia: 'GEO', geo: 'GEO', ge: 'GEO',
      russian: 'RUS', russia: 'RUS', rus: 'RUS', ru: 'RUS',
      canadian: 'CAN', canada: 'CAN', can: 'CAN', ca: 'CAN',
      nigerian: 'NGA', nigeria: 'NGA', nga: 'NGA', ng: 'NGA',
      senegalese: 'SEN', senegal: 'SEN', sen: 'SEN', sn: 'SEN',
      cameroonian: 'CMR', cameroon: 'CMR', cmr: 'CMR', cm: 'CMR',
      argentine: 'ARG', argentinian: 'ARG', argentina: 'ARG', arg: 'ARG', ar: 'ARG',
      brazilian: 'BRA', brazil: 'BRA', bra: 'BRA', br: 'BRA',
      british: 'GBR', 'great britain': 'GBR', uk: 'GBR', gbr: 'GBR', gb: 'GBR'
    };
    code = map[lower] || (code.length > 3 ? code.slice(0, 3).toUpperCase() : code.toUpperCase());
  }

  return code || '—';
}

export function formatRelativeUtc(isoString) {
  if (!isoString) return 'Pending';
  const dt = new Date(isoString);
  if (Number.isNaN(dt.getTime())) return 'Pending';
  return dt.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export function formatCategory(cat) {
  if (!cat) return 'Sleeper';
  if (cat === 'MultiSeasonConsistency' || cat === 'MultiSeason') return 'Multi-Season';
  if (cat === 'LowMinute' || cat === 'LowMinuteMonster') return 'Low-Minute';
  if (cat === 'ForgottenPeak') return 'Buy-Low';
  if (cat === 'StepUpCandidate') return 'Step-Up';
  return cat;
}

export function getCategoryBadgeClass(cat) {
  if (cat === 'MultiSeasonConsistency' || cat === 'MultiSeason') return 'badge-sky';
  if (cat === 'LowMinute' || cat === 'LowMinuteMonster') return 'badge-amber';
  if (cat === 'ForgottenPeak') return 'badge-rose';
  if (cat === 'StepUpCandidate') return 'badge-emerald';
  return 'badge';
}

export function numFmt(val, decimals = 1, fallback = '—') {
  if (val == null || Number.isNaN(Number(val))) return fallback;
  return Number(val).toFixed(decimals);
}

export function pctFmt(val, decimals = 1, fallback = '—') {
  if (val == null || Number.isNaN(Number(val))) return fallback;
  return `${(Number(val) * 100).toFixed(decimals)}%`;
}

export function openFilmRoom(fullName, teamName, league) {
  const q = encodeURIComponent(`${fullName || ''} ${teamName || ''} ${league || ''} basketball highlights`.trim());
  window.open(`https://www.youtube.com/results?search_query=${q}`, '_blank', 'noopener');
}
