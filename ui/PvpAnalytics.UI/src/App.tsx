import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import MainLayout from './layouts/MainLayout'
import StatsPage from './pages/StatsPage'
import PlayersPage from './pages/PlayersPage'
import PlayerProfilePage from './pages/PlayerProfilePage'
import MatchesPage from './pages/MatchesPage'
import UploadPage from './pages/UploadPage'
import CustomMetricsPage from './pages/CustomMetricsPage'
import ReportBuilderPage from './pages/ReportBuilderPage'
import MatchDetailPage from './pages/MatchDetailPage'
import TeamsPage from './pages/TeamsPage'
import TeamSynergyPage from './pages/TeamSynergyPage'
import TeamLeaderboardPage from './pages/TeamLeaderboardPage'
import ProfileSettingsPage from './pages/ProfileSettingsPage'
import FavoritesPage from './pages/FavoritesPage'
import RivalsPage from './pages/RivalsPage'
import HighlightsPage from './pages/HighlightsPage'
import CommunityRankingsPage from './pages/CommunityRankingsPage'
import DiscoveryPage from './pages/DiscoveryPage'
import BuildsPage from './pages/BuildsPage'

const App = () => {
  return (
    <BrowserRouter>
      <MainLayout>
        <Routes>
          <Route path="/" element={<StatsPage />} />
          <Route path="/players" element={<PlayersPage />} />
          <Route path="/players/:id" element={<PlayerProfilePage />} />
          <Route path="/matches" element={<MatchesPage />} />
          <Route path="/matches/:id" element={<MatchDetailPage />} />
          <Route path="/upload" element={<UploadPage />} />
          <Route path="/metrics" element={<CustomMetricsPage />} />
          <Route path="/reports" element={<ReportBuilderPage />} />
          <Route path="/teams" element={<TeamsPage />} />
          <Route path="/teams/:teamId" element={<TeamSynergyPage />} />
          <Route path="/leaderboards" element={<TeamLeaderboardPage />} />
          <Route path="/profile" element={<ProfileSettingsPage />} />
          <Route path="/favorites" element={<FavoritesPage />} />
          <Route path="/rivals" element={<RivalsPage />} />
          <Route path="/highlights" element={<HighlightsPage />} />
          <Route path="/rankings" element={<CommunityRankingsPage />} />
          <Route path="/builds" element={<BuildsPage />} />
          <Route path="/discover" element={<DiscoveryPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </MainLayout>
    </BrowserRouter>
  )
}

export default App
