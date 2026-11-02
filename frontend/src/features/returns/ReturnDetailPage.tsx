import { PageHeader } from '../../components/PageHeader'
import { useState } from 'react'
import { Alert, Box, Button, Paper, Stack, Typography } from '@mui/material'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { useQueryClient } from '@tanstack/react-query'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { DocumentLink } from '../../components/DocumentLink'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName, dateLabel, locationName, money } from '../purchases/contracts'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { returnConfig, returnError } from './contracts'
import type { ReturnDocument, ReturnKind, ReturnLookups } from './contracts'
import { ReturnForm } from './ReturnForm'
import { ReturnLines } from './ReturnLines'
import { useReturn, useReturnable, useReturnLookups, useReturnMutations, useReturnRefund } from './queries'
import { useReturnNotice } from './notices'

function ReadReturn({ kind, doc, lookups, compact = false }: { compact?: boolean; kind: ReturnKind; doc: ReturnDocument; lookups: ReturnLookups }) {
  const config = returnConfig[kind]; const source = lookups.sources.find(s => s.id === doc.sourceId); const lines = useReturnable(kind, doc.sourceId, doc.storageLocationId); const refund = useReturnRefund(kind, doc.document.id, true)
  const values = [['Дата', dateLabel(doc.document.documentDate)], [config.party, source?.counterpartyName ?? '—'], ['Склад', locationName(doc.storageLocationId, lookups.locations)], ['Коментар', doc.document.comment || '—'], ['Внутрішня примітка', doc.document.internalNote || '—']]
  return <Stack spacing={2}><Paper variant="outlined" sx={{ p: 2 }}><Box component="dl" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2, m: 0 }}>{values.map(([label, value]) => <Box key={label}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" sx={{ m: 0, mt: 0.5, overflowWrap: 'anywhere' }}>{value}</Typography></Box>)}</Box><Typography variant="body2" sx={{ mt: 2.5 }}>{config.source}: <DocumentLink id={doc.sourceId} type={kind === 'customer' ? 'SALE_INVOICE' : 'PURCHASE_INVOICE'} number={source?.number} /></Typography></Paper>{lines.isPending ? <ListLoading label="Завантаження рядків повернення" /> : lines.isError ? <RequestErrorState error={lines.error} retry={() => { void lines.refetch() }} /> : <ReturnLines compact={compact} kind={kind} rows={lines.data.filter(r => doc.lines.some(l => l.sourceLineId === r.sourceLineId))} lookups={lookups} readonlyQuantities={new Map(doc.lines.map(l => [l.sourceLineId, l.quantity]))} refunds={new Map(refund.data?.lines.map(l => [l.sourceLineId, l.refundAmount]))} />}{refund.isError ? <RequestErrorState error={refund.error} retry={() => { void refund.refetch() }} /> : <Typography variant="h2">Загальна сума компенсації: {refund.data ? money(refund.data.totalRefund) : '…'} {source ? currencyName(source.currencyId, lookups.sources.map(s => s.currencyId)) : ''}</Typography>}</Stack>
}
export function ReturnDetail({ kind, newDocument, selectedId, embedded = false, onDirtyChange, onDeleted }: { kind: ReturnKind; newDocument?: boolean; selectedId?: string; embedded?: boolean; onDirtyChange?: (dirty: boolean) => void; onDeleted?: () => void }) {
  const { id: routeId = '' } = useParams(); const [params] = useSearchParams(); const config = returnConfig[kind]; const user = useCurrentUser().data; const notify = useReturnNotice(); const navigate = useNavigate(); const client = useQueryClient()
  const [created, setCreated] = useState<ReturnDocument | undefined>(); const id = newDocument ? created?.document.id ?? '' : selectedId ?? routeId
  const query = useReturn(kind, newDocument ? '' : id); const lookups = useReturnLookups(kind); const mutations = useReturnMutations(kind)
  const [revision, setRevision] = useState(0); const [confirm, setConfirm] = useState<'post' | 'unpost' | 'delete' | null>(null); const [error, setError] = useState<unknown>(null)
  const doc = newDocument ? created : query.data; const posted = doc?.document.status === 'POSTED'; const busy = mutations.transition.isPending || mutations.remove.isPending
  const refresh = async () => { await Promise.all([query.refetch(), lookups.refetch(), client.invalidateQueries({ queryKey: ['returns', kind, 'returnable'] }), client.invalidateQueries({ queryKey: ['returns', kind, 'refund'] })]); setRevision(v => v + 1); setError(null) }
  if (!user) return null
  if ((!newDocument && query.isPending) || lookups.isPending) return <ListLoading label="Завантаження повернення" />
  if (!newDocument && query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  if (lookups.isError) return <RequestErrorState error={lookups.error} retry={() => { void lookups.refetch() }} />
  const canCreate = hasAnyPermission(user, [`${config.permission}.create`]); const canRead = hasAnyPermission(user, [`${config.permission}.view`]); const canPost = !posted && hasAnyPermission(user, [`${config.permission}.post`]); const canDelete = Boolean(doc && !posted && hasAnyPermission(user, [`${config.permission}.delete`])); const canUnpost = posted && hasAnyPermission(user, [`${config.permission}.unpost`])
  const initial = params.get(config.sourceKey) ?? ''; const invalidPrefill = Boolean(initial && !z.guid().safeParse(initial).success)
  const perform = async () => {
    if (!confirm || !doc) return; setError(null)
    try { if (confirm === 'delete') { await mutations.remove.mutateAsync(doc.document.id); notify({ severity: 'success', message: 'Чернетку повернення видалено.' }); if (onDeleted) onDeleted(); else void navigate(config.root, { replace: true }) } else { const result = await mutations.transition.mutateAsync({ id: doc.document.id, action: confirm }); if (newDocument) setCreated(result); setRevision(v => v + 1); notify({ severity: 'success', message: confirm === 'post' ? 'Повернення проведено.' : 'Повернення повернуто в чернетку.' }) } }
    catch (caught) { setError(caught); notify({ severity: 'error', message: returnError(caught) }) } finally { setConfirm(null) }
  }
  return <Stack spacing={2}>{canRead && <Button component={Link} to={embedded ? `${config.root}/${id}` : config.root} sx={{ alignSelf: 'flex-start', p: 0 }}>{embedded ? 'Відкрити окремо' : 'До списку повернень'}</Button>}<PageHeader secondary={embedded} title={doc?.document.number || `${doc ? 'Чернетка:' : 'Нове'} ${config.singular.toLocaleLowerCase('uk-UA')}`} description={<PurchaseStatus status={posted ? 'POSTED' : 'DRAFT'} />} />{error != null && <Alert severity="error" action={<Button onClick={() => { void refresh() }}>Оновити дані</Button>}>{returnError(error)}</Alert>}{invalidPrefill && <Alert severity="warning">Невірне посилання на документ-підставу. Виберіть документ у формі.</Alert>}{!posted && canCreate ? <ReturnForm compact={embedded} onDirtyChange={onDirtyChange} key={revision} kind={kind} doc={doc} lookups={lookups.data} initialSourceId={invalidPrefill ? undefined : initial} saved={result => { if (newDocument && canRead) void navigate(`${config.root}/${result.document.id}`, { replace: true }); else if (newDocument && result.document.status === 'POSTED' && !canRead) void navigate('/', { replace: true }); else if (newDocument) setCreated(result) }} refresh={!newDocument ? () => { void refresh() } : undefined} remove={canDelete ? () => setConfirm('delete') : undefined} /> : doc && <ReadReturn compact={embedded} kind={kind} doc={doc} lookups={lookups.data} />}
    {kind === 'supplier' && (posted || !canCreate) && <Alert severity="info">{posted ? 'Проведено: фактичне списання товару зі складу.' : 'Чернетка: товар зарезервовано; складські рухи ще не створені.'}</Alert>}
    {(!canCreate || posted) && <DocumentActions danger={(canUnpost || canDelete) && <>{canUnpost && <Button color="error" variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>Розпровести</Button>}{canDelete && <Button color="error" disabled={busy} onClick={() => setConfirm('delete')}>Видалити</Button>}</>}>{canPost && <Button variant="contained" disabled={busy} onClick={() => setConfirm('post')}>Провести</Button>}</DocumentActions>}
    <ConfirmDialog open={Boolean(confirm)} busy={busy} danger={confirm !== 'post'} title={confirm === 'post' ? 'Провести повернення?' : confirm === 'unpost' ? 'Розпровести повернення?' : 'Видалити чернетку повернення?'} action={confirm === 'post' ? 'Провести' : confirm === 'unpost' ? 'Розпровести' : 'Видалити'} close={() => setConfirm(null)} confirm={() => { void perform() }}>{confirm === 'post' ? config.confirmation : confirm === 'unpost' ? `Рухи складу та взаєморозрахунків буде скасовано.${kind === 'supplier' ? ' Чернетка знову резервуватиме товар.' : ''}` : 'Документ буде логічно видалено. Бізнес-дані збережуться.'}</ConfirmDialog>
  </Stack>
}
export function ReturnDetailPage({ kind, newDocument }: { kind: ReturnKind; newDocument?: boolean }) { const { id = '' } = useParams(); return <ReturnDetail key={`${kind}-${newDocument ? 'new' : id}`} kind={kind} newDocument={newDocument} /> }
