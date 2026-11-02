import { z } from 'zod'
import { ApiError } from '../../api/client'
import { uahId } from '../counterparties/contracts'
export { uahId }

export const moneyKinds = ['receipts', 'payments', 'transfers'] as const
export type MoneyKind = typeof moneyKinds[number]
export const moneyTitles: Record<MoneyKind, string> = { receipts: 'Надходження', payments: 'Виплати', transfers: 'Перекази' }
export const moneySingular: Record<MoneyKind, string> = { receipts: 'Надходження', payments: 'Виплата', transfers: 'Переказ' }
export const moneyRights: Record<MoneyKind, string> = { receipts: 'money.receipt', payments: 'money.payment', transfers: 'money.transfer' }
export const accountTypes = { CASH: 'Готівка', BANK: 'Банк', SAFE: 'Сейф', OTHER: 'Інший' } as const
export const purposes = { SETTLEMENT: 'Взаєморозрахунок', OWNER: 'Операція власника', OPENING_BALANCE: 'Початковий баланс' } as const
const accountBase = { id: z.guid(), name: z.string(), accountType: z.enum(['CASH', 'BANK', 'SAFE', 'OTHER']), currencyId: z.guid(), active: z.boolean() }
export const accountSchema = z.object(accountBase)
export type MoneyAccount = z.infer<typeof accountSchema>
export const accountOptionSchema = z.object({ ...accountBase, currencyLocked: z.boolean() })
export const accountOverviewSchema = z.object({ account: accountOptionSchema, balance: z.number() })
export type AccountOverview = z.infer<typeof accountOverviewSchema>
export const moneyLookupsSchema = z.object({ accounts: z.array(accountOptionSchema), counterparties: z.array(z.object({ id: z.guid(), name: z.string(), active: z.boolean() })), currencyIds: z.array(z.guid()) })
export type MoneyLookups = z.infer<typeof moneyLookupsSchema>
const documentSchema = z.object({ id: z.guid(), number: z.string().nullable(), documentDate: z.iso.date(), status: z.enum(['DRAFT', 'POSTED']), version: z.number().int().positive(), comment: z.string().nullable(), internalNote: z.string().nullable(), postedAt: z.string().nullable() })
export const moneyDocumentSchema = z.object({ document: documentSchema, purpose: z.enum(['SETTLEMENT', 'OWNER', 'OPENING_BALANCE']).nullable(), counterpartyId: z.guid().nullable(), sourceAccountId: z.guid().nullable(), destinationAccountId: z.guid().nullable(), sourceAmount: z.number().nullable(), destinationAmount: z.number().nullable(), settlementCurrencyId: z.guid().nullable(), settlementAmount: z.number().nullable(), settlementExchangeRate: z.number().nullable() })
export type MoneyDocument = z.infer<typeof moneyDocumentSchema>
export const moneyListSchema = z.object({ money: moneyDocumentSchema, sourceAccountName: z.string().nullable(), sourceCurrencyId: z.guid().nullable(), destinationAccountName: z.string().nullable(), destinationCurrencyId: z.guid().nullable(), counterpartyName: z.string().nullable() })
export interface MoneyFilters { from: string; to: string; accountId: string; counterpartyId: string; purpose: string; status: string }
export const moneyFilters: MoneyFilters = { from: '', to: '', accountId: '', counterpartyId: '', purpose: '', status: '' }
const requiredId = (message: string) => z.string().min(1, message).refine(value => !value || z.guid().safeParse(value).success, message)
export const accountFormSchema = z.object({ name: z.string().trim().min(1, 'Введіть назву рахунку.'), accountType: z.enum(['CASH', 'BANK', 'SAFE', 'OTHER']), currencyId: requiredId('Виберіть валюту.'), active: z.boolean() })
export type AccountValues = z.infer<typeof accountFormSchema>
const decimal = (scale: number) => z.number({ error: 'Введіть число.' }).finite().positive('Значення має бути більше нуля.').lt(scale === 8 ? 1e12 : 1e16, 'Завелике значення.').refine(value => Math.abs(value * 10 ** scale - Math.round(value * 10 ** scale)) < 0.000001, `Не більше ${scale} знаків після коми.`).nullable()
const formFields = z.object({ documentDate: z.iso.date({ error: 'Вкажіть дату.' }), purpose: z.enum(['', 'SETTLEMENT', 'OWNER', 'OPENING_BALANCE']), sourceAccountId: z.string(), destinationAccountId: z.string(), sourceAmount: decimal(4), destinationAmount: decimal(4), counterpartyId: z.string(), settlementCurrencyId: z.string(), settlementAmount: decimal(4), settlementExchangeRate: decimal(8), comment: z.string(), internalNote: z.string() })
export type MoneyValues = z.infer<typeof formFields>
export function expensePaymentPrefill(state: unknown): Partial<MoneyValues> | undefined {
  const parsed = z.object({ expensePaymentPrefill: z.object({ counterpartyId: z.guid(), currencyId: z.guid(), amount: z.number().finite().positive().lt(1e16), exchangeRate: z.number().finite().positive().lt(1e14) }) }).safeParse(state)
  if (!parsed.success) return undefined
  const input = parsed.data.expensePaymentPrefill
  return { purpose: 'SETTLEMENT', counterpartyId: input.counterpartyId, settlementCurrencyId: input.currencyId, settlementAmount: input.amount, settlementExchangeRate: input.currencyId === uahId ? 1 : input.exchangeRate }
}
export function moneyFormSchema(kind: MoneyKind, accounts: MoneyAccount[], allowEmptyPurpose = false) {
  return formFields.superRefine((v, ctx) => {
    const require = (field: keyof MoneyValues, value: unknown, message: string) => { if (value === '' || value === null) ctx.addIssue({ code: 'custom', path: [field], message }) }
    if (kind !== 'receipts') { require('sourceAccountId', v.sourceAccountId, 'Виберіть рахунок списання.'); require('sourceAmount', v.sourceAmount, 'Введіть суму списання.') }
    if (kind !== 'payments') { require('destinationAccountId', v.destinationAccountId, 'Виберіть рахунок зарахування.'); require('destinationAmount', v.destinationAmount, 'Введіть суму зарахування.') }
    if (kind === 'transfers') {
      if (v.sourceAccountId && v.sourceAccountId === v.destinationAccountId) ctx.addIssue({ code: 'custom', path: ['destinationAccountId'], message: 'Рахунки мають бути різними.' })
      const source = accounts.find(a => a.id === v.sourceAccountId); const destination = accounts.find(a => a.id === v.destinationAccountId)
      if (source && destination && source.currencyId === destination.currencyId && v.sourceAmount !== v.destinationAmount) ctx.addIssue({ code: 'custom', path: ['destinationAmount'], message: 'Для однієї валюти суми мають збігатися.' })
    } else {
      if (!allowEmptyPurpose) require('purpose', v.purpose, 'Виберіть призначення операції.')
      if (v.purpose === 'SETTLEMENT') {
        require('counterpartyId', v.counterpartyId, 'Виберіть контрагента.'); require('settlementCurrencyId', v.settlementCurrencyId, 'Виберіть валюту взаєморозрахунку.'); require('settlementAmount', v.settlementAmount, 'Введіть суму взаєморозрахунку.'); require('settlementExchangeRate', v.settlementExchangeRate, 'Введіть курс.')
        if (v.settlementCurrencyId === uahId && v.settlementExchangeRate !== 1) ctx.addIssue({ code: 'custom', path: ['settlementExchangeRate'], message: 'Для UAH курс дорівнює 1.' })
      }
    }
  })
}
export function moneyDefaults(kind: MoneyKind, money?: MoneyDocument): MoneyValues {
  const now = new Date(); const date = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
  return { documentDate: money?.document.documentDate ?? date, purpose: money ? money.purpose ?? '' : kind === 'transfers' ? '' : 'SETTLEMENT', sourceAccountId: money?.sourceAccountId ?? '', destinationAccountId: money?.destinationAccountId ?? '', sourceAmount: money?.sourceAmount ?? null, destinationAmount: money?.destinationAmount ?? null, counterpartyId: money?.counterpartyId ?? '', settlementCurrencyId: money?.settlementCurrencyId ?? uahId, settlementAmount: money?.settlementAmount ?? null, settlementExchangeRate: money?.settlementExchangeRate ?? 1, comment: money?.document.comment ?? '', internalNote: money?.document.internalNote ?? '' }
}
export function moneyInput(kind: MoneyKind, values: MoneyValues, baseline?: MoneyDocument) {
  const settlement = kind !== 'transfers' && values.purpose === 'SETTLEMENT'
  return { ...(baseline ? { version: baseline.document.version } : {}), documentDate: values.documentDate, comment: values.comment || null, internalNote: values.internalNote || null, purpose: kind === 'transfers' ? baseline?.purpose ?? null : values.purpose || null, sourceAccountId: kind === 'receipts' ? null : values.sourceAccountId, destinationAccountId: kind === 'payments' ? null : values.destinationAccountId, sourceAmount: kind === 'receipts' ? null : values.sourceAmount, destinationAmount: kind === 'payments' ? null : values.destinationAmount, counterpartyId: settlement ? values.counterpartyId : null, settlementCurrencyId: settlement ? values.settlementCurrencyId : null, settlementAmount: settlement ? values.settlementAmount : null, settlementExchangeRate: settlement ? values.settlementCurrencyId === uahId ? 1 : values.settlementExchangeRate : null }
}
export const effectiveRate = (source: number | null, destination: number | null) => source !== null && destination !== null && source > 0 && destination > 0 ? destination / source : null
export function moneyError(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'VERSION_CONFLICT' || /Version is stale|Concurrent modification/i.test(error.detail ?? '')) return 'Документ був змінений іншим користувачем. Оновіть дані.'
    if (error.code === 'DOCUMENT_NOT_DRAFT' || /Only a (?:nondeleted )?DRAFT|requires a DRAFT/i.test(error.detail ?? '')) return 'Документ уже не є чернеткою. Оновіть дані.'
    if (error.code === 'INVALID_MONEY_OPERATION') return 'Некоректна грошова операція. Перевірте рахунки, суми та реквізити взаєморозрахунку.'
    if (/CurrencyId cannot change/.test(error.detail ?? '')) return 'Валюту використаного рахунку змінити не можна.'
    if (error.code === 'PERMISSION_DENIED' || error.status === 403) return 'Недостатньо прав для цієї дії.'
    return error.message
  }
  return error instanceof Error ? error.message : 'Не вдалося виконати операцію.'
}
