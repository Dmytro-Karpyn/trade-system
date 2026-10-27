import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { counterpartySchema, priceReadPermissions, priceTypeSchema, settlementSchema } from './contracts'
import type { PartyFilters, PartyInput } from './contracts'

export const partyKeys = { all: ['counterparties'] as const, list: (filters: PartyFilters) => ['counterparties', 'list', filters] as const, detail: (id: string) => ['counterparties', 'detail', id] as const, settlements: (id: string) => ['counterparties', 'settlements', id] as const }
export const priceTypesKey = ['price-types'] as const
export function useCounterparties(filters: PartyFilters) {
  const { api } = useAppServices()
  const params = new URLSearchParams()
  if (filters.search.trim()) params.set('search', filters.search.trim())
  if (filters.active !== '') params.set('active', filters.active)
  const roleParams = { customer: 'isCustomer', supplier: 'isSupplier', employee: 'isEmployee', system: 'isSystem' }
  if (filters.role) params.set(roleParams[filters.role], 'true')
  return useQuery({ queryKey: partyKeys.list(filters), queryFn: ({ signal }) => api.request(`/counterparties${params.size ? `?${params}` : ''}`, { signal, schema: z.array(counterpartySchema) }) })
}
export function useCounterparty(id: string) {
  const { api } = useAppServices()
  return useQuery({ queryKey: partyKeys.detail(id), queryFn: ({ signal }) => api.request(`/counterparties/${encodeURIComponent(id)}`, { signal, schema: counterpartySchema }) })
}
export function usePriceTypes() {
  const { api } = useAppServices()
  const user = useCurrentUser().data
  const permitted = Boolean(user && hasAnyPermission(user, priceReadPermissions))
  const query = useQuery({ queryKey: priceTypesKey, enabled: permitted, queryFn: ({ signal }) => api.request('/price-types', { signal, schema: z.array(priceTypeSchema) }) })
  // Do not expose cached lookup data to a role which no longer has permission to read it.
  return { ...query, data: permitted ? query.data : undefined, permitted }
}
export function usePartySettlements(id: string) {
  const { api } = useAppServices()
  return useQuery({ queryKey: partyKeys.settlements(id), queryFn: ({ signal }) => api.request(`/settlements/counterparties/${encodeURIComponent(id)}`, { signal, schema: settlementSchema }) })
}
export function useSaveCounterparty(id?: string) {
  const { api } = useAppServices()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: PartyInput) => api.request(id ? `/counterparties/${encodeURIComponent(id)}` : '/counterparties', { method: id ? 'PUT' : 'POST', body: input, schema: counterpartySchema }),
    onSuccess: async party => { queryClient.setQueryData(partyKeys.detail(party.id), party); await queryClient.invalidateQueries({ queryKey: partyKeys.all }) },
  })
}
