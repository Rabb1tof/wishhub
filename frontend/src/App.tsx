import { Routes, Route } from 'react-router-dom'
import { useAuthStore } from './store/auth'
import { LoginPage } from './pages/LoginPage'
import { RegisterPage } from './pages/RegisterPage'
import { AuthCallbackPage } from './pages/AuthCallbackPage'
import { FeedPage } from './pages/FeedPage'
import { WishlistPage } from './pages/WishlistPage'
import { ProfilePage } from './pages/ProfilePage'
import { FriendsPage } from './pages/FriendsPage'
import { MessagesPage } from './pages/MessagesPage'
import { SettingsPage } from './pages/SettingsPage'

function App() {
  // Drop page data and drafts when another tab changes the login.
  const sessionVersion = useAuthStore((state) => state.sessionVersion)
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/auth/callback" element={<AuthCallbackPage />} />
      <Route path="/" element={<FeedPage key={sessionVersion} />} />
      <Route path="/wishlist" element={<WishlistPage key={sessionVersion} />} />
      <Route path="/wishlist/:username" element={<WishlistPage key={sessionVersion} />} />
      <Route path="/profile/:username" element={<ProfilePage key={sessionVersion} />} />
      <Route path="/friends" element={<FriendsPage key={sessionVersion} />} />
      <Route path="/messages" element={<MessagesPage key={sessionVersion} />} />
      <Route path="/settings" element={<SettingsPage key={sessionVersion} />} />
    </Routes>
  )
}

export default App
