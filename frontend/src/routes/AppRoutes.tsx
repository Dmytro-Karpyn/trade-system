import { CounterpartyWorkspace } from '../features/counterparties/CounterpartyWorkspace'
import { CounterpartyListPage } from '../features/counterparties/CounterpartyListPage'
import { CounterpartyNewPage } from '../features/counterparties/CounterpartyNewPage'
import { CounterpartyDetailPage } from '../features/counterparties/CounterpartyDetailPage'
import { partyReadPermissions } from '../features/counterparties/contracts'
import { PurchaseWorkspace } from '../features/purchases/PurchaseWorkspace'
import { PurchaseListPage } from '../features/purchases/PurchaseListPage'
import { PurchaseNewPage } from '../features/purchases/PurchaseNewPage'
import { PurchaseDetailPage } from '../features/purchases/PurchaseDetailPage'
import { SaleWorkspace } from '../features/sales/SaleWorkspace'
import { SaleListPage } from '../features/sales/SaleListPage'
import { SaleNewPage } from '../features/sales/SaleNewPage'
import { SaleDetailPage } from '../features/sales/SaleDetailPage'
import { StockWorkspace } from '../features/stock/StockWorkspace'
import { StockLandingPage, StockProductPage, StockBatchPage } from '../features/stock/StockPages'
import { OperationListPage, OperationPage } from '../features/stock/OperationPages'
import { MoneyWorkspace } from '../features/money/MoneyWorkspace'
import { MoneyAccountsPage, MoneyLandingPage } from '../features/money/MoneyAccounts'
import { MoneyListPage } from '../features/money/MoneyListPage'
import { MoneyDocumentPage } from '../features/money/MoneyDocumentPage'
import { moneyKinds, moneyRights } from '../features/money/contracts'
import { SettlementsPage, SettlementDetailPage } from '../features/settlements/SettlementPages'
import { ReturnWorkspace } from '../features/returns/ReturnWorkspace'
import { returnConfig } from '../features/returns/contracts'
import { directoryConfig,directoryKinds } from '../features/directories/contracts'
import { ManagementWorkspace } from '../features/directories/ManagementWorkspace'
import { reportKinds } from '../features/reports/contracts'
import { ExpenseWorkspace } from '../features/expenses/ExpenseWorkspace'
import { lazy, Suspense } from 'react'
import type { ReactNode } from 'react'
import { ProductWorkspace } from '../features/products/ProductWorkspace'
import { ProductListPage } from '../features/products/ProductListPage'
import { ProductNewPage } from '../features/products/ProductNewPage'
import { ProductDetailPage } from '../features/products/ProductDetailPage'
import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { Button, Paper } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../auth/contracts'
import { useCurrentUser } from '../auth/queries'
import { EmptyState } from '../components/EmptyState'
import { ForbiddenState, LoadingState, RequestErrorState } from '../components/RequestState'
import { AppLayout } from '../layout/AppLayout'
import { DashboardPage } from '../pages/DashboardPage'
import { LoginPage } from '../pages/LoginPage'
import { PlaceholderPage } from '../pages/PlaceholderPage'
import { navigation } from './navigation'
import type { NavigationItem } from './navigation'

const ExpenseListPage = lazy(() => import('../features/expenses/ExpenseListPage').then(module => ({ default: module.ExpenseListPage })))
const ExpenseDetailPage = lazy(() => import('../features/expenses/ExpenseDetailPage').then(module => ({ default: module.ExpenseDetailPage })))
const ExpenseCategoriesPage = lazy(() => import('../features/expenses/ExpenseCategoriesPage').then(module => ({ default: module.ExpenseCategoriesPage })))
const ReturnListPage = lazy(() => import('../features/returns/ReturnListPage').then(module => ({ default: module.ReturnListPage })))
const ReturnDetailPage = lazy(() => import('../features/returns/ReturnDetailPage').then(module => ({ default: module.ReturnDetailPage })))
const ReportsIndexPage = lazy(() => import('../features/reports/ReportPages').then(module => ({ default: module.ReportsIndexPage })))
const ReportPage = lazy(() => import('../features/reports/ReportPages').then(module => ({ default: module.ReportPage })))
const DirectoriesIndexPage=lazy(()=>import('../features/directories/DirectoryPages').then(m=>({default:m.DirectoriesIndexPage})))
const DirectoryPage=lazy(()=>import('../features/directories/DirectoryPages').then(m=>({default:m.DirectoryPage})))
const AdminIndexPage=lazy(()=>import('../features/admin/AdminPages').then(m=>({default:m.AdminIndexPage})))
const UsersPage=lazy(()=>import('../features/admin/AdminPages').then(m=>({default:m.UsersPage})))
const UserPage=lazy(()=>import('../features/admin/AdminPages').then(m=>({default:m.UserPage})))
const RolesPage=lazy(()=>import('../features/admin/AdminPages').then(m=>({default:m.RolesPage})))
const RolePage=lazy(()=>import('../features/admin/AdminPages').then(m=>({default:m.RolePage})))
const AuditPage=lazy(()=>import('../features/admin/AuditPage').then(m=>({default:m.AuditPage})))
function ExpenseScreen({ children }: { children: ReactNode }) { return <Suspense fallback={<LoadingState />}>{children}</Suspense> }

