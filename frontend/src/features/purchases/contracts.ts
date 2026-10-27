import { z } from 'zod'
import { ApiError } from '../../api/client'

export const uahId = '00000000-0000-0000-0000-000000000980'
const documentSchema = z.object({ id: z.guid(), organizationId: z.guid(), documentType: z.enum(['PURCHASE_INVOICE', 'OPENING_STOCK']), number: z.string().nullable(), documentDate: z.iso.date(), status: z.enum(['DRAFT', 'POSTED']), comment: z.string().nullable(), internalNote: z.string().nullable(), createdBy: z.guid(), createdAt: z.string(), updatedAt: z.string(), postedBy: z.guid().nullable(), postedAt: z.string().nullable(), deletedBy: z.guid().nullable(), deletedAt: z.string().nullable(), version: z.number().int().positive() })
const lineSchema = z.object({ id: z.guid(), purchaseInvoiceId: z.guid(), lineNo: z.number(), productId: z.guid(), quantity: z.number(), unitPriceCurrency: z.number(), removedAt: z.string().nullable() })
export const purchaseProductSchema = z.object({ productId: z.guid(), name: z.string(), code: z.string() })
export const purchaseSchema = z.object({ document: documentSchema, counterpartyId: z.guid().nullable(), storageLocationId: z.guid(), currencyId: z.guid(), exchangeRate: z.number(), externalNumber: z.string().nullable(), externalDate: z.iso.date().nullable(), discountPercent: z.number(), lines: z.array(lineSchema), products: z.array(purchaseProductSchema).nullish().transform(value => value ?? []), postedByName: z.string().nullable().optional() })
export type Purchase = z.infer<typeof purchaseSchema>
export const purchaseListSchema = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), counterpartyId: z.guid().nullable(), counterpartyName: z.string().nullable(), storageLocationId: z.guid(), currencyId: z.guid(), totalCurrency: z.number(), status: z.enum(['DRAFT', 'POSTED']), externalNumber: z.string().nullable() })
export const locationSchema = z.object({ id: z.guid(), name: z.string().nullable() })
export type Location = z.infer<typeof locationSchema>
export const lookupsSchema = z.object({ locations: z.array(locationSchema), currencyIds: z.array(z.guid()) })
export type PurchaseLookups = z.infer<typeof lookupsSchema>
export const batchSchema = z.object({ batchId: z.guid(), purchaseInvoiceLineId: z.guid(), productId: z.guid(), productName: z.string(), productCode: z.string(), batchNumber: z.string(), createdAt: z.string() })
export type PurchaseBatch = z.infer<typeof batchSchema>
export const costSchema = z.object({ batchId: z.guid(), unitCostUah: z.number() })
export const batchPriceSchema = z.object({ batchId: z.guid(), priceTypeId: z.guid(), priceTypeName: z.string(), unitPrice: z.number() })
export interface PurchaseFilters { search: string; from: string; to: string; counterpartyId: string; storageLocationId: string; currencyId: string; status: '' | 'DRAFT' | 'POSTED' }
export const emptyFilters: PurchaseFilters = { search: '', from: '', to: '', counterpartyId: '', storageLocationId: '', currencyId: '', status: '' }
const requiredId = (message: string) => z.string().min(1, message).refine(value => !value || z.guid().safeParse(value).success, message)
const decimal = (places: number, positive: boolean) => z.number({ error: 'Введіть число.' }).finite().min(positive ? Number.MIN_VALUE : 0, positive ? 'Значення має бути більше нуля.' : 'Значення не може бути від’ємним.').lt(1e16, 'Завелике значення.').refine(value => Math.abs(value * 10 ** places - Math.round(value * 10 ** places)) < 0.000001, `Не більше ${places} знаків після коми.`)
export const purchaseFormSchema = z.object({
  documentDate: z.iso.date({ error: 'Вкажіть дату документа.' }), counterpartyId: requiredId('Виберіть постачальника.'), storageLocationId: requiredId('Виберіть склад.'), currencyId: requiredId('Виберіть валюту.'),
  exchangeRate: decimal(8, true).lt(1e12, 'Завеликий курс.'), discountPercent: decimal(4, false).max(100, 'Знижка не може перевищувати 100%.'),
  externalNumber: z.string(), externalDate: z.union([z.literal(''), z.iso.date({ error: 'Вкажіть коректну дату.' })]), comment: z.string(), internalNote: z.string(),
  lines: z.array(z.object({ lineId: z.guid().nullable(), productId: requiredId('Виберіть товар.'), quantity: decimal(4, true), unitPriceCurrency: decimal(4, false) })),
}).refine(values => values.currencyId !== uahId || values.exchangeRate === 1, { path: ['exchangeRate'], message: 'Для UAH курс дорівнює 1.' })
export type PurchaseFormValues = z.infer<typeof purchaseFormSchema>
export function localDate() { const now = new Date(); return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}` }
export function formDefaults(purchase?: Purchase): PurchaseFormValues {
  return purchase ? { documentDate: purchase.document.documentDate, counterpartyId: purchase.counterpartyId ?? '', storageLocationId: purchase.storageLocationId, currencyId: purchase.currencyId, exchangeRate: purchase.exchangeRate, discountPercent: purchase.discountPercent, externalNumber: purchase.externalNumber ?? '', externalDate: purchase.externalDate ?? '', comment: purchase.document.comment ?? '', internalNote: purchase.document.internalNote ?? '', lines: purchase.lines.filter(line => !line.removedAt).sort((a, b) => a.lineNo - b.lineNo).map(line => ({ lineId: line.id, productId: line.productId, quantity: line.quantity, unitPriceCurrency: line.unitPriceCurrency })) }
    : { documentDate: localDate(), counterpartyId: '', storageLocationId: '', currencyId: uahId, exchangeRate: 1, discountPercent: 0, externalNumber: '', externalDate: '', comment: '', internalNote: '', lines: [] }
}
export function purchaseInput(values: PurchaseFormValues, purchase?: Purchase) {
  return { documentType: 'PURCHASE_INVOICE', ...(purchase ? { version: purchase.document.version } : {}), documentDate: values.documentDate, counterpartyId: values.counterpartyId, storageLocationId: values.storageLocationId, currencyId: values.currencyId, exchangeRate: values.currencyId === uahId ? 1 : values.exchangeRate, discountPercent: values.discountPercent, externalNumber: values.externalNumber.trim() || null, externalDate: values.externalDate || null, comment: values.comment || null, internalNote: values.internalNote || null,
    lines: values.lines.map((line, index) => ({ id: line.lineId, lineNo: index + 1, productId: line.productId, quantity: line.quantity, unitPriceCurrency: line.unitPriceCurrency })) }
}
export type PurchaseInput = ReturnType<typeof purchaseInput>
export const money = (value: number) => new Intl.NumberFormat('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(value)
export const dateLabel = (value: string) => value.split('-').reverse().join('.')
export function locationName(id: string, locations: readonly Location[]) { const item = locations.find(location => location.id === id); return item?.name ?? `Склад без назви${item ? ` ${locations.filter(location => !location.name).findIndex(location => location.id === id) + 1}` : ''}` }
export function currencyName(id: string, ids: readonly string[]) { return id === uahId ? 'UAH' : `Валюта ${[...new Set([...ids, id])].filter(value => value !== uahId).sort().indexOf(id) + 1}` }
export function purchaseError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'VERSION_CONFLICT' || error.status === 409 && /stale|concurrent modification/i.test(error.detail ?? '')) return 'Документ був змінений іншим користувачем. Оновіть дані.'
    if (error.code === 'DOCUMENT_HAS_DEPENDENCIES') return 'Документ не можна розпровести, оскільки від нього залежать інші документи.'
    if (error.code === 'DOCUMENT_NOT_DRAFT' || /Only a nondeleted DRAFT|Only a POSTED/.test(error.detail ?? '')) return 'Стан документа змінився. Оновіть дані перед дією.'
    if (error.code === 'INVALID_SOURCE_DOCUMENT') return 'Вихідний документ недоступний або має неприпустимий стан.'
  }
  return error instanceof Error ? error.message : 'Не вдалося виконати дію.'
}
