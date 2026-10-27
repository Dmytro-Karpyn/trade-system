import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { productSchema, unitSchema } from './contracts'
import type { ProductFilters, ProductInput } from './contracts'

export const productKeys = { all: ['products'] as const, list: (filters: ProductFilters) => ['products', 'list', filters] as const, detail: (id: string) => ['products', 'detail', id] as const }
export const unitsKey = ['units'] as const
export function useProducts(filters: ProductFilters) {
  const { api } = useAppServices()
  const params = new URLSearchParams()
  if (filters.search.trim()) params.set('search', filters.search.trim())
  if (filters.itemType) params.set('itemType', filters.itemType)
  if (filters.active !== '') params.set('active', filters.active)
  return useQuery({ queryKey: productKeys.list(filters), queryFn: ({ signal }) => api.request(`/products${params.size ? `?${params}` : ''}`, { signal, schema: z.array(productSchema) }) })
}
export function useProduct(id: string) {
  const { api } = useAppServices()
  return useQuery({ queryKey: productKeys.detail(id), queryFn: ({ signal }) => api.request(`/products/${encodeURIComponent(id)}`, { signal, schema: productSchema }) })
}
export function useUnits() {
  const { api } = useAppServices()
  return useQuery({ queryKey: unitsKey, queryFn: ({ signal }) => api.request('/units', { signal, schema: z.array(unitSchema) }) })
}
export function useSaveProduct(id?: string) {
  const { api } = useAppServices()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: ProductInput) => api.request(id ? `/products/${encodeURIComponent(id)}` : '/products', { method: id ? 'PUT' : 'POST', body: input, schema: productSchema }),
    onSuccess: async product => {
      queryClient.setQueryData(productKeys.detail(product.id), product)
      await queryClient.invalidateQueries({ queryKey: productKeys.all })
    },
  })
}
export function useCreateUnit() {
  const { api } = useAppServices()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => api.request('/units', { method: 'POST', body: { name }, schema: unitSchema }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: unitsKey }) },
  })
}
