import { z } from 'zod'
import { ApiError } from '../../api/client'
import { purchaseTotals } from '../purchases/totals'
import { uahId } from '../counterparties/contracts'
export { uahId }

const document = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), status: z.enum(['DRAFT', 'POSTED']), version: z.number().int().positive(), comment: z.string().nullable(), internalNote: z.string().nullable() })
export const expenseSchema = z.object({ document, counterpartyId: z.guid(), expenseCategoryId: z.guid(), currencyId: z.guid(), amount: z.number(), exchangeRate: z.number(), purchaseInvoiceId: z.guid().nullable() })
export type Expense = z.infer<typeof expenseSchema>
export const categorySchema = z.object({ id: z.guid(), organizationId: z.guid(), parentId: z.guid().nullable(), name: z.string(), active: z.boolean() })
export type ExpenseCategory = z.infer<typeof categorySchema>
export const purchaseOptionSchema = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), supplier: z.string().nullable(), currencyId: z.guid(), totalCurrency: z.number(), canAllocate: z.boolean() })
export type ExpensePurchase = z.infer<typeof purchaseOptionSchema>
export const expenseLookupsSchema = z.object({ counterparties: z.array(z.object({ id: z.guid(), name: z.string(), active: z.boolean() })), currencyIds: z.array(z.guid()), categories: z.array(categorySchema), purchases: z.array(purchaseOptionSchema) })
export type ExpenseLookups = z.infer<typeof expenseLookupsSchema>
export const costImpactSchema = z.object({ purchaseInvoiceId: z.guid(), purchaseNumber: z.string().nullable(), purchaseDate: z.iso.date(), currencyId: z.guid(), purchaseTotalCurrency: z.number(), purchaseBaseValueUah: z.number(), postedDirectExpensesUah: z.number(), batches: z.array(z.object({ batchId: z.guid(), batchNumber: z.string(), productName: z.string(), quantity: z.number(), currentUnitCostUah: z.number() })) })
const requiredId = (message: string) => z.string().min(1, message).refine(id => !id || z.guid().safeParse(id).success, message)
const numeric = (scale: number, limit: number) => z.number({ error: 'Введіть число.' }).finite().positive('Значення має бути більше нуля.').lt(limit, 'Завелике значення.').refine(x => Math.abs(x * 10 ** scale - Math.round(x * 10 ** scale)) < 0.000001, `Не більше ${scale} знаків після коми.`).nullable()
export const expenseFormSchema = z.object({ documentDate: z.iso.date({ error: 'Вкажіть дату документа.' }), counterpartyId: requiredId('Виберіть контрагента.'), expenseCategoryId: requiredId('Виберіть категорію.'), currencyId: requiredId('Виберіть валюту.'), amount: numeric(4, 1e16), exchangeRate: numeric(6, 1e14), purchaseInvoiceId: z.union([z.literal(''), z.guid()]), comment: z.string(), internalNote: z.string() }).superRefine((v, ctx) => {
  if (v.amount === null) ctx.addIssue({ code: 'custom', path: ['amount'], message: 'Введіть суму.' })
  if (v.exchangeRate === null) ctx.addIssue({ code: 'custom', path: ['exchangeRate'], message: 'Введіть курс.' })
  if (v.currencyId === uahId && v.exchangeRate !== 1) ctx.addIssue({ code: 'custom', path: ['exchangeRate'], message: 'Для UAH курс дорівнює 1.' })
})
export type ExpenseValues = z.infer<typeof expenseFormSchema>
export const categoryFormSchema = z.object({ name: z.string().trim().min(1, 'Введіть назву категорії.'), parentId: z.union([z.literal(''), z.guid()]), active: z.boolean() })
export type CategoryValues = z.infer<typeof categoryFormSchema>
export interface ExpenseFilters { search: string; from: string; to: string; counterpartyId: string; categoryId: string; currencyId: string; isPurchaseExpense: '' | 'true' | 'false'; status: '' | 'DRAFT' | 'POSTED' }
export const expenseFilters: ExpenseFilters = { search: '', from: '', to: '', counterpartyId: '', categoryId: '', currencyId: '', isPurchaseExpense: '', status: '' }
export function expenseDefaults(expense?: Expense, purchaseId = ''): ExpenseValues {
  const now = new Date(); const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
  return { documentDate: expense?.document.documentDate ?? today, counterpartyId: expense?.counterpartyId ?? '', expenseCategoryId: expense?.expenseCategoryId ?? '', currencyId: expense?.currencyId ?? uahId, amount: expense?.amount ?? null, exchangeRate: expense?.currencyId && expense.currencyId !== uahId ? expense.exchangeRate : 1, purchaseInvoiceId: expense?.purchaseInvoiceId ?? purchaseId, comment: expense?.document.comment ?? '', internalNote: expense?.document.internalNote ?? '' }
}
export function expenseInput(values: ExpenseValues, baseline?: Expense) { return { ...(baseline ? { version: baseline.document.version } : {}), documentDate: values.documentDate, counterpartyId: values.counterpartyId, expenseCategoryId: values.expenseCategoryId, currencyId: values.currencyId, amount: values.amount, exchangeRate: values.currencyId === uahId ? 1 : values.exchangeRate, purchaseInvoiceId: values.purchaseInvoiceId || null, comment: values.comment || null, internalNote: values.internalNote || null } }
export const expenseAmountUah = (amount: number | null, rate: number | null) => amount !== null && rate !== null && Number.isFinite(amount) && Number.isFinite(rate) && amount > 0 && rate > 0 ? purchaseTotals([{ quantity: 1, unitPriceCurrency: amount }], 0, rate).uah : null
export const categoryInput = (v: CategoryValues) => ({ name: v.name, parentId: v.parentId || null, active: v.active })
export function categoryTree(categories: ExpenseCategory[]): { category: ExpenseCategory; depth: number; path: string }[] {
  const byParent = new Map<string, ExpenseCategory[]>()
  const ids = new Set(categories.map(c => c.id))
  for (const c of categories) { const parent = c.parentId && ids.has(c.parentId) ? c.parentId : ''; byParent.set(parent, [...(byParent.get(parent) ?? []), c]) }
  for (const children of byParent.values()) children.sort((a, b) => a.name.localeCompare(b.name, 'uk'))
  const stack = [...(byParent.get('') ?? [])].reverse().map(category => ({ category, depth: 0, path: category.name })); const seen = new Set<string>(); const result: { category: ExpenseCategory; depth: number; path: string }[] = []
  while (stack.length) { const row = stack.pop()!; if (seen.has(row.category.id)) continue; seen.add(row.category.id); result.push(row); for (const child of [...(byParent.get(row.category.id) ?? [])].reverse()) stack.push({ category: child, depth: row.depth + 1, path: `${row.path} / ${child.name}` }) }
  return result
}
export function excludedParents(categories: ExpenseCategory[], id?: string) {
  const excluded = new Set<string>(id ? [id] : []); if (!id) return excluded
  const children = new Map<string, string[]>(); for (const c of categories) if (c.parentId) children.set(c.parentId, [...(children.get(c.parentId) ?? []), c.id])
  const pending = [id]; while (pending.length) for (const child of children.get(pending.pop()!) ?? []) if (!excluded.has(child)) { excluded.add(child); pending.push(child) }
  return excluded
}
export function expenseError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'INVALID_PURCHASE_EXPENSE_ALLOCATION') return 'Цю витрату неможливо включити до собівартості вибраної закупівлі.'
    if (error.code === 'VERSION_CONFLICT' || /Version is stale|Concurrent modification/i.test(error.detail ?? '')) return 'Документ був змінений іншим користувачем. Оновіть дані.'
    if (error.code === 'DOCUMENT_NOT_DRAFT' || /Only a (?:nondeleted )?DRAFT/i.test(error.detail ?? '')) return 'Документ уже не є чернеткою. Оновіть дані.'
    if (/cycles|Invalid parent category/i.test(error.detail ?? '')) return 'Категорія не може посилатися на себе або свого нащадка. Цикли заборонені.'
    if (/requires a nondeleted POSTED PURCHASE_INVOICE/.test(error.detail ?? '')) return 'Для закупівельної витрати виберіть проведену закупівлю. Початкові залишки не підходять.'
    return error.message
  }
  return error instanceof Error ? error.message : 'Не вдалося виконати операцію.'
}
export const costWarning = 'Зміна або розпроведення цієї витрати може змінити поточну собівартість партій і перерахувати історичну валову маржу.'
export const allocationZeroMessage = 'Закупівельну витрату не можна розподілити для документа з нульовою вартістю товарів.'
