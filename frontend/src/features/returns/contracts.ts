import { z } from 'zod'
import { ApiError } from '../../api/client'
import { localDate, locationSchema } from '../purchases/contracts'

export type ReturnKind = 'customer' | 'supplier'
export const returnConfig = {
  customer: { root: '/customer-returns', sourceRoot: '/sales', sourceKey: 'sourceSaleInvoiceId', sourceLineKey: 'sourceSaleLineId', permission: 'sale', title: 'Повернення покупців', singular: 'Повернення покупця', source: 'Продаж', party: 'Покупець', create: 'Створити повернення', confirmation: 'Після проведення товар буде повернуто на склад, а взаєморозрахунок з покупцем буде скориговано.' },
  supplier: { root: '/supplier-returns', sourceRoot: '/purchases', sourceKey: 'sourcePurchaseInvoiceId', sourceLineKey: 'sourcePurchaseLineId', permission: 'purchase', title: 'Повернення постачальникам', singular: 'Повернення постачальнику', source: 'Закупівля', party: 'Постачальник', create: 'Повернути постачальнику', confirmation: 'Після проведення товар буде списано зі складу, а заборгованість перед постачальником буде скоригована.' },
} as const
const documentSchema = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), status: z.enum(['DRAFT', 'POSTED']), comment: z.string().nullable(), internalNote: z.string().nullable(), version: z.number().int().positive() })
const customerSchema = z.object({ document: documentSchema, sourceSaleInvoiceId: z.guid(), storageLocationId: z.guid(), lines: z.array(z.object({ id: z.guid(), sourceSaleLineId: z.guid(), quantity: z.number(), refundAmount: z.number() })) })
const supplierSchema = z.object({ document: documentSchema, sourcePurchaseInvoiceId: z.guid(), storageLocationId: z.guid(), lines: z.array(z.object({ id: z.guid(), sourcePurchaseLineId: z.guid(), quantity: z.number() })) })
export interface ReturnDocument { document: z.infer<typeof documentSchema>; sourceId: string; storageLocationId: string; lines: { id: string; sourceLineId: string; quantity: number; refundAmount?: number }[] }
export const returnSchema = (kind: ReturnKind): z.ZodType<ReturnDocument> => kind === 'customer'
  ? customerSchema.transform(x => ({ ...x, sourceId: x.sourceSaleInvoiceId, lines: x.lines.map(l => ({ ...l, sourceLineId: l.sourceSaleLineId })) }))
  : supplierSchema.transform(x => ({ ...x, sourceId: x.sourcePurchaseInvoiceId, lines: x.lines.map(l => ({ ...l, sourceLineId: l.sourcePurchaseLineId })) }))
export const returnListSchema = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), counterpartyId: z.guid(), counterpartyName: z.string(), sourceId: z.guid(), sourceNumber: z.string().nullable(), currencyId: z.guid(), totalRefund: z.number(), status: z.enum(['DRAFT', 'POSTED']) })
export type ReturnListItem = z.infer<typeof returnListSchema>
export const returnLookupsSchema = z.object({ sources: z.array(z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), counterpartyId: z.guid(), counterpartyName: z.string(), storageLocationId: z.guid(), currencyId: z.guid(), exchangeRate: z.number(), discountPercent: z.number() })), locations: z.array(locationSchema), products: z.array(z.object({ productId: z.guid(), name: z.string() })), batches: z.array(z.object({ batchId: z.guid(), batchNumber: z.string() })) })
export type ReturnLookups = z.infer<typeof returnLookupsSchema>
export interface ReturnableLine { sourceLineId: string; productId: string; itemType: 'GOODS' | 'SERVICE'; batchId: string | null; originalQuantity: number; alreadyReturnedQuantity: number; returnableQuantity: number; originalUnitPrice: number; originalDiscountPercent: number; refundableAmount?: number; physicalQuantity?: number; reservedQuantity?: number; availableQuantity?: number; availableForDocument?: number; maxReturnableQuantity?: number }
export const returnableSchema = (kind: ReturnKind): z.ZodType<ReturnableLine[]> => kind === 'customer'
  ? z.array(z.object({ sourceSaleLineId: z.guid(), productId: z.guid(), itemType: z.enum(['GOODS', 'SERVICE']), batchId: z.guid().nullable(), soldQuantity: z.number(), alreadyReturnedQuantity: z.number(), returnableQuantity: z.number(), originalUnitPrice: z.number(), originalDiscountPercent: z.number(), refundableAmount: z.number() }).transform(x => ({ ...x, sourceLineId: x.sourceSaleLineId, originalQuantity: x.soldQuantity })))
  : z.array(z.object({ sourcePurchaseLineId: z.guid(), productId: z.guid(), batchId: z.guid(), purchasedQuantity: z.number(), alreadyReturnedQuantity: z.number(), remainingReturnableByPurchase: z.number(), physicalQuantity: z.number(), reservedQuantity: z.number(), availableQuantity: z.number(), availableForDocument: z.number(), maxReturnableQuantity: z.number(), originalUnitPriceCurrency: z.number(), originalDiscountPercent: z.number() }).transform(x => ({ ...x, itemType: 'GOODS' as const, sourceLineId: x.sourcePurchaseLineId, originalQuantity: x.purchasedQuantity, returnableQuantity: x.remainingReturnableByPurchase, originalUnitPrice: x.originalUnitPriceCurrency })))
