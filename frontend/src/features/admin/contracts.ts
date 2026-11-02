import { z } from 'zod'
export const userSchema = z.object({
  userId: z.guid(),
  username: z.string(),
  fullName: z.string(),
  organizationId: z.guid(),
  counterpartyId: z.guid().nullable(),
  active: z.boolean(),
  lastLoginAt: z.string().nullable(),
  roleIds: z.array(z.guid()),
  roleNames: z
    .array(z.string())
    .nullish()
    .transform((v) => v ?? []),
  counterpartyName: z.string().nullish(),
})
export type ManagedUser = z.infer<typeof userSchema>
export const roleSchema = z.object({
  id: z.guid(),
  organizationId: z.guid(),
  name: z.string(),
  active: z.boolean(),
  permissions: z.array(z.string()),
  userCount: z.number().int().default(0),
})
export type ManagedRole = z.infer<typeof roleSchema>
export const permissionSchema = z.object({ id: z.guid(), code: z.string(), description: z.string() })
export const userLookupsSchema = z.object({
  roles: z.array(z.object({ id: z.guid(), name: z.string(), active: z.boolean() })),
  counterparties: z.array(z.object({ id: z.guid(), name: z.string(), isEmployee: z.boolean() })),
})
export const userFormSchema = z.object({
  username: z.string().trim().min(1, 'Введіть логін.').max(256, 'Не більше 256 символів.'),
  fullName: z.string(),
  counterpartyId: z.string(),
  roleIds: z.array(z.guid()),
  active: z.boolean(),
  password: z.string(),
  confirmPassword: z.string(),
})
export type UserValues = z.infer<typeof userFormSchema>
export const roleFormSchema = z.object({
  name: z.string().trim().min(1, 'Введіть назву ролі.').max(256, 'Не більше 256 символів.'),
  active: z.boolean(),
})
export type RoleValues = z.infer<typeof roleFormSchema>
export const permissionGroups = [
  ['product', 'Товари'],
  ['purchase', 'Закупівлі'],
  ['sale', 'Продажі'],
  ['stock', 'Склад'],
  ['money', 'Гроші'],
  ['expense', 'Витрати'],
  ['settlement', 'Взаєморозрахунки'],
  ['reports', 'Звіти'],
  ['admin', 'Адміністрування'],
] as const
export function permissionGroup(code: string) {
  const prefix = code.split('.')[0]
  return prefix === 'cost'
    ? 'stock'
    : prefix === 'profit'
      ? 'reports'
      : ['users', 'roles', 'settings', 'audit'].includes(prefix)
        ? 'admin'
        : prefix
}
const actionNames: Record<string, string> = {
  view: 'Перегляд',
  create: 'Створення / редагування',
  edit: 'Редагування',
  post: 'Проведення',
  unpost: 'Розпроведення',
  delete: 'Видалення чернеток',
  change_price: 'Зміна ціни нижче рекомендованої',
  transfer: 'Переміщення',
  writeoff: 'Списання',
  receipt: 'Надходження',
  payment: 'Виплата',
  manage: 'Керування',
}
export function permissionLabel(code: string) {
  if (code === 'product.create') return 'Створення'
  if (code === 'money.transfer') return 'Перекази'
  if (code === 'cost.view') return 'Перегляд собівартості'
  if (code === 'profit.view') return 'Прибуток і рентабельність'
  const module: Record<string, string> = {
    users: 'Користувачі',
    roles: 'Ролі та права',
    settings: 'Налаштування',
    audit: 'Аудит',
  }
  return module[code.split('.')[0]] ?? actionNames[code.split('.')[1]] ?? code
}
export const auditEntrySchema = z.object({
  id: z.guid(),
  occurredAt: z.string(),
  actorId: z.guid().nullable(),
  username: z.string().nullable(),
  eventType: z.string(),
  entityType: z.string(),
  entityId: z.string(),
  documentId: z.guid().nullable(),
  documentNumber: z.string().nullable(),
  documentType: z.string().nullable(),
  summary: z.string(),
})
export const auditPageSchema = z.object({ items: z.array(auditEntrySchema), total: z.number().int() })
export const auditDetailSchema = z.object({
  entry: auditEntrySchema,
  oldValues: z.unknown().nullable(),
  newValues: z.unknown().nullable(),
})
const option = z.object({ id: z.guid(), name: z.string() })
export const auditLookupsSchema = z.object({
  users: z.array(option),
  eventTypes: z.array(z.string()),
  entityTypes: z.array(z.string()),
  documents: z.array(option),
})
export const auditEventNames: Record<string, string> = {
  CREATE: 'Створення',
  UPDATE: 'Зміна',
  DELETE: 'Видалення',
  ACTIVATE: 'Активація',
  DEACTIVATE: 'Деактивація',
  POST: 'Проведення',
  UNPOST: 'Розпроведення',
  PERMISSIONS: 'Зміна прав',
}
export const entityNames: Record<string, string> = {
  Product: 'Номенклатура',
  ProductPrice: 'Ціна послуги',
  BatchPrice: 'Ціна партії',
  ProductCategory: 'Категорія товарів',
  Unit: 'Одиниця виміру',
  StorageLocation: 'Місце зберігання',
  PriceType: 'Тип ціни',
  Counterparty: 'Контрагент',
  User: 'Користувач',
  Role: 'Роль',
  Document: 'Документ',
  PurchaseInvoice: 'Закупівля',
  SaleInvoice: 'Продаж',
  Expense: 'Витрата',
  ExpenseCategory: 'Категорія витрат',
  MoneyAccount: 'Грошовий рахунок',
  MoneyDocument: 'Грошовий документ',
  WriteOffReason: 'Причина списання',
}
export const timestamp = (v: string | null) => (v ? new Date(v).toLocaleString('uk-UA') : 'Ще не входив')
