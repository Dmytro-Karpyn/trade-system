import type { IconName } from '../components/Icon'

export interface NavigationItem {
  path: string
  title: string
  icon: IconName
  group: 'Робочий простір' | 'Операції' | 'Управління'
  permissions?: readonly string[]
}
export const navigation: readonly NavigationItem[] = [
  { path: '/', title: 'Головна', icon: 'home', group: 'Робочий простір' },
  { path: '/products', title: 'Товари', icon: 'box', group: 'Робочий простір', permissions: ['product.view'] },
  { path: '/purchases', title: 'Закупівлі', icon: 'purchase', group: 'Робочий простір', permissions: ['purchase.view'] },
  { path: '/sales', title: 'Продажі', icon: 'sale', group: 'Робочий простір', permissions: ['sale.view'] },
  { path: '/stock', title: 'Склад', icon: 'stock', group: 'Операції', permissions: ['stock.view', 'stock.transfer', 'stock.writeoff'] },
  { path: '/money', title: 'Гроші', icon: 'wallet', group: 'Операції', permissions: ['money.view'] },
  { path: '/expenses', title: 'Витрати', icon: 'expense', group: 'Операції', permissions: ['expense.view'] },
  { path: '/settlements', title: 'Взаєморозрахунки', icon: 'settlement', group: 'Операції', permissions: ['settlement.view'] },
  { path: '/counterparties', title: 'Контрагенти', icon: 'people', group: 'Операції', permissions: ['settlement.view', 'sale.view', 'purchase.view'] },
  { path: '/reports', title: 'Звіти', icon: 'chart', group: 'Управління', permissions: ['reports.view', 'profit.view'] },
  { path: '/directories', title: 'Довідники', icon: 'folder', group: 'Управління', permissions: ['settings.manage', 'product.view', 'sale.view', 'purchase.view', 'money.view', 'expense.view', 'stock.writeoff'] },
  { path: '/admin', title: 'Адміністрування', icon: 'settings', group: 'Управління', permissions: ['users.manage', 'roles.manage', 'settings.manage', 'audit.view'] },
]

export function sectionTitle(pathname: string) {
  return navigation.find(item => item.path === pathname || item.path !== '/' && pathname.startsWith(`${item.path}/`))?.title
}
