import { state } from './state.js';
import { initCompareView, launchCompareWith } from './components/compareView.js';
import { initDirectoryView, fetchDirectory } from './components/directoryView.js';
import {
  closeDossier,
  configureDossierHandlers,
  debounceDossierNotes,
  dossierGoBack,
  openDossier,
  openDossierFilmRoom,
  sendDossierToCompare,
  toggleDossierWatchlist
} from './components/dossierDrawer.js';
import { initSleepersView, fetchSleepers, renderSleepersTable, updateSleeperWatchlistFlag } from './components/sleepersView.js';
import { initSmartFinderView } from './components/smartFinderView.js';
import { closeSyncDrawer, initSyncDrawer } from './components/syncDrawer.js';
import { closeTeamRosterModal, openTeamRosterModal } from './components/teamRosterModal.js';
import {
  fetchScoutBoards,
  fetchWatchlist,
  initWatchlistView,
  refreshWatchlistCount,
  renderLineupSimulator
} from './components/watchlistView.js';

export function switchTab(tabId) {
  state.currentTab = tabId;
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.tab === tabId);
  });
  document.querySelectorAll('.tab-panel').forEach(panel => {
    panel.classList.toggle('hidden', panel.id !== `tab-${tabId}`);
  });

  if (tabId === 'players') {
    fetchDirectory();
  } else if (tabId === 'watchlist') {
    fetchScoutBoards().then(() => fetchWatchlist());
    renderLineupSimulator();
  } else if (tabId === 'sleepers' && state.sleepers.filtered.length > 0) {
    renderSleepersTable();
  }
}

function handleLaunchCompare(playerId, fullName, season) {
  switchTab('compare');
  launchCompareWith(playerId, fullName, season);
}

function handleOpenTeamRoster(team, league, season) {
  openTeamRosterModal(team, league, season, openDossier);
}

document.addEventListener('DOMContentLoaded', () => {
  const syncWatchlistState = async () => {
    await refreshWatchlistCount();
    await fetchScoutBoards();
    if (state.currentTab === 'watchlist') {
      await fetchWatchlist();
    } else if (state.currentTab === 'sleepers' && state.sleepers.filtered.length > 0) {
      renderSleepersTable();
    }
  };

  const sharedHandlers = {
    openDossier,
    openTeamRoster: handleOpenTeamRoster,
    launchCompare: handleLaunchCompare,
    switchTab,
    onWatchlistMutated: syncWatchlistState
  };

  configureDossierHandlers({
    watchlistMutated: () => {
      if (state.dossier.currentPlayer && (!state.watchlist.activeBoardId || state.watchlist.activeBoardId === 'default')) {
        const id = state.dossier.currentPlayer.playerId || state.dossier.currentPlayer.id;
        updateSleeperWatchlistFlag(id, state.dossier.currentPlayer.isWatchlisted);
      }
      syncWatchlistState();
    },
    launchCompare: handleLaunchCompare,
    openTeamRoster: handleOpenTeamRoster
  });

  initSleepersView(sharedHandlers);
  initSmartFinderView(sharedHandlers);
  initDirectoryView(sharedHandlers);
  initCompareView();
  initWatchlistView(sharedHandlers);
  initSyncDrawer();

  // Topbar tab bindings
  document.querySelectorAll('.tab-btn[data-tab]').forEach(btn => {
    btn.addEventListener('click', () => switchTab(btn.dataset.tab));
  });
  document.getElementById('btn-brand-home')?.addEventListener('click', () => switchTab('sleepers'));

  // Dossier Drawer bindings
  document.getElementById('dossier-drawer')?.addEventListener('click', (e) => {
    if (e.target.id === 'dossier-drawer') closeDossier();
  });
  document.getElementById('dossier-close-btn')?.addEventListener('click', closeDossier);
  document.getElementById('dossier-back-btn')?.addEventListener('click', dossierGoBack);
  document.getElementById('dossier-film-btn')?.addEventListener('click', openDossierFilmRoom);
  document.getElementById('dossier-compare-btn')?.addEventListener('click', sendDossierToCompare);
  document.getElementById('dossier-watchlist-btn')?.addEventListener('click', toggleDossierWatchlist);
  document.getElementById('dossier-scout-notes')?.addEventListener('input', debounceDossierNotes);

  // Team Roster modal bindings
  document.getElementById('team-roster-modal')?.addEventListener('click', (e) => {
    if (e.target.id === 'team-roster-modal') closeTeamRosterModal();
  });
  document.getElementById('roster-modal-close')?.addEventListener('click', closeTeamRosterModal);

  // Global keyboard shortcuts (Ctrl+K -> Smart Finder, Esc -> close drawers/modals)
  document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      switchTab('search');
      setTimeout(() => {
        const input = document.getElementById('smart-prompt-input');
        input?.focus();
        input?.select();
      }, 40);
    } else if (e.key === 'Escape') {
      closeDossier();
      closeTeamRosterModal();
      closeSyncDrawer();
    }
  });

  if (typeof lucide !== 'undefined') lucide.createIcons();

  // Initial data load
  fetchSleepers();
  fetchScoutBoards().then(() => refreshWatchlistCount());
});
