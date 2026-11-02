import { useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
export const settlementOverviewSchema = z.object({ counterpartyId: z.guid(), counterpartyName: z.string(), currencyId: z.guid(), balance: z.number() })
export interface SettlementFilters { search: string; state: '' | 'OWED_TO_US' | 'WE_OWE' | 'ZERO'; currencyId: string }
export function useSettlementOverview(filters: SettlementFilters) { const { api } = useAppServices(); const params = new URLSearchParams(Object.entries(filters).filter(([, v]) => v.trim() !== '')); return useQuery({ queryKey: ['settlements', 'overview', filters], queryFn: ({ signal }) => api.request(`/settlements/overview${params.size ? `?${params}` : ''}`, { signal, schema: z.array(settlementOverviewSchema) }) }) }
export function useSettlementCurrencies() { const { api } = useAppServices(); return useQuery({ queryKey: ['settlements', 'currencies'], queryFn: ({ signal }) => api.request('/settlements/currencies', { signal, schema: z.array(z.guid()) }) }) }
