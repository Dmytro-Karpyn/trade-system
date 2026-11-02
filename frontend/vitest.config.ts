import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    clearMocks: true,
    // Full MUI application tests also run beside PostgreSQL containers in CI.
    // Bound worker concurrency instead of relaxing render/assertion timeouts.
    maxWorkers: 1,
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
