import { z } from 'zod'

export const legalTypes = { PERSON: 'Фізична особа', ENTREPRENEUR: 'ФОП', COMPANY: 'Компанія' } as const
export const counterpartySchema = z.object({
  id: z.guid(), organizationId: z.guid(), name: z.string(), legalType: z.enum(['PERSON', 'ENTREPRENEUR', 'COMPANY']).nullable(),
  taxId: z.string().nullable(), registrationCode: z.string().nullable(), phone: z.string().nullable(), email: z.string().nullable(), address: z.string().nullable(), defaultPriceTypeId: z.guid().nullable(),
  isCustomer: z.boolean(), isSupplier: z.boolean(), isEmployee: z.boolean(), isSystem: z.boolean(), active: z.boolean(), note: z.string().nullable(),
})
export type Counterparty = z.infer<typeof counterpartySchema>
export const priceTypeSchema = z.object({ id: z.guid(), organizationId: z.guid(), name: z.string(), defaultMarkupPercent: z.number(), active: z.boolean(), sortOrder: z.number() })
export type PriceType = z.infer<typeof priceTypeSchema>
export const partyReadPermissions = ['settlement.view', 'sale.view', 'purchase.view'] as const
export const priceReadPermissions = ['product.view', 'sale.view', 'purchase.view'] as const
export type PartyRoleFilter = '' | 'customer' | 'supplier' | 'employee' | 'system'
export interface PartyFilters { search: string; role: PartyRoleFilter; active: '' | 'true' | 'false' }
export const partyFormSchema = z.object({
  name: z.string().trim().min(1, 'Введіть назву контрагента.'),
  legalType: z.enum(['', 'PERSON', 'ENTREPRENEUR', 'COMPANY']),
  taxId: z.string(), registrationCode: z.string(), phone: z.string(),
  email: z.string().trim().refine(value => value === '' || z.email().safeParse(value).success, 'Введіть коректний email.'),
  address: z.string(), defaultPriceTypeId: z.union([z.literal(''), z.guid()]),
  isCustomer: z.boolean(), isSupplier: z.boolean(), isEmployee: z.boolean(), active: z.boolean(), note: z.string(),
})
export type PartyFormValues = z.infer<typeof partyFormSchema>
export const partyDefaults: PartyFormValues = { name: '', legalType: '', taxId: '', registrationCode: '', phone: '', email: '', address: '', defaultPriceTypeId: '', isCustomer: false, isSupplier: false, isEmployee: false, active: true, note: '' }
export function partyInput(values: PartyFormValues, existing?: Counterparty) {
  return { ...values, legalType: values.legalType || null, taxId: values.taxId.trim() || null, registrationCode: values.registrationCode.trim() || null,
    phone: values.phone.trim() || null, email: values.email || null, address: values.address || null, note: values.note || null,
    defaultPriceTypeId: values.defaultPriceTypeId || null, isSystem: existing?.isSystem ?? false }
}
export type PartyInput = ReturnType<typeof partyInput>
export function priceTypeName(id: string | null, types: readonly PriceType[]) {
  return id ? types.find(type => type.id === id)?.name ?? 'Назва недоступна' : '—'
}
const balanceSchema = z.object({ counterpartyId: z.guid(), currencyId: z.guid(), balance: z.number() })
const movementSchema = z.object({ id: z.guid(), documentId: z.guid(), documentNumber: z.string().nullable().optional(), documentType: z.string(), documentDate: z.iso.date(), counterpartyId: z.guid(), currencyId: z.guid(), amount: z.number(), baseAmountUah: z.number(), balanceAfterMovement: z.number() })
export const settlementSchema = z.object({ counterpartyId: z.guid(), balances: z.array(balanceSchema), movements: z.array(movementSchema) })
export type PartySettlement = z.infer<typeof settlementSchema>
export const uahId = '00000000-0000-0000-0000-000000000980'
export function balanceStatus(value: number) { return value > 0 ? 'Нам винні' : value < 0 ? 'Ми винні' : 'Нульовий баланс' }
export function formatAmount(value: number, signed = false) { return new Intl.NumberFormat('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 4, signDisplay: signed ? 'exceptZero' : 'auto' }).format(value) }
export const operationNames: Record<string, string> = { PURCHASE_INVOICE: 'Закупівля', SALE_INVOICE: 'Продаж', EXPENSE: 'Витрата', MONEY_RECEIPT: 'Надходження грошей', MONEY_PAYMENT: 'Виплата грошей', MONEY_TRANSFER: 'Переказ грошей', CUSTOMER_RETURN: 'Повернення покупця', SUPPLIER_RETURN: 'Повернення постачальнику', OPENING_STOCK: 'Початковий залишок', STOCK_TRANSFER: 'Переміщення', WRITE_OFF: 'Списання' }
