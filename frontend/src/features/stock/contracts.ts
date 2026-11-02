import { z } from 'zod'
import { ApiError } from '../../api/client'
import { money } from '../purchases/contracts'

const range = z.object({ min: z.number(), max: z.number() }).nullable()
export const stockRowSchema = z.object({ productId: z.guid(), productName: z.string(), code: z.string(), unit: z.string().nullable(), category: z.string().nullable(), storageLocationId: z.guid(), locationName: z.string().nullable(), batchId: z.guid().nullable(), batchNumber: z.string().nullable(), purchaseDocumentDate: z.iso.date().nullable(), supplier: z.string().nullable(), physicalQuantity: z.number(), reservedQuantity: z.number(), availableQuantity: z.number(), retailPrice: range, wholesalePrice: range, currentUnitCost: range, categoryId: z.guid().nullable().optional().default(null) })
export type StockRow = z.infer<typeof stockRowSchema>
export const stockRowId = (row: StockRow) => `${row.productId}-${row.storageLocationId}-${row.batchId ?? ''}`
const product = z.object({ id: z.guid(), name: z.string(), code: z.string(), unit: z.string().nullable(), category: z.string().nullable(), active: z.boolean(), categoryId: z.guid().nullable().optional().default(null) })
const location = z.object({ id: z.guid(), name: z.string().nullable() })
export const stockLookupsSchema = z.object({ locations: z.array(location), operationLocations: z.array(location), priceTypes: z.array(z.object({ id: z.guid(), name: z.string(), active: z.boolean() })), categories: z.array(z.object({ id: z.guid(), name: z.string().nullable() })).optional().default([]) })
export const stockProductSchema = z.object({ product, balances: z.array(stockRowSchema), batches: z.array(stockRowSchema) })
export const stockBatchSchema = z.object({ batchId: z.guid(), batchNumber: z.string(), product, purchaseDocumentId: z.guid(), purchaseNumber: z.string().nullable(), purchaseDocumentType: z.string(), purchaseDocumentDate: z.iso.date(), supplier: z.string().nullable(), currencyId: z.guid(), valuation: z.object({ unitPriceCurrency: z.number(), exchangeRate: z.number(), discountPercent: z.number(), unitCostUah: z.number() }).nullable(), prices: z.array(z.object({ priceTypeId: z.guid(), priceTypeName: z.string(), unitPrice: z.number() })), balances: z.array(stockRowSchema) })
export const movementSchema = z.object({ id: z.guid(), documentId: z.guid(), documentNumber: z.string().nullable(), documentType: z.string(), documentDate: z.iso.date(), storageLocationId: z.guid(), locationName: z.string().nullable(), quantity: z.number(), physicalBalanceAfter: z.number() })
export const reservationSchema = z.object({ documentId: z.guid(), documentNumber: z.string().nullable(), documentType: z.string(), documentDate: z.iso.date(), batchId: z.guid(), storageLocationId: z.guid(), quantity: z.number(), createdByName: z.string().nullable() })
export const candidateSchema = z.object({ stock: stockRowSchema, ownReservedQuantity: z.number(), availableForDocument: z.number() })
export type StockCandidate = z.infer<typeof candidateSchema>
export interface StockFilters { search: string; categoryId: string; storageLocationId: string; withStock: boolean; withReservations: boolean; byBatch: boolean; retailPriceTypeId: string; wholesalePriceTypeId: string }
export const stockFilters: StockFilters = { search: '', categoryId: '', storageLocationId: '', withStock: false, withReservations: false, byBatch: false, retailPriceTypeId: '', wholesalePriceTypeId: '' }
export type OperationKind = 'transfers' | 'write-offs'
export const operationApi = (kind: OperationKind) => kind === 'transfers' ? '/stock-transfers' : '/write-offs'
export const operationPermission = (kind: OperationKind) => kind === 'transfers' ? 'stock.transfer' : 'stock.writeoff'
export const operationTitle = (kind: OperationKind) => kind === 'transfers' ? 'Переміщення' : 'Списання'
const document = z.object({ id: z.guid(), documentDate: z.iso.date(), status: z.enum(['DRAFT', 'POSTED']), number: z.string().nullable(), version: z.number(), comment: z.string().nullable(), internalNote: z.string().nullable() })
export const operationSchema = z.object({ document, fromLocationId: z.guid().optional(), toLocationId: z.guid().optional(), storageLocationId: z.guid().optional(), writeOffReasonId: z.guid().optional(), lines: z.array(z.object({ id: z.guid(), lineNo: z.number(), batchId: z.guid(), quantity: z.number() })) })
export type StockOperation = z.infer<typeof operationSchema>
export const reasonSchema = z.object({ id: z.guid(), name: z.string(), active: z.boolean() })
const required = (message: string) => z.string().min(1, message)
export const operationFormSchema = z.object({ documentDate: z.iso.date({ error: 'Вкажіть дату.' }), source: required('Виберіть склад.'), destination: z.string(), reason: z.string(), comment: z.string(), internalNote: z.string(), lines: z.array(z.object({ lineId: z.guid().nullable(), batchId: required('Виберіть партію.'), quantity: z.number({ error: 'Введіть кількість.' }).finite().positive('Кількість має бути більше нуля.').lt(1e16).refine(x => Math.abs(x * 1e4 - Math.round(x * 1e4)) < 0.000001, 'Не більше 4 знаків після коми.') })) }).superRefine((values, ctx) => {
  const ids = new Set<string>()
  values.lines.forEach((line, index) => { if (line.batchId && ids.has(line.batchId)) ctx.addIssue({ code: 'custom', path: ['lines', index, 'batchId'], message: 'Одна партія не може повторюватися.' }); ids.add(line.batchId) })
})
export type OperationValues = z.infer<typeof operationFormSchema>
export function operationDefaults(doc?: StockOperation): OperationValues {
  const date = new Date(); const today = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
  return { documentDate: doc?.document.documentDate ?? today, source: doc?.fromLocationId ?? doc?.storageLocationId ?? '', destination: doc?.toLocationId ?? '', reason: doc?.writeOffReasonId ?? '', comment: doc?.document.comment ?? '', internalNote: doc?.document.internalNote ?? '', lines: doc?.lines.map(l => ({ lineId: l.id, batchId: l.batchId, quantity: l.quantity })) ?? [] }
}
export function operationInput(kind: OperationKind, values: OperationValues, baseline?: StockOperation) {
  return { ...(baseline ? { version: baseline.document.version } : {}), documentDate: values.documentDate, comment: values.comment || null, internalNote: values.internalNote || null,
    ...(kind === 'transfers' ? { fromLocationId: values.source, toLocationId: values.destination } : { storageLocationId: values.source, writeOffReasonId: values.reason }),
    lines: values.lines.map((l, index) => ({ id: l.lineId, lineNo: index + 1, batchId: l.batchId, quantity: l.quantity })) }
}
export const priceRange = (value: StockRow['retailPrice']) => value === null ? '—' : value.min === value.max ? money(value.min) : `${money(value.min)}–${money(value.max)}`
export const documentTypes: Record<string, string> = { PURCHASE_INVOICE: 'Закупівля', OPENING_STOCK: 'Початкові залишки', SALE_INVOICE: 'Продаж', CUSTOMER_RETURN: 'Повернення покупця', SUPPLIER_RETURN: 'Повернення постачальнику', STOCK_TRANSFER: 'Переміщення', WRITE_OFF: 'Списання' }
export function documentRoute(type: string, id: string) { return type === 'PURCHASE_INVOICE' || type === 'OPENING_STOCK' ? `/purchases/${id}` : type === 'SALE_INVOICE' ? `/sales/${id}` : type === 'STOCK_TRANSFER' ? `/stock/transfers/${id}` : type === 'WRITE_OFF' ? `/stock/write-offs/${id}` : null }
export const documentPermission = (type: string) => type === 'STOCK_TRANSFER' ? 'stock.transfer' : type === 'WRITE_OFF' ? 'stock.writeoff' : type === 'SALE_INVOICE' ? 'sale.view' : 'purchase.view'
export function stockError(error: unknown) {
  if (error instanceof ApiError) {
    const messages: Record<string, string> = { INSUFFICIENT_STOCK: 'Недостатньо доступного залишку.', HISTORICAL_NEGATIVE_STOCK: 'Операція призведе до від’ємного складського залишку.', VERSION_CONFLICT: 'Документ був змінений іншим користувачем. Оновіть дані.', DOCUMENT_HAS_DEPENDENCIES: 'Документ має залежні операції. Спочатку скасуйте їх.', DUPLICATE_BATCH_LINE: 'Одна партія не може повторюватися.', DOCUMENT_NOT_DRAFT: 'Стан документа змінився. Оновіть дані.', PERMISSION_DENIED: 'Недостатньо прав для цієї дії.' }
    return error.code && messages[error.code] || error.message
  }
  return error instanceof Error ? error.message : 'Не вдалося виконати операцію.'
}
