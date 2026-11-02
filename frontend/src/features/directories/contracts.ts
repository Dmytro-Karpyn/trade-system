import { z } from 'zod'
import { ApiError } from '../../api/client'
export const directoryKinds = [
  'categories',
  'units',
  'price-types',
  'storage-locations',
  'write-off-reasons',
  'currencies',
] as const
export type DirectoryKind = (typeof directoryKinds)[number]
export const directoryConfig: Record<
  DirectoryKind,
  { title: string; description: string; read: string[]; write: string[] }
> = {
  categories: {
    title: 'Категорії товарів',
    description: 'Дерево категорій номенклатури.',
    read: ['settings.manage', 'product.view', 'product.create', 'product.edit', 'stock.view'],
    write: ['settings.manage'],
  },
  units: {
    title: 'Одиниці виміру',
    description: 'Одна одиниця для кожного товару чи послуги.',
    read: ['settings.manage', 'product.view', 'product.create', 'product.edit'],
    write: ['settings.manage'],
  },
  'price-types': {
    title: 'Типи цін',
    description: 'Типи продажних цін і початкова націнка нових партій.',
    read: ['settings.manage', 'product.view', 'sale.view', 'purchase.view', 'stock.view'],
    write: ['settings.manage'],
  },
  'storage-locations': {
    title: 'Місця зберігання',
    description: 'Універсальні місця зберігання товарів.',
    read: ['settings.manage', 'purchase.view', 'purchase.create', 'sale.view', 'stock.view'],
    write: ['settings.manage'],
  },
  'write-off-reasons': {
    title: 'Причини списання',
    description: 'Причини та пов’язані категорії витрат.',
    read: ['settings.manage', 'stock.writeoff'],
    write: ['settings.manage', 'stock.writeoff'],
  },
  currencies: {
    title: 'Валюти',
    description: 'Відомі валюти. UAH — базова; довідник доступний лише для перегляду.',
    read: [
      'settings.manage',
      'purchase.view',
      'purchase.create',
      'sale.view',
      'money.view',
      'expense.view',
      'expense.create',
    ],
    write: [],
  },
}
export const directoryRowSchema = z.object({
  id: z.guid(),
  organizationId: z.guid().optional(),
  name: z.string(),
  active: z.boolean().default(true),
  parentId: z.guid().nullable().optional(),
  description: z.string().nullable().optional(),
  sortOrder: z.number().int().optional(),
  defaultMarkupPercent: z.number().optional(),
  expenseCategoryId: z.guid().optional(),
})
export type DirectoryRow = z.infer<typeof directoryRowSchema>
export const categorySchema = directoryRowSchema.extend({
  organizationId: z.guid(),
  parentId: z.guid().nullable(),
  description: z.string().nullable(),
  sortOrder: z.number().int(),
})
export type Category = z.infer<typeof categorySchema>
export const currencyReferenceSchema = z.object({
  id: z.guid(),
  code: z.string().nullable(),
  name: z.string().nullable(),
  active: z.boolean().nullable(),
  isBase: z.boolean(),
})
export const directoryFormSchema = z.object({
  name: z.string().trim().min(1, 'Введіть назву.'),
  active: z.boolean(),
  parentId: z.string(),
  description: z.string(),
  sortOrder: z.number().int('Порядок має бути цілим числом.'),
  defaultMarkupPercent: z
    .number()
    .min(0, 'Націнка не може бути від’ємною.')
    .lt(1000, 'Максимальна націнка — 999,9999%.')
    .refine((v) => Math.abs(v * 10000 - Math.round(v * 10000)) < 0.000001, 'Не більше 4 знаків після коми.'),
  expenseCategoryId: z.string(),
})
export type DirectoryValues = z.infer<typeof directoryFormSchema>
export function directoryDefaults(row?: DirectoryRow, parentId = ''): DirectoryValues {
  return {
    name: row?.name ?? '',
    active: row?.active ?? true,
    parentId: row?.parentId ?? parentId,
    description: row?.description ?? '',
    sortOrder: row?.sortOrder ?? 0,
    defaultMarkupPercent: row?.defaultMarkupPercent ?? 0,
    expenseCategoryId: row?.expenseCategoryId ?? '',
  }
}
export function directoryInput(kind: DirectoryKind, v: DirectoryValues) {
  const common = { name: v.name, active: v.active }
  return kind === 'categories'
    ? { ...common, parentId: v.parentId || null, description: v.description || null, sortOrder: v.sortOrder }
    : kind === 'price-types'
      ? { ...common, defaultMarkupPercent: v.defaultMarkupPercent, sortOrder: v.sortOrder }
      : kind === 'write-off-reasons'
        ? { ...common, expenseCategoryId: v.expenseCategoryId }
        : common
}
export function directoryTree<
  T extends { id: string; name: string; parentId?: string | null; sortOrder?: number },
>(nodes: T[]) {
  const children = new Map<string | null, T[]>()
  for (const n of nodes) {
    const key = n.parentId ?? null
    children.set(key, [...(children.get(key) ?? []), n])
  }
  for (const group of children.values())
    group.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.name.localeCompare(b.name))
  const roots = nodes
    .filter((n) => !n.parentId || !nodes.some((p) => p.id === n.parentId))
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0) || a.name.localeCompare(b.name))
  const pending = roots
    .slice()
    .reverse()
    .map((n) => ({ node: n, depth: 0, path: n.name }))
  const result: typeof pending = []
  const seen = new Set<string>()
  while (pending.length) {
    const row = pending.pop()!
    if (seen.has(row.node.id)) continue
    seen.add(row.node.id)
    result.push(row)
    for (const child of [...(children.get(row.node.id) ?? [])].reverse())
      pending.push({ node: child, depth: row.depth + 1, path: row.path + ' / ' + child.name })
  }
  return result
}
export function excludedCategories(nodes: DirectoryRow[], id?: string) {
  const found = new Set<string>()
  if (!id) return found
  const pending = [id]
  while (pending.length) {
    const next = pending.pop()!
    if (found.has(next)) continue
    found.add(next)
    pending.push(...nodes.filter((n) => n.parentId === next).map((n) => n.id))
  }
  return found
}
export function managementError(error: unknown) {
  if (!(error instanceof ApiError))
    return error instanceof Error ? error.message : 'Не вдалося зберегти зміни.'
  if (error.code === 'LAST_ADMIN_REQUIRED' || error.detail === 'LAST_ADMIN_REQUIRED')
    return 'Останнього активного адміністратора не можна деактивувати або позбавити адміністративних прав.'
  if (error.detail === 'ROLE_INACTIVE')
    return 'Права неактивної ролі не можна змінювати. Спочатку активуйте роль.'
  if (error.detail === 'CATEGORY_CYCLE') return 'Категорія не може посилатися на себе або свого нащадка.'
  if (
    (error.status === 409 || error.status === 400) &&
    /unique|duplicate|already (exists|taken)/i.test(error.detail ?? '')
  )
    return 'Запис із такою назвою або логіном уже існує.'
  if (
    error.status === 400 &&
    error.detail &&
    /password|username|role|parent|organization|category/i.test(error.detail)
  )
    return error.detail
  return error.message
}
