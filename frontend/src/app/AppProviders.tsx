import { useState } from 'react'
import type { ReactNode } from 'react'
import { QueryClientProvider } from '@tanstack/react-query'
import { CssBaseline, ThemeProvider } from '@mui/material'
import { theme } from '../theme/theme'
import { AppErrorBoundary } from '../components/AppErrorBoundary'
import { createAppServices } from './services'
import type { AppServices } from './services'
import { AppServicesContext } from './context'

export function AppProviders({ children, services }: { children: ReactNode; services?: AppServices }) {
  const [defaultServices] = useState(() => services ?? createAppServices())
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <AppErrorBoundary>
        <AppServicesContext.Provider value={defaultServices}>
          <QueryClientProvider client={defaultServices.queryClient}>{children}</QueryClientProvider>
        </AppServicesContext.Provider>
      </AppErrorBoundary>
    </ThemeProvider>
  )
}
