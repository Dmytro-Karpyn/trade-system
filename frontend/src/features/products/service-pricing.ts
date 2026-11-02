import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
export const servicePricesKey = (id: string) => ['service-prices', id] as const
const priceSchema = z.object({ productId: z.guid(), priceTypeId: z.guid(), unitPrice: z.number() })
export const priceTextSchema = z
  .string()
  .trim()
  .refine(
    (value) => value !== '' && Number.isFinite(Number(value)) && Number(value) >= 0,
    'Ціна має бути невід’ємним числом.',
  )
  .refine(
    (value) =>
      Number(value) < 1e16 && Math.abs(Number(value) * 10000 - Math.round(Number(value) * 10000)) < 0.000001,
    'До 4 знаків після коми; ціна має бути менше 10¹⁶.',
  )
export function useServicePrices(id: string) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: servicePricesKey(id),
    enabled: Boolean(id),
    queryFn: ({ signal }) => api.request(`/products/${id}/prices`, { signal, schema: z.array(priceSchema) }),
  })
}
export function useSaveServicePrice() {
  const { api } = useAppServices()
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({
      productId,
      priceTypeId,
      price,
    }: {
      productId: string
      priceTypeId: string
      price: number
    }) =>
      api.request(`/products/${productId}/prices/${priceTypeId}`, {
        method: 'PUT',
        body: { unitPrice: price },
        schema: priceSchema,
      }),
    onSuccess: async (result) => {
      await Promise.all(
        [servicePricesKey(result.productId), ['sales', 'price-options', result.productId], ['admin']].map(
          (queryKey) => client.invalidateQueries({ queryKey }),
        ),
      )
    },
  })
}
