import { defineConfig, type Plugin } from 'vite'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'

// Plugin to handle SPA routing - rewrite all requests to index.html
function spaFallback(): Plugin {
  return {
    name: 'spa-fallback',
    configureServer(server) {
      // This function runs after Vite sets up its middleware
      return () => {
        // Add our middleware at the end to catch all unmatched routes
        server.middlewares.use((req, _res, next) => {
          const url = req.url || ''
          
          // Check if URL has a file extension (static asset)
          const hasExtension = /\.\w+$/.test(url.split('?')[0])
          
          // Skip if it's a static asset, Vite internal, or API call
          if (
            hasExtension ||
            url.startsWith('/@') ||
            url.startsWith('/src/') ||
            url.startsWith('/node_modules/') ||
            url.startsWith('/vite') ||
            url.startsWith('/api/')
          ) {
            return next()
          }
          
          // For all other routes (like /matches, /players, etc.), serve index.html
          // This allows React Router to handle the routing
          if (url !== '/' && !url.startsWith('/api')) {
            req.url = '/index.html'
          }
          
          next()
        })
      }
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), spaFallback(), tailwindcss()],
  base: '/',
})
