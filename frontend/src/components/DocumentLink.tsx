import { Link as MuiLink } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../auth/contracts'
import { useCurrentUser } from '../auth/queries'

const targets: Record<string, { path: string; permission: string }> = {
  PURCHASE_INVOICE: { path: '/purchases', permission: 'purchase.view' }, OPENING_STOCK: { path: '/purchases', permission: 'purchase.view' },
  SALE_INVOICE: { path: '/sales', permission: 'sale.view' }, STOCK_TRANSFER: { path: '/stock/transfers', permission: 'stock.transfer' }, WRITE_OFF: { path: '/stock/write-offs', permission: 'stock.writeoff' },
  MONEY_RECEIPT: { path: '/money/receipts', permission: 'money.view' }, MONEY_PAYMENT: { path: '/money/payments', permission: 'money.view' }, MONEY_TRANSFER: { path: '/money/transfers', permission: 'money.view' },
  CUSTOMER_RETURN: { path: '/customer-returns', permission: 'sale.view' }, SUPPLIER_RETURN: { path: '/supplier-returns', permission: 'purchase.view' },
  EXPENSE: { path: '/expenses', permission: 'expense.view' },
}
export function DocumentLink({ id, type, number }: { id: string; type: string; number?: string | null }) {
  const user = useCurrentUser().data; const target = targets[type]
  return target && user && hasAnyPermission(user, [target.permission]) ? <MuiLink component={Link} to={`${target.path}/${id}`}>{number || 'Без номера'}</MuiLink> : <>{number || 'Без номера'}</>
}
