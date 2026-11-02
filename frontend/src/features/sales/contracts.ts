import { z } from 'zod'
import { ApiError } from '../../api/client'
import { purchaseTotals } from '../purchases/totals'

export const uahId = '00000000-0000-0000-0000-000000000980'
const documentSchema = z.object({ id: z.guid(), documentDate: z.iso.date(), status: z.enum(['DRAFT', 'POSTED']), number: z.string().nullable(), comment: z.string().nullable(), internalNote: z.string().nullable(), version: z.number().int().positive(), postedAt: z.string().nullable() })
const productReference = z.object({ productId: z.guid(), name: z.string(), code: z.string(), itemType: z.enum(['GOODS', 'SERVICE']) })
const batchReference = z.object({ batchId: z.guid(), batchNumber: z.string(), purchaseDocumentDate: z.iso.date() })
export const saleSchema = z.object({ document: documentSchema, counterpartyId: z.guid(), storageLocationId: z.guid(), priceTypeId: z.guid().nullable(), discountPercent: z.number(),
  lines: z.array(z.object({ id: z.guid(), lineNo: z.number(), productId: z.guid(), batchId: z.guid().nullable(), quantity: z.number(), recommendedUnitPrice: z.number(), unitPrice: z.number(), removedAt: z.string().nullable() })),
  products: z.array(productReference).nullish().transform(value => value ?? []), batches: z.array(batchReference).nullish().transform(value => value ?? []), postedByName: z.string().nullable().optional(), counterpartyName: z.string().nullable().optional(),
})
export type Sale = z.infer<typeof saleSchema>
export const saleListSchema = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), counterpartyId: z.guid(), counterpartyName: z.string(), storageLocationId: z.guid(), priceTypeId: z.guid().nullable(), priceTypeName: z.string().nullable(), totalUah: z.number(), discountPercent: z.number(), status: z.enum(['DRAFT', 'POSTED']) })
export const availableBatchSchema = z.object({ batchId: z.guid(), productId: z.guid(), storageLocationId: z.guid(), physicalQuantity: z.number(), reservedQuantity: z.number(), availableQuantity: z.number(), ownReservedQuantity: z.number().optional().default(0), availableForDocument: z.number().nullable().optional(), purchaseDocumentDate: z.iso.date(), batchNumber: z.string() })
export type AvailableBatch = z.infer<typeof availableBatchSchema>
export const priceOptionSchema = z.object({ batchId: z.guid().nullable(), unitPrice: z.number() })
export const saleLookupsSchema = z.object({ locations: z.array(z.object({ id: z.guid(), name: z.string().nullable() })) })
export const balanceSchema = z.object({ counterpartyId: z.guid(), currencyId: z.guid(), balance: z.number() })
export const costSchema = z.object({ currentCost: z.number() })
export const profitSchema = z.object({ netSale: z.number(), grossProfit: z.number(), margin: z.number().nullable() })
export interface SaleFilters { search: string; from: string; to: string; counterpartyId: string; storageLocationId: string; priceTypeId: string; status: '' | 'DRAFT' | 'POSTED' }
export const emptyFilters: SaleFilters = { search: '', from: '', to: '', counterpartyId: '', storageLocationId: '', priceTypeId: '', status: '' }
const requiredId = (message: string) => z.string().min(1, message).refine(value => !value || z.guid().safeParse(value).success, message)
const numeric = (positive: boolean) => z.number({ error: 'Введіть число.' }).finite().min(positive ? Number.MIN_VALUE : 0, positive ? 'Кількість має бути більше нуля.' : 'Ціна не може бути від’ємною.').lt(1e16, 'Завелике значення.').refine(value => Math.abs(value * 1e4 - Math.round(value * 1e4)) < 0.000001, 'Не більше 4 знаків після коми.')
export const saleLineSchema = z.object({ lineId: z.guid().nullable(), productId: requiredId('Виберіть номенклатуру.'), itemType: z.enum(['GOODS', 'SERVICE']), batchId: z.string(), quantity: numeric(true), unitPrice: numeric(false).nullable() }).superRefine((line, context) => {
  if (line.itemType === 'GOODS' && !z.guid().safeParse(line.batchId).success) context.addIssue({ code: 'custom', path: ['batchId'], message: 'Для товару виберіть партію.' })
  if (line.itemType === 'SERVICE' && line.batchId) context.addIssue({ code: 'custom', path: ['batchId'], message: 'Послуга не може мати партію.' })
})
export const saleFormSchema = z.object({ documentDate: z.iso.date({ error: 'Вкажіть дату документа.' }), counterpartyId: requiredId('Виберіть покупця.'), storageLocationId: requiredId('Виберіть склад.'), priceTypeId: z.union([z.literal(''), z.guid()]), discountPercent: numeric(false).max(100, 'Знижка не може перевищувати 100%.'), comment: z.string(), internalNote: z.string(), lines: z.array(saleLineSchema) }).superRefine((form, context) => {
  const seen = new Set<string>()
  form.lines.forEach((line, index) => { if (line.batchId && seen.has(line.batchId)) context.addIssue({ code: 'custom', path: ['lines', index, 'batchId'], message: 'Одна партія не може повторюватися в документі.' }); if (line.batchId) seen.add(line.batchId) })
})
export type SaleFormValues = z.infer<typeof saleFormSchema>
export type SaleFormLine = SaleFormValues['lines'][number]
export const capacity = (batch: AvailableBatch) => batch.availableForDocument ?? batch.availableQuantity
export const emptyLine = (): SaleFormLine => ({ lineId: null, productId: '', itemType: 'GOODS', batchId: '', quantity: 1, unitPrice: null })
export function defaults(sale?: Sale): SaleFormValues {
  const now = new Date()
  return sale ? { documentDate: sale.document.documentDate, counterpartyId: sale.counterpartyId, storageLocationId: sale.storageLocationId, priceTypeId: sale.priceTypeId ?? '', discountPercent: sale.discountPercent, comment: sale.document.comment ?? '', internalNote: sale.document.internalNote ?? '', lines: sale.lines.filter(line => !line.removedAt).sort((a, b) => a.lineNo - b.lineNo).map(line => ({ lineId: line.id, productId: line.productId, itemType: sale.products.find(product => product.productId === line.productId)?.itemType ?? (line.batchId ? 'GOODS' : 'SERVICE'), batchId: line.batchId ?? '', quantity: line.quantity, unitPrice: line.unitPrice })) }
    : { documentDate: `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`, counterpartyId: '', storageLocationId: '', priceTypeId: '', discountPercent: 0, comment: '', internalNote: '', lines: [] }
}
export function saleInput(values: SaleFormValues, sale?: Sale) { return { ...(sale ? { version: sale.document.version } : {}), documentDate: values.documentDate, counterpartyId: values.counterpartyId, storageLocationId: values.storageLocationId, priceTypeId: values.priceTypeId || null, discountPercent: values.discountPercent, comment: values.comment || null, internalNote: values.internalNote || null, lines: values.lines.map((line, index) => ({ id: line.lineId, lineNo: index + 1, productId: line.productId, batchId: line.itemType === 'SERVICE' ? null : line.batchId || null, quantity: line.quantity, ...(line.unitPrice === null ? {} : { unitPrice: line.unitPrice }) })) } }
export type SaleInput = ReturnType<typeof saleInput>
export function snapshot(sale: Sale | undefined, priceTypeId: string, line: SaleFormLine) { return sale && (sale.priceTypeId ?? '') === priceTypeId ? sale.lines.find(old => old.id === line.lineId && old.productId === line.productId && (old.batchId ?? '') === line.batchId) : undefined }
export function totals(lines: readonly { quantity: number; unitPrice: number }[], discount: number) { return purchaseTotals(lines.map(line => ({ quantity: line.quantity, unitPriceCurrency: line.unitPrice })), discount, 1) }
export function allocate(batches: readonly AvailableBatch[], quantity: number) {
  let remaining = Math.round(quantity * 1e4)
  const lines: { batch: AvailableBatch; quantity: number }[] = []
  for (const batch of [...batches].sort((a, b) => a.purchaseDocumentDate.localeCompare(b.purchaseDocumentDate))) {
    const amount = Math.min(remaining, Math.max(0, Math.round(capacity(batch) * 1e4)))
    if (amount > 0) lines.push({ batch, quantity: amount / 1e4 })
    remaining -= amount
    if (remaining <= 0) break
  }
  return { lines, shortage: Math.max(0, remaining) / 1e4 }
}
export function saleError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'INSUFFICIENT_STOCK') return 'Недостатньо доступного залишку.'
    if (error.code === 'VERSION_CONFLICT' || error.status === 409 && /stale|concurrent modification/i.test(error.detail ?? '')) return 'Документ був змінений іншим користувачем. Оновіть дані.'
    if (error.code === 'DOCUMENT_HAS_DEPENDENCIES') return 'Продаж не можна розпровести, оскільки на його основі вже створено повернення.'
    if (error.code === 'DUPLICATE_BATCH_LINE' || /Batch cannot occur twice/.test(error.detail ?? '')) return 'Одна партія не може повторюватися в документі.'
    if (error.code === 'DOCUMENT_NOT_DRAFT' || /Only a nondeleted DRAFT|Only a POSTED/.test(error.detail ?? '')) return 'Стан документа змінився. Оновіть дані перед дією.'
    if (error.code === 'PERMISSION_DENIED' || error.code === 'PRICE_BELOW_RECOMMENDED') return 'Недостатньо прав для зниження ціни нижче рекомендованої.'
    if (error.code === 'PRICE_NOT_FOUND') return 'Для вибраної партії або послуги не задано ціну.'
  }
  return error instanceof Error ? error.message : 'Не вдалося виконати дію.'
}
