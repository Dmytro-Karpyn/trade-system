import type { CurrentUser } from '../auth/contracts'

export const testUser: CurrentUser = {
  userId: '019a073c-9ee5-7000-8000-000000000001',
  username: 'okoval', fullName: 'Олена Коваль',
  organizationId: '00000000-0000-0000-0000-000000000001',
  roles: ['SALES_MANAGER'],
  permissions: ['product.view', 'purchase.view', 'sale.view', 'stock.view', 'money.view', 'expense.view', 'settlement.view', 'reports.view', 'profit.view', 'users.manage', 'roles.manage', 'settings.manage'],
}
export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
