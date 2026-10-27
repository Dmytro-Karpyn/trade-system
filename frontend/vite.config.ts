import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: 'react-vendor', test: /node_modules\/(react|react-dom|scheduler)\//, priority: 20 },
            { name: 'mui-vendor', test: /node_modules\/(@mui|@emotion|stylis)\//, priority: 10 },
          ],
        },
      },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        rewrite: path => path.replace(/^\/api/, ''),
      },
      '/health': 'http://localhost:5080',
    },
  },
})
