import type { Query } from '@tanstack/react-query'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { productSchema } from '../products/contracts'
import { counterpartySchema, partyReadPermissions } from '../counterparties/contracts'
import { partyKeys } from '../counterparties/queries'
import { availableBatchSchema, balanceSchema, costSchema, priceOptionSchema, profitSchema, saleListSchema, saleLookupsSchema, saleSchema, uahId } from './contracts'
import type { Sale, SaleFilters, SaleInput } from './contracts'

export const saleKeys = { all: ['sales'] as const, detail: (id: string) => ['sales', 'detail', id] as const, lookups: ['sale-lookups'] as const }
export function useSales(filters: SaleFilters) { const { api } = useAppServices(); const params = new URLSearchParams(Object.entries(filters).filter(([, value]) => value.trim() !== '')); return useQuery({ queryKey: ['sales', 'list', filters], queryFn: ({ signal }) => api.request(`/sales${params.size ? `?${params}` : ''}`, { signal, schema: z.array(saleListSchema) }) }) }
export function useSale(id: string) { const { api } = useAppServices(); return useQuery({ queryKey: saleKeys.detail(id), refetchOnWindowFocus: false, queryFn: ({ signal }) => api.request(`/sales/${id}`, { signal, schema: saleSchema }) }) }
export function useSaleLookups() { const { api } = useAppServices(); return useQuery({ queryKey: saleKeys.lookups, queryFn: ({ signal }) => api.request('/sales/lookups', { signal, schema: saleLookupsSchema }) }) }
export function useCustomers(activeOnly = false) {
  const { api } = useAppServices(); const user = useCurrentUser().data
  const permitted = Boolean(user && hasAnyPermission(user, partyReadPermissions))
  const query = useQuery({ queryKey: ['counterparties', 'sale-customers', activeOnly], enabled: permitted, queryFn: ({ signal }) => api.request(`/counterparties?isCustomer=true${activeOnly ? '&active=true' : ''}`, { signal, schema: z.array(counterpartySchema) }) })
  return { ...query, data: permitted ? query.data : undefined, permitted }
}
export function useSaleProducts(search: string, enabled: boolean, goodsOnly = false) { const { api } = useAppServices(); return useQuery({ queryKey: ['products', 'sale-search', search, goodsOnly], enabled, queryFn: ({ signal }) => api.request(`/products?active=true${goodsOnly ? '&itemType=GOODS' : ''}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`, { signal, schema: z.array(productSchema) }) }) }
export function useAvailableBatches(productId: string, storageLocationId: string, saleId?: string, enabled = true) {
  const { api } = useAppServices(); const user = useCurrentUser().data
  const permitted = Boolean(user && hasAnyPermission(user, ['stock.view']))
  const query = useQuery({ queryKey: ['stock', 'available-batches', productId, storageLocationId, saleId ?? ''], enabled: Boolean(enabled && productId && storageLocationId && permitted), queryFn: ({ signal }) => api.request(`/stock/available-batches?productId=${encodeURIComponent(productId)}&storageLocationId=${encodeURIComponent(storageLocationId)}${saleId ? `&excludeSaleId=${saleId}` : ''}`, { signal, schema: z.array(availableBatchSchema) }) })
  return { ...query, data: permitted ? query.data : undefined, permitted }
}
export function useSalePrices(productIds: string[], priceTypeId: string) {
  const { api } = useAppServices()
  const ids = [...new Set(productIds.filter(Boolean))]
  const queries = useQueries({ queries: ids.map(id => ({ queryKey: ['sales', 'price-options', id, priceTypeId], queryFn: ({ signal }: { signal: AbortSignal }) => api.request(`/sales/price-options?productId=${id}${priceTypeId ? `&priceTypeId=${priceTypeId}` : ''}`, { signal, schema: z.array(priceOptionSchema) }) })) })
  return { options: new Map(ids.map((id, index) => [id, queries[index].data])), pending: queries.some(query => query.isPending), error: queries.find(query => query.isError)?.error, refetch: () => Promise.all(queries.map(query => query.refetch())) }
}
export function useCustomerBalance(id: string, enabled: boolean) {
  const { api } = useAppServices(); const user = useCurrentUser().data
  const permitted = Boolean(user && hasAnyPermission(user, ['settlement.view']))
  return { ...useQuery({ queryKey: ['settlements', 'customer-uah', id], enabled: Boolean(id && enabled && permitted), queryFn: ({ signal }) => api.request(`/settlements/balances?counterpartyId=${id}&currencyId=${uahId}`, { signal, schema: z.array(balanceSchema) }) }), permitted }
}
export function useSaleCost(id: string, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['sales', 'cost', id], enabled, queryFn: ({ signal }) => api.request(`/sales/${id}/cost`, { signal, schema: costSchema }) }) }
export function useSaleProfit(id: string, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['sales', 'profit', id], enabled, queryFn: ({ signal }) => api.request(`/sales/${id}/profit`, { signal, schema: profitSchema }) }) }
export function useSaleMutations(id?: string) {
  const { api } = useAppServices(); const client = useQueryClient()
  const ownAvailability = (documentId: string) => (query: Query) => query.queryKey[0] === 'stock' && query.queryKey[1] === 'available-batches' && query.queryKey[4] === documentId
  const changed = async (sale?: Sale) => {
    if (sale) client.setQueryData(saleKeys.detail(sale.document.id), (old: Sale | undefined) => ({ ...sale, products: sale.products.length ? sale.products : old?.products ?? [], batches: sale.batches.length ? sale.batches : old?.batches ?? [] }))
    // POST ends the lifetime of excludeSaleId (valid only for a DRAFT).
    // Mark that cache stale without refetching the retiring form's query.
    const closedId = sale?.document.status === 'POSTED' ? sale.document.id : undefined
    await Promise.all([
      ...[saleKeys.all, ['settlements'], partyKeys.all].map(queryKey => client.invalidateQueries({ queryKey })),
      client.invalidateQueries({ queryKey: ['stock'], predicate: query => !closedId || !ownAvailability(closedId)(query) }),
      ...(closedId ? [client.invalidateQueries({ predicate: ownAvailability(closedId), refetchType: 'none' })] : []),
    ])
  }
  const save = useMutation({ mutationFn: (input: SaleInput) => api.request(id ? `/sales/${id}` : '/sales', { method: id ? 'PUT' : 'POST', body: input, schema: saleSchema }), onSuccess: async sale => { await changed(sale) } })
  const transition = useMutation({ onMutate: async ({ action, documentId }: { action: 'post' | 'unpost'; documentId: string }) => { if (action === 'post') await client.cancelQueries({ predicate: ownAvailability(documentId) }) }, mutationFn: ({ action, documentId }: { action: 'post' | 'unpost'; documentId: string }) => api.request(`/sales/${documentId}/${action}`, { method: 'POST', schema: saleSchema }), onSuccess: async sale => { await changed(sale) } })
  const remove = useMutation({ mutationFn: () => api.request(`/sales/${id}`, { method: 'DELETE' }), onSuccess: async () => { client.removeQueries({ queryKey: saleKeys.all, predicate: query => query.queryKey.at(-1) === id }); await changed() } })
  return { save, transition, remove }
}
