import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // ALWAYS load env from .env file (for all modes)
  // Vite automatically loads .env, .env.local, .env.[mode], .env.[mode].local
  const env = loadEnv(mode, process.cwd(), 'VITE_')
  
  // API configuration from env (ALWAYS from .env, no hardcoded fallbacks for production)
  // For dev: .env.local or .env provides these
  // For production: set at build time via build args
  const apiUrl = env.VITE_API_URL || '/api'
  const hubUrl = env.VITE_HUB_URL || '/hubs'
  
  // Calculate proxy target (remove /api suffix for proxy target)
  const apiTarget = apiUrl.replace('/api', '').replace('/hubs', '') || 'http://localhost:5000'

  console.log(`[Vite Config] Mode: ${mode}`)
  console.log(`[Vite Config] API URL: ${apiUrl}`)
  console.log(`[Vite Config] Hub URL: ${hubUrl}`)
  console.log(`[Vite Config] Proxy Target: ${apiTarget}`)

  return {
    plugins: [react()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    server: {
      port: 5173,
      host: true,
      allowedHosts: ['.devtunnels.ms', '.ngrok-free.app', '.ngrok.io', '.trycloudflare.com', '.loca.lt', '.rabb1t.tech'],
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
        '/hubs': {
          target: apiTarget,
          ws: true,
          changeOrigin: true,
          secure: false,
        },
        '/avatars': {
          target: apiTarget,
          changeOrigin: true,
          secure: false,
        },
      },
    },
    // Expose env vars to client
    define: {
      __API_URL__: JSON.stringify(apiUrl),
      __HUB_URL__: JSON.stringify(hubUrl),
    },
  }
})