function ProtectedRoutes() {
  const user = useCurrentUser()
  if (user.isPending) return <LoadingState />
  if (user.isError) return <RequestErrorState error={user.error} retry={() => { void user.refetch() }} />
  if (!user.data) return <Navigate to="/login" replace />
  return <Outlet />
}
function LoginRoute() {
  const user = useCurrentUser()
  if (user.isPending) return <LoadingState />
  if (user.isError) return <RequestErrorState error={user.error} retry={() => { void user.refetch() }} />
  if (user.data) return <Navigate to="/" replace />
  return <LoginPage />
}
function RequirePermission({ permission, children }: { permission: string | readonly string[]; children: ReactNode }) {
  const user = useCurrentUser().data
  if (!user) return null
  return hasAnyPermission(user, typeof permission === 'string' ? [permission] : permission) ? children : <ForbiddenState />
}
function SectionRoute({ item }: { item: NavigationItem }) {
  const user = useCurrentUser().data
  if (!user) return null
  if (!hasAnyPermission(user, item.permissions)) return <ForbiddenState />
  return <PlaceholderPage item={item} />
}
export function AppRoutes() {
  return <Routes>
    <Route path="/login" element={<LoginRoute />} />
    <Route element={<ProtectedRoutes />}>
      <Route element={<AppLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="/directories" element={<ManagementWorkspace/>}>
          <Route index element={<RequirePermission permission={navigation.find(n=>n.path==='/directories')!.permissions??[]}><ExpenseScreen><DirectoriesIndexPage/></ExpenseScreen></RequirePermission>}/>
          {directoryKinds.map(kind=><Route key={kind} path={kind} element={<RequirePermission permission={directoryConfig[kind].read}><ExpenseScreen><DirectoryPage key={kind} kind={kind}/></ExpenseScreen></RequirePermission>}/>) }
        </Route>
        <Route path="/admin" element={<ManagementWorkspace/>}>
          <Route index element={<RequirePermission permission={['users.manage','roles.manage','settings.manage','audit.view']}><ExpenseScreen><AdminIndexPage/></ExpenseScreen></RequirePermission>}/>
          <Route path="users" element={<RequirePermission permission="users.manage"><ExpenseScreen><UsersPage/></ExpenseScreen></RequirePermission>}/>
          <Route path="users/new" element={<RequirePermission permission="users.manage"><ExpenseScreen><UserPage newUser/></ExpenseScreen></RequirePermission>}/>
          <Route path="users/:id" element={<RequirePermission permission="users.manage"><ExpenseScreen><UserPage/></ExpenseScreen></RequirePermission>}/>
          <Route path="roles" element={<RequirePermission permission="roles.manage"><ExpenseScreen><RolesPage/></ExpenseScreen></RequirePermission>}/>
          <Route path="roles/new" element={<RequirePermission permission="roles.manage"><ExpenseScreen><RolePage newRole/></ExpenseScreen></RequirePermission>}/>
          <Route path="roles/:id" element={<RequirePermission permission="roles.manage"><ExpenseScreen><RolePage/></ExpenseScreen></RequirePermission>}/>
          <Route path="audit" element={<RequirePermission permission="audit.view"><ExpenseScreen><AuditPage/></ExpenseScreen></RequirePermission>}/>
        </Route>
        <Route path="/reports" element={<RequirePermission permission="reports.view"><ExpenseScreen><ReportsIndexPage /></ExpenseScreen></RequirePermission>} />
        {reportKinds.map(kind=><Route key={kind} path={`/reports/${kind}`} element={<RequirePermission permission="reports.view"><ExpenseScreen><ReportPage key={kind} kind={kind}/></ExpenseScreen></RequirePermission>}/>)}
        {navigation.filter(item => !['/', '/products', '/counterparties', '/purchases', '/sales', '/stock', '/money', '/settlements', '/expenses', '/reports', '/directories', '/admin'].includes(item.path)).map(item => <Route key={item.path} path={item.path} element={<SectionRoute item={item} />} />)}
        {(['customer', 'supplier'] as const).map(kind => <Route key={kind} path={returnConfig[kind].root} element={<ReturnWorkspace />}>
          <Route index element={<RequirePermission permission={`${returnConfig[kind].permission}.view`}><ExpenseScreen><ReturnListPage kind={kind} /></ExpenseScreen></RequirePermission>} />
          <Route path="new" element={<RequirePermission permission={`${returnConfig[kind].permission}.create`}><ExpenseScreen><ReturnDetailPage kind={kind} newDocument /></ExpenseScreen></RequirePermission>} />
          <Route path=":id" element={<RequirePermission permission={`${returnConfig[kind].permission}.view`}><ExpenseScreen><ReturnDetailPage kind={kind} /></ExpenseScreen></RequirePermission>} />
        </Route>)}
        <Route path="/expenses" element={<ExpenseWorkspace />}>
          <Route index element={<RequirePermission permission="expense.view"><ExpenseScreen><ExpenseListPage /></ExpenseScreen></RequirePermission>} />
          <Route path="new" element={<RequirePermission permission="expense.create"><ExpenseScreen><ExpenseDetailPage newDocument /></ExpenseScreen></RequirePermission>} />
          <Route path=":id" element={<RequirePermission permission="expense.view"><ExpenseScreen><ExpenseDetailPage /></ExpenseScreen></RequirePermission>} />
        </Route>
        <Route path="/expense-categories" element={<ExpenseWorkspace />}>
          <Route index element={<RequirePermission permission={['expense.view','expense.create','settings.manage']}><ExpenseScreen><ExpenseCategoriesPage /></ExpenseScreen></RequirePermission>} />
        </Route>
        <Route path="/money" element={<MoneyWorkspace />}>
          <Route index element={<RequirePermission permission={['money.view', 'money.receipt', 'money.payment', 'money.transfer']}><MoneyLandingPage /></RequirePermission>} />
          <Route path="accounts" element={<RequirePermission permission="money.view"><MoneyAccountsPage /></RequirePermission>} />
          {moneyKinds.map(kind => <Route key={kind} path={kind}>
            <Route index element={<RequirePermission permission="money.view"><MoneyListPage kind={kind} /></RequirePermission>} />
            <Route path="new" element={<RequirePermission permission={moneyRights[kind]}><MoneyDocumentPage kind={kind} newDocument /></RequirePermission>} />
            <Route path=":id" element={<RequirePermission permission="money.view"><MoneyDocumentPage kind={kind} /></RequirePermission>} />
          </Route>)}
        </Route>
        <Route path="/settlements" element={<RequirePermission permission="settlement.view"><SettlementsPage /></RequirePermission>} />
        <Route path="/settlements/:counterpartyId" element={<RequirePermission permission="settlement.view"><SettlementDetailPage /></RequirePermission>} />
        <Route path="/stock" element={<StockWorkspace />}>
          <Route index element={<RequirePermission permission={['stock.view', 'stock.transfer', 'stock.writeoff']}><StockLandingPage /></RequirePermission>} />
          <Route path="products/:productId" element={<RequirePermission permission="stock.view"><StockProductPage /></RequirePermission>} />
          <Route path="batches/:batchId" element={<RequirePermission permission="stock.view"><StockBatchPage /></RequirePermission>} />
          {(['transfers', 'write-offs'] as const).map(kind => <Route key={kind} path={kind}>
            <Route index element={<RequirePermission permission={kind === 'transfers' ? 'stock.transfer' : 'stock.writeoff'}><OperationListPage kind={kind} /></RequirePermission>} />
            <Route path="new" element={<RequirePermission permission={kind === 'transfers' ? 'stock.transfer' : 'stock.writeoff'}><OperationPage kind={kind} newDocument /></RequirePermission>} />
            <Route path=":id" element={<RequirePermission permission={kind === 'transfers' ? 'stock.transfer' : 'stock.writeoff'}><OperationPage kind={kind} /></RequirePermission>} />
          </Route>)}
        </Route>
        <Route path="/sales" element={<SaleWorkspace />}>
          <Route index element={<RequirePermission permission="sale.view"><SaleListPage /></RequirePermission>} />
          <Route path="new" element={<RequirePermission permission="sale.create"><SaleNewPage /></RequirePermission>} />
          <Route path=":id" element={<RequirePermission permission="sale.view"><SaleDetailPage /></RequirePermission>} />
        </Route>
        <Route path="/purchases" element={<PurchaseWorkspace />}>
          <Route index element={<RequirePermission permission="purchase.view"><PurchaseListPage /></RequirePermission>} />
          <Route path="new" element={<RequirePermission permission="purchase.create"><PurchaseNewPage /></RequirePermission>} />
          <Route path=":id" element={<RequirePermission permission="purchase.view"><PurchaseDetailPage /></RequirePermission>} />
        </Route>
        <Route path="/products" element={<ProductWorkspace />}>
          <Route index element={<RequirePermission permission="product.view"><ProductListPage /></RequirePermission>} />
          <Route path="new" element={<RequirePermission permission="product.create"><ProductNewPage /></RequirePermission>} />
          <Route path=":id" element={<RequirePermission permission="product.view"><ProductDetailPage /></RequirePermission>} />
        </Route>
        <Route path="/counterparties" element={<CounterpartyWorkspace />}>
          <Route index element={<RequirePermission permission={partyReadPermissions}><CounterpartyListPage /></RequirePermission>} />
          <Route path="new" element={<RequirePermission permission="settings.manage"><CounterpartyNewPage /></RequirePermission>} />
          <Route path=":id" element={<RequirePermission permission={partyReadPermissions}><CounterpartyDetailPage /></RequirePermission>} />
        </Route>
        <Route path="/forbidden" element={<ForbiddenState />} />
        <Route path="*" element={<Paper variant="outlined"><EmptyState title="Сторінку не знайдено" description="Перевірте адресу або поверніться до робочого простору." action={<Button component={Link} to="/" variant="outlined">На головну</Button>} /></Paper>} />
      </Route>
    </Route>
  </Routes>
}
