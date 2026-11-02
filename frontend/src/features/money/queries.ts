import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { accountOverviewSchema, accountSchema, moneyDocumentSchema, moneyListSchema, moneyLookupsSchema } from './contracts'
import type { AccountValues, MoneyDocument, MoneyFilters, MoneyKind } from './contracts'

const parameters = (values: Record<string, string>) => { const p = new URLSearchParams(Object.entries(values).filter(([, value]) => value !== '')); return p.size ? `?${p}` : '' }
export const moneyKeys = { all: ['money'] as const, lookups: ['money', 'lookups'] as const, accounts: ['money', 'account-overview'] as const, detail: (kind: MoneyKind, id: string) => ['money', kind, id] as const }
export function useMoneyAccounts() { const { api } = useAppServices(); return useQuery({ queryKey: moneyKeys.accounts, queryFn: ({ signal }) => api.request('/money/account-overview', { signal, schema: z.array(accountOverviewSchema) }) }) }
export function useMoneyLookups() { const { api } = useAppServices(); return useQuery({ queryKey: moneyKeys.lookups, queryFn: ({ signal }) => api.request('/money/lookups', { signal, schema: moneyLookupsSchema }) }) }
export function useMoneyList(kind: MoneyKind, filters: MoneyFilters) { const { api } = useAppServices(); return useQuery({ queryKey: ['money', kind, 'list', filters], queryFn: ({ signal }) => api.request(`/money/${kind}` + parameters({ ...filters }), { signal, schema: z.array(moneyListSchema) }) }) }
export function useMoneyDocument(kind: MoneyKind, id?: string) { const { api } = useAppServices(); return useQuery({ queryKey: moneyKeys.detail(kind, id ?? ''), enabled: Boolean(id), refetchOnWindowFocus: false, queryFn: ({ signal }) => api.request(`/money/${kind}/${id}`, { signal, schema: moneyDocumentSchema }) }) }
export function useAccountMutation() {
  const { api } = useAppServices(); const client = useQueryClient()
  return useMutation({ mutationFn: ({ id, input }: { id?: string; input: AccountValues }) => api.request('/money/accounts' + (id ? `/${id}` : ''), { method: id ? 'PUT' : 'POST', body: input, schema: accountSchema }), onSuccess: async () => { await client.invalidateQueries({ queryKey: moneyKeys.all }) } })
}
export function useMoneyMutations(kind: MoneyKind) {
  const { api } = useAppServices(); const client = useQueryClient()
  const changed = async (money?: MoneyDocument) => { if (money) client.setQueryData(moneyKeys.detail(kind, money.document.id), money); await Promise.all([moneyKeys.all, ['settlements'], ['counterparties']].map(queryKey => client.invalidateQueries({ queryKey }))) }
  const save = useMutation({ mutationFn: ({ id, input }: { id?: string; input: unknown }) => api.request(`/money/${kind}` + (id ? `/${id}` : ''), { method: id ? 'PUT' : 'POST', body: input, schema: moneyDocumentSchema }), onSuccess: changed })
  const transition = useMutation({ mutationFn: ({ id, action }: { id: string; action: 'post' | 'unpost' }) => api.request(`/money/${kind}/${id}/${action}`, { method: 'POST', schema: moneyDocumentSchema }), onSuccess: changed })
  const remove = useMutation({ mutationFn: (id: string) => api.request(`/money/${kind}/${id}`, { method: 'DELETE' }), onSuccess: async () => { await changed() } })
  return { save, transition, remove }
}