export const previewSchema = z.object({ totalRefund: z.number(), lines: z.array(z.object({ sourceLineId: z.guid(), refundAmount: z.number() })) })
export type ReturnPreview = z.infer<typeof previewSchema>
export interface ReturnFilters { from: string; to: string; counterpartyId: string; status: string; sourceId: string }
export const returnFilters: ReturnFilters = { from: '', to: '', counterpartyId: '', status: '', sourceId: '' }
const requiredId = (message: string) => z.string().min(1, message).refine(x => !x || z.guid().safeParse(x).success, message)
const numeric = z.number({ error: 'Введіть число.' }).finite().nullable()
export const returnFormSchema = z.object({ documentDate: z.iso.date({ error: 'Вкажіть дату документа.' }), sourceId: requiredId('Виберіть проведений документ-підставу.'), storageLocationId: requiredId('Виберіть склад.'), comment: z.string(), internalNote: z.string(), lines: z.array(z.object({ id: z.guid().nullable(), sourceLineId: z.guid(), selected: z.boolean(), itemType: z.enum(['GOODS', 'SERVICE']), quantity: numeric, refundAmount: numeric })) }).superRefine((v, ctx) => {
  if (!v.lines.some(l => l.selected)) ctx.addIssue({ code: 'custom', path: ['lines'], message: 'Виберіть хоча б один рядок повернення.' })
  v.lines.forEach((line, i) => {
    if (!line.selected) return
    for (const field of line.itemType === 'SERVICE' ? ['quantity', 'refundAmount'] as const : ['quantity'] as const) {
      const value = line[field]; const path = ['lines', i, field]
      const zeroQuantityAllowed = field === 'quantity' && line.itemType === 'SERVICE'
      if (value === null || (zeroQuantityAllowed ? value < 0 : value <= 0)) ctx.addIssue({ code: 'custom', path, message: field === 'quantity' ? zeroQuantityAllowed ? 'Кількість не може бути від’ємною.' : 'Кількість має бути більше нуля.' : 'Вкажіть додатну суму компенсації.' })
      else if (value >= 1e16) ctx.addIssue({ code: 'custom', path, message: 'Завелике значення.' })
      else if (Math.abs(value * 10000 - Math.round(value * 10000)) >= 0.000001) ctx.addIssue({ code: 'custom', path, message: 'Не більше 4 знаків після коми.' })
    }
  })
})
export type ReturnValues = z.infer<typeof returnFormSchema>
export function returnDefaults(doc?: ReturnDocument, sourceId = ''): ReturnValues { return { documentDate: doc?.document.documentDate ?? localDate(), sourceId: doc?.sourceId ?? sourceId, storageLocationId: doc?.storageLocationId ?? '', comment: doc?.document.comment ?? '', internalNote: doc?.document.internalNote ?? '', lines: [] } }
export function formLines(rows: ReturnableLine[], doc?: ReturnDocument): ReturnValues['lines'] { return rows.map(row => { const saved = doc?.lines.find(l => l.sourceLineId === row.sourceLineId); return { id: saved?.id ?? null, sourceLineId: row.sourceLineId, selected: Boolean(saved), itemType: row.itemType, quantity: saved?.quantity ?? Math.max(0, row.maxReturnableQuantity ?? row.returnableQuantity), refundAmount: saved?.refundAmount ?? row.refundableAmount ?? null } }) }
export function returnInput(kind: ReturnKind, values: ReturnValues, baseline?: ReturnDocument, preview = false) { const config = returnConfig[kind]; return { ...(baseline && !preview ? { version: baseline.document.version } : {}), documentDate: values.documentDate, [config.sourceKey]: values.sourceId, storageLocationId: values.storageLocationId, comment: values.comment || null, internalNote: values.internalNote || null, lines: values.lines.filter(l => l.selected).map(l => ({ id: preview ? null : l.id, [config.sourceLineKey]: l.sourceLineId, quantity: l.quantity, ...(kind === 'customer' && l.itemType === 'SERVICE' ? { refundAmount: l.refundAmount } : {}) })) } }
export function returnError(error: unknown): string {
  if (!(error instanceof ApiError)) return error instanceof Error ? error.message : 'Не вдалося виконати операцію.'
  if (error.code === 'RETURN_QUANTITY_EXCEEDED') return 'Кількість перевищує залишок, доступний для повернення. Оновіть рядки документа.'
  if (error.code === 'RETURN_AMOUNT_EXCEEDED') return 'Сума компенсації перевищує залишок суми, доступної для повернення.'
  if (error.code === 'INSUFFICIENT_STOCK' || error.code === 'HISTORICAL_NEGATIVE_STOCK') return 'Недостатньо доступного товару на складі. Перевірте кількість та резерви у рядках.'
  if (error.code === 'INVALID_SOURCE_DOCUMENT' || /requires a nondeleted POSTED|Source (sale|purchase) not found|excluded reserve must belong/.test(error.detail ?? '')) return 'Документ-підстава недоступний. Виберіть проведений документ своєї організації.'
  if (error.code === 'VERSION_CONFLICT' || error.status === 409 && /Version|Concurrent modification/i.test(error.detail ?? '')) return 'Документ змінено іншим користувачем. Оновіть дані перед збереженням.'
  return error.message
}
