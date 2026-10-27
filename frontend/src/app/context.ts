import { createContext, useContext } from 'react'
import type { AppServices } from './services'

export const AppServicesContext = createContext<AppServices | null>(null)
export function useAppServices() {
  const services = useContext(AppServicesContext)
  if (!services) throw new Error('AppProviders is required.')
  return services
}
