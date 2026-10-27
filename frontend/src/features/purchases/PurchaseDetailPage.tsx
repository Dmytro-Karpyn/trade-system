import { useState } from 'react'
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Paper, Stack, Tab, Tabs, Typography } from '@mui/material'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { PurchaseBatches, PurchasePrices } from './PurchaseBatches'
import { PurchaseForm } from './PurchaseForm'
import { PurchaseInvoiceView } from './PurchaseInvoiceView'
import { PurchaseStatus } from './PurchaseStatus'
import { purchaseError } from './contracts'
import { usePurchaseNotice } from './notices'
import { usePurchase, usePurchaseBatches, usePurchaseLookups, usePurchaseMutations, usePurchaseSuppliers } from './queries'

const tabNames = ['Накладна', 'Партії', 'Закупівельні витрати', 'Ціни']
function PurchaseDetail({ id }: { id: string }) {
  const user = useCurrentUser().data
  const purchase = usePurchase(id)
  const batches = usePurchaseBatches(id)
  const lookups = usePurchaseLookups()
  const suppliers = usePurchaseSuppliers()
  const mutations = usePurchaseMutations(id)
  const notify = usePurchaseNotice()
  const navigate = useNavigate()
  const [tab, setTab] = useState(0)
  const [formKey, setFormKey] = useState(0)
  const [confirm, setConfirm] = useState<'post' | 'unpost' | 'delete' | null>(null)
  const [error, setError] = useState<unknown>(null)
  const busy = mutations.transition.isPending || mutations.remove.isPending
  const refresh = async () => { await Promise.all([purchase.refetch(), batches.refetch()]); setFormKey(value => value + 1); setError(null) }
  if (!user) return null
  if (purchase.isPending) return <ListLoading label="Завантаження закупівлі" />
  if (purchase.isError) return purchase.error instanceof ApiError && purchase.error.status === 404 ? <EmptyState icon="purchase" title="Закупівлю не знайдено" action={<Button component={Link} to="/purchases">До закупівель</Button>} /> : <RequestErrorState error={purchase.error} retry={() => { void purchase.refetch() }} />
  const data = purchase.data
  if (data.document.documentType === 'OPENING_STOCK') return <Paper variant="outlined"><EmptyState icon="stock" title="Початкові залишки" description="UI початкових залишків буде реалізовано окремим етапом." /></Paper>
  const posted = data.document.status === 'POSTED'
  const canEdit = !posted && hasAnyPermission(user, ['purchase.create'])
  const canDelete = !posted && hasAnyPermission(user, ['purchase.delete'])
  const canPost = !posted && hasAnyPermission(user, ['purchase.post'])
  const canUnpost = posted && hasAnyPermission(user, ['purchase.unpost'])
  const supplier = suppliers.data?.find(item => item.id === data.counterpartyId)?.name
  const transition = async () => {
    if (!confirm) return
    setError(null)
    try {
      if (confirm === 'delete') { await mutations.remove.mutateAsync(); notify({ severity: 'success', message: 'Чернетку видалено.' }); void navigate('/purchases', { replace: true }) }
      else { await mutations.transition.mutateAsync({ action: confirm, documentId: id }); setTab(0); notify({ severity: 'success', message: confirm === 'post' ? 'Закупівлю проведено.' : 'Закупівлю повернуто в чернетку.' }) }
    } catch (caught) { setError(caught); notify({ severity: 'error', message: purchaseError(caught) }) }
    finally { setConfirm(null) }
  }
  return <Stack spacing={3}>
    <Box><Button component={Link} to="/purchases" color="inherit" startIcon={<Icon name="back" />} sx={{ p: 0, mb: 1.5 }}>До закупівель</Button><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}><Box><Typography variant="h1" component="h1">{data.document.number || 'Чернетка закупівлі'}</Typography><Box sx={{ mt: 1.5 }}><PurchaseStatus status={data.document.status} /></Box></Box><Stack direction="row" spacing={1.5} sx={{ flexWrap: 'wrap', rowGap: 1.5 }}>{!canEdit && canDelete && <Button color="error" disabled={busy} onClick={() => setConfirm('delete')}>Видалити</Button>}{!canEdit && canPost && <Button variant="contained" disabled={busy} onClick={() => setConfirm('post')}>{busy ? 'Проводимо…' : 'Провести'}</Button>}{canUnpost && <Button variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>{busy ? 'Розпроводимо…' : 'Розпровести'}</Button>}</Stack></Stack>{posted && data.document.postedAt && <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>Проведено {new Date(data.document.postedAt).toLocaleString('uk-UA')}{data.postedByName ? ` · ${data.postedByName}` : ''}</Typography>}</Box>
    {error != null && <Alert severity="error" action={<Button onClick={() => { void refresh() }} disabled={busy}>Оновити дані</Button>}>{purchaseError(error)}</Alert>}
    {posted && <Box sx={{ borderBottom: '1px solid', borderColor: 'divider', minWidth: 0 }}><Tabs value={tab} onChange={(_, value: number) => setTab(value)} variant="scrollable" scrollButtons="auto" allowScrollButtonsMobile aria-label="Розділи закупівлі">{tabNames.map((label, index) => <Tab label={label} key={label} id={`purchase-tab-${index}`} aria-controls={`purchase-panel-${index}`} />)}</Tabs></Box>}
    <Box role={posted ? 'tabpanel' : undefined} id={`purchase-panel-${tab}`} aria-labelledby={posted ? `purchase-tab-${tab}` : undefined}>
      {batches.isPending ? <ListLoading label="Завантаження партій закупівлі" /> : batches.isError ? <RequestErrorState error={batches.error} retry={() => { void batches.refetch() }} /> : !posted ? canEdit ? <PurchaseForm key={formKey} purchase={data} batches={batches.data} supplierName={supplier} refresh={() => { void refresh() }} remove={canDelete ? () => setConfirm('delete') : undefined} saved={() => {}} /> : <PurchaseInvoiceView purchase={data} lookups={lookups.data} supplierName={supplier} /> : tab === 0 ? <Stack spacing={3}><PurchaseInvoiceView purchase={data} lookups={lookups.data} supplierName={supplier} /><PurchaseBatches purchase={data} batches={batches.data} canSeeCost={hasAnyPermission(user, ['cost.view'])} /></Stack> : tab === 1 ? <PurchaseBatches purchase={data} batches={batches.data} canSeeCost={hasAnyPermission(user, ['cost.view'])} /> : tab === 2 ? <Paper variant="outlined"><EmptyState icon="expense" title="Закупівельні витрати" description="Розділ буде реалізовано наступним етапом. Створення витрат тут поки недоступне." /></Paper> : <PurchasePrices id={id} batches={batches.data} />}
    </Box>
    <Dialog open={Boolean(confirm)} onClose={busy ? undefined : () => setConfirm(null)} fullWidth maxWidth="xs"><DialogTitle>{confirm === 'post' ? 'Провести закупівлю?' : confirm === 'unpost' ? 'Розпровести закупівлю?' : 'Видалити чернетку?'}</DialogTitle><DialogContent><DialogContentText>{confirm === 'post' ? 'Після проведення будуть створені партії та складські залишки.' : confirm === 'unpost' ? 'Документ повернеться в чернетку. Партії та їхні ціни буде збережено; складські рухи буде скасовано.' : 'Чернетку буде логічно видалено. Бізнес-дані залишаться в системі.'}</DialogContentText></DialogContent><DialogActions><Button onClick={() => setConfirm(null)} disabled={busy}>Скасувати</Button><Button variant="contained" color={confirm === 'delete' ? 'error' : 'primary'} onClick={() => { void transition() }} disabled={busy}>{busy ? 'Виконуємо…' : confirm === 'post' ? 'Провести' : confirm === 'unpost' ? 'Розпровести' : 'Видалити'}</Button></DialogActions></Dialog>
  </Stack>
}
export function PurchaseDetailPage() { const { id = '' } = useParams(); return <PurchaseDetail key={id} id={id} /> }
