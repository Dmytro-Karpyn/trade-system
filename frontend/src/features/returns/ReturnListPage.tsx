import { useState } from 'react'
import { Box, Button, MenuItem, Paper, Stack, TextField, Typography, useMediaQuery, useTheme } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { CurrencyAmount } from '../../components/CurrencyAmount'
import { DataTable } from '../../components/DataTable'
import { FilterBar } from '../../components/FilterBar'
import { PageHeader } from '../../components/PageHeader'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { useDetailSelection } from '../../components/useDetailSelection'
import { ReturnDetail } from './ReturnDetailPage'
import { DocumentLink } from '../../components/DocumentLink'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName, dateLabel, money } from '../purchases/contracts'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { returnConfig, returnFilters } from './contracts'
import type { ReturnKind, ReturnListItem } from './contracts'
import { useReturnLookups, useReturns } from './queries'

export function ReturnList({ kind, rows, selectedId, select, compact = false }: { kind: ReturnKind; rows: ReturnListItem[]; selectedId?: string | null; select?: (id: string) => void; compact?: boolean }) {
  const config = returnConfig[kind]; const mobile = useMediaQuery(useTheme().breakpoints.down('md')); const currencyIds = [...new Set(rows.map(r => r.currencyId))]
  if (!rows.length) return <EmptyState title="Повернень ще немає" description="Створіть повернення з проведеної накладної або змініть фільтри." />
  const link = (row: ReturnListItem) => <DocumentLink id={row.sourceId} type={kind === 'customer' ? 'SALE_INVOICE' : 'PURCHASE_INVOICE'} number={row.sourceNumber} />
  return mobile ? <Box component="ul" aria-label={config.title} sx={{ listStyle: 'none', p: 0, m: 0 }}>{rows.map(r => <Stack component="li" key={r.id} spacing={1.5} sx={{ p: 2.5, borderBottom: '1px solid', borderColor: 'divider' }}><Button component={Link} to={`${config.root}/${r.id}`} sx={{ alignSelf: 'flex-start', p: 0 }}>{r.number || 'Чернетка'}</Button><Typography variant="body2" color="text.secondary">{dateLabel(r.documentDate)}</Typography><Typography>{r.counterpartyName}</Typography><Typography variant="body2">{config.source}: {link(r)}</Typography><Typography variant="subtitle1">{money(r.totalRefund)} {currencyName(r.currencyId, currencyIds)}</Typography><Box><PurchaseStatus status={r.status} /></Box></Stack>)}</Box>
    : <DataTable label={config.title} compact={compact} rows={rows} rowId={row => row.id} selectedId={selectedId} select={select ? row => select(row.id) : undefined} columns={[
      { key: 'number', label: 'Номер', width: compact ? 115 : undefined, render: row => <Box><Button component={Link} to={`${config.root}/${row.id}`} sx={{ p: 0, minWidth: 0 }} onClick={event => { if (select && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); select(row.id) } }}>{row.number || 'Чернетка'}</Button><Typography variant="caption" sx={{ display: 'block' }} color="text.secondary">{dateLabel(row.documentDate)}</Typography></Box> },
      { key: 'party', label: config.party, render: row => <Box>{row.counterpartyName}{compact && <Typography variant="caption" sx={{ display: 'block' }}>{link(row)}</Typography>}</Box> },
      ...(!compact ? [{ key: 'source', label: config.source, render: link }] : []),
      { key: 'amount', label: 'Сума', align: 'right', width: compact ? 115 : undefined, render: row => <CurrencyAmount amount={row.totalRefund} currency={currencyName(row.currencyId, currencyIds)} /> },
      { key: 'status', label: 'Статус', width: compact ? 98 : undefined, render: row => <PurchaseStatus status={row.status} /> },
      ...(!compact ? [{ key: 'open', label: 'Дії', render: (row: ReturnListItem) => <Button component={Link} to={`${config.root}/${row.id}`}>Відкрити</Button> }] : []),
    ]} />
}
export function ReturnListPage({ kind }: { kind: ReturnKind }) {
  const config = returnConfig[kind]; const user = useCurrentUser().data; const [filters, setFilters] = useState(returnFilters); const query = useReturns(kind, filters); const lookups = useReturnLookups(kind)
  const split = useMediaQuery(useTheme().breakpoints.up('lg')), selection = useDetailSelection()
  const parties = [...new Map([...(lookups.data?.sources.map(s => [s.counterpartyId, s.counterpartyName] as const) ?? []), ...(query.data?.map(r => [r.counterpartyId, r.counterpartyName] as const) ?? [])]).entries()]
  const sources = [...new Map([...(lookups.data?.sources.map(s => [s.id, s.number] as const) ?? []), ...(query.data?.map(r => [r.sourceId, r.sourceNumber] as const) ?? [])]).entries()]
  const content = <Stack spacing={2}><PageHeader title={config.title} actions={user && hasAnyPermission(user, [`${config.permission}.create`]) && <Button component={Link} to={`${config.root}/new`} variant="contained">+ {config.create}</Button>} /><Paper variant="outlined"><FilterBar mobileSheet><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', xl: 'repeat(3, 1fr)' }, gap: 2 }}>{(['from', 'to'] as const).map(key => <TextField key={key} type="date" label={key === 'from' ? 'Період від' : 'Період до'} value={filters[key]} onChange={e => setFilters({ ...filters, [key]: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />)}<TextField select label={config.party} value={filters.counterpartyId} onChange={e => setFilters({ ...filters, counterpartyId: e.target.value })}><MenuItem value="">Усі</MenuItem>{parties.map(([id, name]) => <MenuItem key={id} value={id}>{name}</MenuItem>)}</TextField><TextField select label="Статус" value={filters.status} onChange={e => setFilters({ ...filters, status: e.target.value })}><MenuItem value="">Усі</MenuItem><MenuItem value="DRAFT">Чернетки</MenuItem><MenuItem value="POSTED">Проведені</MenuItem></TextField><TextField select label={config.source} value={filters.sourceId} onChange={e => setFilters({ ...filters, sourceId: e.target.value })}><MenuItem value="">Усі</MenuItem>{sources.map(([id, number]) => <MenuItem key={id} value={id}>{number || 'Без номера'}</MenuItem>)}</TextField></Box></FilterBar></Paper>{lookups.isError && <RequestErrorState error={lookups.error} retry={() => { void lookups.refetch() }} />}<Paper variant="outlined">{query.isPending ? <ListLoading label="Завантаження повернень" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : <ReturnList kind={kind} rows={query.data} compact={split} selectedId={selection.id} select={split ? selection.select : undefined} />}</Paper></Stack>
  return <><SplitViewLayout split={split} list={content} detailLabel="Вибране повернення" detail={selection.id ? <ReturnDetail key={`${kind}-${selection.id}`} kind={kind} selectedId={selection.id} embedded onDirtyChange={selection.dirtyChanged} onDeleted={selection.deleted} /> : <Paper variant="outlined"><EmptyState icon="purchase" title="Оберіть повернення" /></Paper>} /><ConfirmDialog open={selection.pending !== null} title="Незбережені зміни" action="Відкинути зміни" danger close={selection.cancel} confirm={selection.discard}>Відкинути незбережені зміни та відкрити інше повернення?</ConfirmDialog></>
}
