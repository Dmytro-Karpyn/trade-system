import { useState } from 'react'
import { Alert, Box, Button, MenuItem, Paper, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { FilterBar } from '../../components/FilterBar'
import { PageHeader } from '../../components/PageHeader'
import { EmptyState } from '../../components/EmptyState'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { dateLabel, locationName, money } from '../purchases/contracts'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { operationTitle, stockError } from './contracts'
import type { OperationKind } from './contracts'
import { OperationForm } from './OperationForm'
import { PostedOperationLines } from './OperationLines'
import { useStockNotice } from './notices'
import { useOperation, useOperationMutations, useOperationRows, useOperations, useReasons, useStockLookups, useWriteOffCost } from './queries'

export function OperationListPage({ kind }: { kind: OperationKind }) {
  const [filters, setFilters] = useState({ from: '', to: '', status: '' }); const query = useOperations(kind, filters); const lookups = useStockLookups(); const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  const loc = (id?: string) => id ? locationName(id, lookups.data?.locations ?? []) : '—'
  return <Stack spacing={2}><Button component={Link} to="/stock" startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>До складу</Button><PageHeader title={operationTitle(kind)} actions={<Button component={Link} to={`/stock/${kind}/new`} variant="contained" startIcon={<Icon name="plus" />}>Створити {kind === 'transfers' ? 'переміщення' : 'списання'}</Button>} /><Paper variant="outlined"><FilterBar mobileSheet><TextField label="Період від" type="date" value={filters.from} onChange={e => setFilters({ ...filters, from: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} /><TextField label="Період до" type="date" value={filters.to} onChange={e => setFilters({ ...filters, to: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} /><TextField select label="Статус" value={filters.status} onChange={e => setFilters({ ...filters, status: e.target.value })} sx={{ minWidth: 180 }}><MenuItem value="">Усі</MenuItem><MenuItem value="DRAFT">Чернетки</MenuItem><MenuItem value="POSTED">Проведені</MenuItem></TextField></FilterBar></Paper><Paper variant="outlined">{query.isPending ? <ListLoading label="Завантаження складських документів" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : !query.data.length ? <EmptyState title="Документів ще немає" /> : mobile ? <Box component="ul" aria-label="Складські документи" sx={{ listStyle: 'none', p: 0, m: 0 }}>{query.data.map(row => <Stack component="li" key={row.document.id} spacing={1} sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider' }}><Button component={Link} to={`/stock/${kind}/${row.document.id}`} sx={{ alignSelf: 'flex-start', p: 0 }}>{row.document.number || 'DRAFT'}</Button><Typography>{dateLabel(row.document.documentDate)} · {loc(row.fromLocationId ?? row.storageLocationId)}</Typography>{kind === 'transfers' && <Typography>→ {loc(row.toLocationId)}</Typography>}<PurchaseStatus status={row.document.status} /></Stack>)}</Box> : <TableContainer><Table aria-label="Складські документи"><TableHead><TableRow>{['Номер', 'Дата', kind === 'transfers' ? 'Зі складу' : 'Склад', ...(kind === 'transfers' ? ['На склад'] : []), 'Рядків', 'Статус'].map(x => <TableCell key={x}>{x}</TableCell>)}</TableRow></TableHead><TableBody>{query.data.map(row => <TableRow key={row.document.id}><TableCell><Button component={Link} to={`/stock/${kind}/${row.document.id}`} sx={{ p: 0 }}>{row.document.number || 'DRAFT'}</Button></TableCell><TableCell>{dateLabel(row.document.documentDate)}</TableCell><TableCell>{loc(row.fromLocationId ?? row.storageLocationId)}</TableCell>{kind === 'transfers' && <TableCell>{loc(row.toLocationId)}</TableCell>}<TableCell>{row.lines.length}</TableCell><TableCell><PurchaseStatus status={row.document.status} /></TableCell></TableRow>)}</TableBody></Table></TableContainer>}</Paper></Stack>
}
export function OperationPage({ kind, newDocument = false }: { kind: OperationKind; newDocument?: boolean }) {
  const { id } = useParams(); return <OperationDetail key={`${kind}-${newDocument ? 'new' : id}`} kind={kind} id={newDocument ? undefined : id} />
}
function OperationDetail({ kind, id }: { kind: OperationKind; id?: string }) {
  const query = useOperation(kind, id); const rows = useOperationRows(kind, id); const lookups = useStockLookups(); const reasons = useReasons(kind === 'write-offs')
  const mutations = useOperationMutations(kind); const notify = useStockNotice(); const navigate = useNavigate(); const user = useCurrentUser().data
  const canCost = Boolean(user && hasAnyPermission(user, ['cost.view'])); const cost = useWriteOffCost(id, kind === 'write-offs' && canCost)
  const [confirm, setConfirm] = useState<'unpost' | 'delete' | null>(null); const [error, setError] = useState<unknown>(null); const [revision, setRevision] = useState(0)
  const busy = mutations.transition.isPending || mutations.remove.isPending
  const refresh = async () => { await query.refetch(); await rows.refetch(); setRevision(x => x + 1); setError(null) }
  if (id && query.isPending) return <ListLoading label="Завантаження складського документа" />
  if (id && query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const doc = id ? query.data : undefined; const posted = doc?.document.status === 'POSTED'
  const perform = async () => {
    if (!id || !confirm) return
    try { if (confirm === 'delete') { await mutations.remove.mutateAsync(id); notify({ severity: 'success', message: 'Чернетку видалено, резерв звільнено.' }); void navigate(`/stock/${kind}`, { replace: true }) } else { await mutations.transition.mutateAsync({ id, action: 'unpost' }); notify({ severity: 'success', message: 'Документ повернуто в чернетку.' }) } setError(null) }
    catch (caught) { setError(caught); notify({ severity: 'error', message: stockError(caught) }) } finally { setConfirm(null) }
  }
  const costSummary = kind === 'write-offs' && canCost && id && <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="subtitle1">Поточна собівартість списання</Typography>{cost.isPending ? <ListLoading rows={1} label="Завантаження собівартості списання" /> : cost.isError ? <RequestErrorState error={cost.error} retry={() => { void cost.refetch() }} /> : <Typography sx={{ mt: 1 }}>{money(cost.data.totalCostUah)} UAH</Typography>}<Typography variant="caption" color="text.secondary">Обчислено backend за збереженими рядками та поточною собівартістю партій.</Typography></Paper>
  return <Stack spacing={2}><Button component={Link} to={`/stock/${kind}`} startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>До списку</Button><PageHeader title={doc?.document.number || (id ? `Чернетка: ${operationTitle(kind).toLowerCase()}` : `Нове ${operationTitle(kind).toLowerCase()}`)} description={doc && <PurchaseStatus status={doc.document.status} />} />{error != null && <Alert severity="error" action={<Button disabled={busy} onClick={() => { void refresh() }}>Оновити дані</Button>}>{stockError(error)}</Alert>}
    {posted && doc ? <><Paper variant="outlined" sx={{ p: 2 }}><Stack spacing={2}><Typography>Дата: {dateLabel(doc.document.documentDate)}</Typography><Typography>{kind === 'transfers' ? 'Зі складу' : 'Склад'}: {locationName(doc.fromLocationId ?? doc.storageLocationId ?? '', lookups.data?.locations ?? [])}</Typography>{kind === 'transfers' ? <Typography>На склад: {locationName(doc.toLocationId ?? '', lookups.data?.locations ?? [])}</Typography> : <Typography>Причина: {reasons.data?.find(x => x.id === doc.writeOffReasonId)?.name || '—'}</Typography>}<Typography>Коментар: {doc.document.comment || '—'}</Typography><Typography>Внутрішня примітка: {doc.document.internalNote || '—'}</Typography></Stack></Paper><PostedOperationLines doc={doc} rows={rows.data ?? []} /></> : <OperationForm summary={costSummary} key={revision} kind={kind} doc={doc} rows={rows.data ?? []} refresh={id ? () => { void refresh() } : undefined} remove={id ? () => setConfirm('delete') : undefined} saved={result => { if (!id) void navigate(`/stock/${kind}/${result.document.id}`, { replace: true }) }} />}
    {rows.isError && <RequestErrorState error={rows.error} retry={() => { void rows.refetch() }} />}{posted && costSummary}
    {posted && <DocumentActions danger={<Button color="error" variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>Розпровести</Button>} />}
    <ConfirmDialog open={Boolean(confirm)} busy={busy} danger title={confirm === 'unpost' ? 'Розпровести документ?' : 'Видалити чернетку?'} action={confirm === 'unpost' ? 'Розпровести' : 'Видалити'} close={() => setConfirm(null)} confirm={() => { void perform() }}>{confirm === 'unpost' ? 'Складські рухи буде скасовано, чернетка знову резервуватиме товар. Backend перевірить залишки та активні резерви.' : 'Документ буде логічно видалено, його резерв звільниться.'}</ConfirmDialog>
  </Stack>
}
