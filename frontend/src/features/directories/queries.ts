import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { categorySchema, currencyReferenceSchema, directoryInput, directoryRowSchema } from './contracts'
import type { DirectoryKind, DirectoryValues } from './contracts'
export function useDirectory(kind: DirectoryKind | 'expense-categories', enabled = true) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['directories', kind],
    enabled: enabled && kind !== 'currencies',
    queryFn: ({ signal }) => api.request('/' + kind, { signal, schema: z.array(directoryRowSchema) }),
  })
}
export function useCategories(enabled = true) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['directories', 'categories'],
    enabled,
    queryFn: ({ signal }) => api.request('/categories', { signal, schema: z.array(categorySchema) }),
  })
}
export function useCurrencies(enabled = true) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['directories', 'currencies'],
    enabled,
    queryFn: ({ signal }) => api.request('/currencies', { signal, schema: z.array(currencyReferenceSchema) }),
  })
}
export function useDirectorySave(kind: DirectoryKind) {
  const { api } = useAppServices()
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, values }: { id?: string; values: DirectoryValues }) =>
      api.request(`/${kind}${id ? '/' + id : ''}`, {
        method: id ? 'PUT' : 'POST',
        body: directoryInput(kind, values),
        schema: directoryRowSchema,
      }),
    onSuccess: async () => {
      await Promise.all(
        [
          ['directories'],
          ['products'],
          ['units'],
          ['stock'],
          ['price-types'],
          ['pricing'],
          ['purchase-lookups'],
          ['purchases'],
          ['sale-lookups'],
          ['sales'],
          ['expenses'],
          ['returns'],
        ].map((queryKey) => client.invalidateQueries({ queryKey })),
      )
    },
  })
}
export function useDefaultPrices() {
  const { api } = useAppServices()
  const client = useQueryClient()
  return useMutation({
    mutationFn: () =>
      api.request('/price-types/defaults', { method: 'POST', schema: z.array(directoryRowSchema) }),
    onSuccess: async () => {
      await Promise.all(
        [['directories', 'price-types'], ['price-types'], ['stock'], ['sales'], ['purchases']].map(
          (queryKey) => client.invalidateQueries({ queryKey }),
        ),
      )
    },
  })
}
