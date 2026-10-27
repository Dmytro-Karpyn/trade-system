import { z } from 'zod'

export const itemTypeSchema = z.enum(['GOODS', 'SERVICE'])
export const productSchema = z.object({
  id: z.guid(), organizationId: z.guid(), categoryId: z.guid().nullable(), unitId: z.guid(),
  itemType: itemTypeSchema, code: z.string(), name: z.string(), barcode: z.string().nullable(),
  description: z.string().nullable(), attributes: z.unknown().nullable(), active: z.boolean(),
})
export type Product = z.infer<typeof productSchema>
export const unitSchema = z.object({ id: z.guid(), organizationId: z.guid(), name: z.string() })
export type Unit = z.infer<typeof unitSchema>
export interface ProductFilters { search: string; itemType: '' | Product['itemType']; active: 'true' | 'false' | '' }
export const productFormSchema = z.object({
  itemType: itemTypeSchema,
  code: z.string().trim().min(1, 'Введіть код.'),
  name: z.string().trim().min(1, 'Введіть назву.'),
  unitId: z.string().min(1, 'Виберіть одиницю виміру.').refine(value => value !== '' ? z.guid().safeParse(value).success : true, 'Виберіть одиницю виміру.'),
  barcode: z.string(), description: z.string(), active: z.boolean(),
})
export type ProductFormValues = z.infer<typeof productFormSchema>
export interface ProductInput {
  itemType: Product['itemType']; code: string; name: string; unitId: string; categoryId: string | null
  barcode: string | null; description: string | null; active: boolean; attributes: unknown
}
export const productDefaults: ProductFormValues = { itemType: 'GOODS', code: '', name: '', unitId: '', barcode: '', description: '', active: true }
export function toProductInput(values: ProductFormValues, existing?: Product): ProductInput {
  return { ...values, barcode: values.barcode.trim() || null, description: values.description || null,
    categoryId: existing?.categoryId ?? null, attributes: existing?.attributes ?? null }
}
export function unitName(id: string, units: readonly Unit[]) {
  return units.find(unit => unit.id === id)?.name ?? 'Назву не вказано'
}
