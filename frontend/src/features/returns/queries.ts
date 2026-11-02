import type { Query } from '@tanstack/react-query'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { returnConfig, returnListSchema, returnLookupsSchema, returnSchema, returnableSchema, previewSchema } from './contracts'
import type { ReturnDocument, ReturnFilters, ReturnKind } from './contracts'

export function useReturnLookups(kind: ReturnKind) { const { api } = useAppServices(); return useQuery({ queryKey: ['returns', kind, 'lookups'], queryFn: ({ signal }) => api.request(`${returnConfig[kind].root}/lookups`, { signal, schema: returnLookupsSchema }) }) }
export function useReturns(kind: ReturnKind, filters: Partial<ReturnFilters> = {}, enabled = true) { const { api } = useAppServices(); const config = returnConfig[kind]; const params = new URLSearchParams(Object.entries(filters).filter(([, v]) => Boolean(v)).map(([k, v]) => [k === 'sourceId' ? config.sourceKey : k, v!])); return useQuery({ queryKey: ['returns', kind, 'list', filters], enabled, queryFn: ({ signal }) => api.request(`${config.root}/overview${params.size ? `?${params}` : ''}`, { signal, schema: z.array(returnListSchema) }) }) }
export function useReturn(kind: ReturnKind, id: string) { const { api } = useAppServices(); return useQuery({ queryKey: ['returns', kind, 'detail', id], enabled: Boolean(id), refetchOnWindowFocus: false, queryFn: ({ signal }) => api.request(`${returnConfig[kind].root}/${id}`, { signal, schema: returnSchema(kind) }) }) }
export function useReturnable(kind: ReturnKind, sourceId: string, locationId: string, ownId?: string, enabled = true) { const { api } = useAppServices(); const params = new URLSearchParams(); if (kind === 'supplier') { if (locationId) params.set('storageLocationId', locationId); if (ownId) params.set('excludeSupplierReturnId', ownId) } return useQuery({ queryKey: ['returns', kind, 'returnable', sourceId, locationId, ownId ?? ''], enabled: Boolean(sourceId && enabled), queryFn: ({ signal }) => api.request(`${returnConfig[kind].sourceRoot}/${sourceId}/returnable-lines${params.size ? `?${params}` : ''}`, { signal, schema: returnableSchema(kind) }) }) }
export function useReturnPreview(kind: ReturnKind, input: unknown, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['returns', kind, 'preview', input], enabled, staleTime: 0, retry: false, queryFn: ({ signal }) => api.request(`${returnConfig[kind].root}/preview`, { method: 'POST', body: input, signal, schema: previewSchema }) }) }
export function useReturnRefund(kind: ReturnKind, id: string, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['returns', kind, 'refund', id], enabled: Boolean(id && enabled), queryFn: ({ signal }) => api.request(`${returnConfig[kind].root}/${id}/refund`, { signal, schema: previewSchema }) }) }
export function useReturnMutations(kind: ReturnKind) { const { api } = useAppServices(); const client = useQueryClient(); const root = returnConfig[kind].root
  const retiringForm = (id: string) => (query: Query) => query.queryKey[0] === 'returns' && query.queryKey[1] === kind && (query.queryKey[2] === 'preview' || query.queryKey[2] === 'returnable' && query.queryKey[5] === id)
  const changed = async (doc?: ReturnDocument, deletedId?: string) => {
    if (doc) client.setQueryData(['returns', kind, 'detail', doc.document.id], doc)
    const closedId = deletedId ?? (doc?.document.status === 'POSTED' ? doc.document.id : undefined)
    await Promise.all([
      ...[['stock'], ['settlements'], ['counterparties'], ['sales'], ['purchases']].map(queryKey => client.invalidateQueries({ queryKey })),
      client.invalidateQueries({ queryKey: ['returns'], predicate: query => !closedId || !retiringForm(closedId)(query) }),
      ...(closedId ? [client.invalidateQueries({ predicate: retiringForm(closedId), refetchType: 'none' })] : []),
    ])
  }
  const save = useMutation({ mutationFn: ({ id, input }: { id?: string; input: unknown }) => api.request(`${root}${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', body: input, schema: returnSchema(kind) }), onSuccess: doc => changed(doc) })
  const transition = useMutation({ onMutate: async ({ id, action }: { id: string; action: 'post' | 'unpost' }) => { if (action === 'post') await client.cancelQueries({ predicate: retiringForm(id) }) }, mutationFn: ({ id, action }: { id: string; action: 'post' | 'unpost' }) => api.request(`${root}/${id}/${action}`, { method: 'POST', schema: returnSchema(kind) }), onSuccess: doc => changed(doc) })
  const remove = useMutation({ mutationFn: (id: string) => api.request(`${root}/${id}`, { method: 'DELETE' }), onSuccess: async (_, id) => { client.removeQueries({ queryKey: ['returns', kind, 'detail', id] }); await changed(undefined, id) } })
  return { save, transition, remove }
}
