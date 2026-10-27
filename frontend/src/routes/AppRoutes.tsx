import { CounterpartyWorkspace } from '../features/counterparties/CounterpartyWorkspace'
import { CounterpartyListPage } from '../features/counterparties/CounterpartyListPage'
import { CounterpartyNewPage } from '../features/counterparties/CounterpartyNewPage'
import { CounterpartyDetailPage } from '../features/counterparties/CounterpartyDetailPage'
import { partyReadPermissions } from '../features/counterparties/contracts'
import { PurchaseWorkspace } from '../features/purchases/PurchaseWorkspace'
import { PurchaseListPage } from '../features/purchases/PurchaseListPage'
import { PurchaseNewPage } from '../features/purchases/PurchaseNewPage'
import { PurchaseDetailPage } from '../features/purchases/PurchaseDetailPage'
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
        {navigation.filter(item => !['/', '/products', '/counterparties', '/purchases'].includes(item.path)).map(item => <Route key={item.path} path={item.path} element={<SectionRoute item={item} />} />)}
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
