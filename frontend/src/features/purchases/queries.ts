import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { useCurrentUser } from '../../auth/queries'
import { hasAnyPermission } from '../../auth/contracts'
import { productSchema } from '../products/contracts'
import { partyKeys } from '../counterparties/queries'
import { counterpartySchema, partyReadPermissions } from '../counterparties/contracts'
import { batchPriceSchema, batchSchema, costSchema, locationSchema, lookupsSchema, purchaseListSchema, purchaseSchema } from './contracts'
import type { Purchase, PurchaseFilters, PurchaseInput } from './contracts'

export const purchaseKeys = { all: ['purchases'] as const, list: (filters: PurchaseFilters) => ['purchases', 'list', filters] as const, detail: (id: string) => ['purchases', 'detail', id] as const, batches: (id: string) => ['purchases', 'batches', id] as const, costs: (id: string) => ['purchases', 'costs', id] as const, prices: (id: string) => ['purchases', 'prices', id] as const, lookups: ['purchase-lookups'] as const }
export function usePurchases(filters: PurchaseFilters) {
  const { api } = useAppServices()
  const params = new URLSearchParams(Object.entries(filters).filter(([, value]) => value.trim() !== ''))
  return useQuery({ queryKey: purchaseKeys.list(filters), queryFn: ({ signal }) => api.request(`/purchases${params.size ? `?${params}` : ''}`, { signal, schema: z.array(purchaseListSchema) }) })
}
export function usePurchase(id: string) {
  const { api } = useAppServices()
  return useQuery({ queryKey: purchaseKeys.detail(id), refetchOnWindowFocus: false, queryFn: ({ signal }) => api.request(`/purchases/${encodeURIComponent(id)}`, { signal, schema: purchaseSchema }) })
}
export function usePurchaseLookups() {
  const { api } = useAppServices()
  return useQuery({ queryKey: purchaseKeys.lookups, queryFn: ({ signal }) => api.request('/purchases/lookups', { signal, schema: lookupsSchema }) })
}
export function usePurchaseBatches(id: string) {
  const { api } = useAppServices()
  return useQuery({ queryKey: purchaseKeys.batches(id), queryFn: ({ signal }) => api.request(`/purchases/${encodeURIComponent(id)}/batches`, { signal, schema: z.array(batchSchema) }) })
}
export function usePurchaseCosts(id: string, enabled: boolean) {
  const { api } = useAppServices()
  return useQuery({ queryKey: purchaseKeys.costs(id), enabled, queryFn: ({ signal }) => api.request(`/purchases/${encodeURIComponent(id)}/batch-costs`, { signal, schema: z.array(costSchema) }) })
}
export function usePurchasePrices(id: string) {
  const { api } = useAppServices()
  return useQuery({ queryKey: purchaseKeys.prices(id), queryFn: ({ signal }) => api.request(`/purchases/${encodeURIComponent(id)}/batch-prices`, { signal, schema: z.array(batchPriceSchema) }) })
}
export function usePurchaseProductSearch(search: string, enabled: boolean) {
  const { api } = useAppServices()
  return useQuery({ queryKey: ['products', 'purchase-search', search], enabled, queryFn: ({ signal }) => api.request(`/products?active=true&itemType=GOODS${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`, { signal, schema: z.array(productSchema) }) })
}
export function usePurchaseSuppliers(activeOnly = false) {
  const { api } = useAppServices()
  const user = useCurrentUser().data
  const permitted = Boolean(user && hasAnyPermission(user, partyReadPermissions))
  const query = useQuery({ queryKey: ['counterparties', 'purchase-suppliers', activeOnly], enabled: permitted, queryFn: ({ signal }) => api.request(`/counterparties?isSupplier=true${activeOnly ? '&active=true' : ''}`, { signal, schema: z.array(counterpartySchema) }) })
  return { ...query, data: permitted ? query.data : undefined, permitted }
}
export function useCreateLocation() {
  const { api } = useAppServices()
  const client = useQueryClient()
  return useMutation({ mutationFn: (name: string) => api.request('/storage-locations', { method: 'POST', body: { name }, schema: locationSchema }), onSuccess: async () => { await client.invalidateQueries({ queryKey: purchaseKeys.lookups }) } })
}
export function usePurchaseMutations(id?: string) {
  const { api } = useAppServices()
  const user = useCurrentUser().data
  const client = useQueryClient()
  const changed = async (purchase?: Purchase) => {
    if (purchase) client.setQueryData(purchaseKeys.detail(purchase.document.id), (previous: Purchase | undefined) => ({ ...purchase, products: purchase.products.length ? purchase.products : previous?.products ?? [] }))
    await Promise.all([purchaseKeys.all, purchaseKeys.lookups, ['stock'], ['batches'], ['batch-prices'], ['pricing'], ['settlements'], partyKeys.all].map(queryKey => client.invalidateQueries({ queryKey })))
  }
  const save = useMutation({ mutationFn: (input: PurchaseInput) => api.request(id ? `/purchases/${id}` : '/purchases', { method: id ? 'PUT' : 'POST', body: input, schema: purchaseSchema }), onSuccess: async purchase => { await changed(purchase) } })
  const transition = useMutation({ mutationFn: ({ action, documentId }: { action: 'post' | 'unpost'; documentId: string }) => api.request(`/purchases/${documentId}/${action}`, { method: 'POST', schema: purchaseSchema }), onSuccess: async purchase => { await changed(purchase) } })
  const remove = useMutation({ mutationFn: () => api.request(`/purchases/${id}`, { method: 'DELETE' }), onSuccess: async () => { client.removeQueries({ queryKey: purchaseKeys.all, predicate: query => query.queryKey.at(-1) === id }); await changed() } })
  return { save, transition, remove, canRead: Boolean(user && hasAnyPermission(user, ['purchase.view'])) }
}
