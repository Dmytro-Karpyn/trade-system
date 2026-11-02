import type { Query } from '@tanstack/react-query'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { candidateSchema, movementSchema, operationApi, operationSchema, reasonSchema, reservationSchema, stockBatchSchema, stockLookupsSchema, stockProductSchema, stockRowSchema } from './contracts'
import type { OperationKind, StockFilters, StockOperation } from './contracts'

function params(values: Record<string, string | boolean | undefined>) { const p = new URLSearchParams(Object.entries(values).filter(([, v]) => v !== '' && v !== undefined).map(([k, v]) => [k, String(v)])); return p.size ? `?${p}` : '' }
export function useStockLookups() { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'lookups'], queryFn: ({ signal }) => api.request('/stock/lookups', { signal, schema: stockLookupsSchema }) }) }
export function useStockOverview(filters: StockFilters, canCost: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'overview', filters, canCost], queryFn: ({ signal }) => api.request('/stock/overview' + params({ ...filters }), { signal, schema: z.array(stockRowSchema) }) }) }
export function useStockProduct(id: string, retail: string, wholesale: string, canCost: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'product', id, retail, wholesale, canCost], queryFn: ({ signal }) => api.request(`/stock/products/${id}` + params({ retailPriceTypeId: retail, wholesalePriceTypeId: wholesale }), { signal, schema: stockProductSchema }) }) }
export function useStockBatch(id: string, canCost: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'batch', id, canCost], queryFn: ({ signal }) => api.request(`/stock/batches/${id}`, { signal, schema: stockBatchSchema }) }) }
export function useStockMovements(scope: { productId?: string; batchId?: string; storageLocationId?: string }, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'movements', scope], enabled, queryFn: ({ signal }) => api.request('/stock/movements' + params(scope), { signal, schema: z.array(movementSchema) }) }) }
export function useReservations(scope: { productId?: string; batchId?: string; storageLocationId?: string }, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'reservations', scope], enabled, queryFn: ({ signal }) => api.request('/stock/reservations' + params(scope), { signal, schema: z.array(reservationSchema) }) }) }
export function useStockCandidates(locationId: string, excludeId?: string, enabled = true) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'operation-batches', locationId, excludeId ?? ''], enabled: Boolean(locationId && enabled), queryFn: ({ signal }) => api.request('/stock/operation-batches' + params({ storageLocationId: locationId, excludeDocumentId: excludeId }), { signal, schema: z.array(candidateSchema) }) }) }
export function useOperations(kind: OperationKind, filters: { from: string; to: string; status: string }) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', kind, 'list', filters], queryFn: ({ signal }) => api.request(operationApi(kind) + params(filters), { signal, schema: z.array(operationSchema) }) }) }
export function useOperation(kind: OperationKind, id?: string) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', kind, id], enabled: Boolean(id), refetchOnWindowFocus: false, queryFn: ({ signal }) => api.request(`${operationApi(kind)}/${id}`, { signal, schema: operationSchema }) }) }
export function useOperationRows(kind: OperationKind, id?: string) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', kind, id, 'stock-lines'], enabled: Boolean(id), queryFn: ({ signal }) => api.request(`${operationApi(kind)}/${id}/stock-lines`, { signal, schema: z.array(stockRowSchema) }) }) }
export function useReasons(enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'write-off-reasons'], enabled, queryFn: ({ signal }) => api.request('/write-off-reasons', { signal, schema: z.array(reasonSchema) }) }) }
export function useWriteOffCost(id?: string, enabled = false) { const { api } = useAppServices(); return useQuery({ queryKey: ['stock', 'write-off-cost', id], enabled: Boolean(id && enabled), queryFn: ({ signal }) => api.request(`/write-offs/${id}/cost`, { signal, schema: z.object({ totalCostUah: z.number() }) }) }) }
export function useOperationMutations(kind: OperationKind) {
  const { api } = useAppServices(); const client = useQueryClient()
  const ownCandidates = (id: string) => (query: Query) => query.queryKey[0] === 'stock' && query.queryKey[1] === 'operation-batches' && query.queryKey[3] === id
  const changed = async (doc?: StockOperation) => {
    // Own-reserve exclusion is valid for a DRAFT only, including the retiring form after POST.
    const closedId = doc?.document.status === 'POSTED' ? doc.document.id : undefined
    await Promise.all([
      ...[['sales'], ['purchases'], ['settlements']].map(queryKey => client.invalidateQueries({ queryKey })),
      client.invalidateQueries({ queryKey: ['stock'], predicate: query => !closedId || !ownCandidates(closedId)(query) }),
      ...(closedId ? [client.invalidateQueries({ predicate: ownCandidates(closedId), refetchType: 'none' })] : []),
    ])
  }
  const save = useMutation({ mutationFn: ({ id, input }: { id?: string; input: unknown }) => api.request(operationApi(kind) + (id ? `/${id}` : ''), { method: id ? 'PUT' : 'POST', body: input, schema: operationSchema }), onSuccess: changed })
  const transition = useMutation({ onMutate: async ({ id, action }: { id: string; action: 'post' | 'unpost' }) => { if (action === 'post') await client.cancelQueries({ predicate: ownCandidates(id) }) }, mutationFn: ({ id, action }: { id: string; action: 'post' | 'unpost' }) => api.request(`${operationApi(kind)}/${id}/${action}`, { method: 'POST', schema: operationSchema }), onSuccess: changed })
  const remove = useMutation({ mutationFn: (id: string) => api.request(`${operationApi(kind)}/${id}`, { method: 'DELETE' }), onSuccess: () => changed() })
  return { save, transition, remove }
}
