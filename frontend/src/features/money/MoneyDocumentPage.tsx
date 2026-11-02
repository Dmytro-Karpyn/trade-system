import { useState } from 'react'
import { Alert, Box, Button, Paper, Stack, Typography } from '@mui/material'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName, dateLabel, money as amount } from '../purchases/contracts'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { effectiveRate, expensePaymentPrefill, moneyError, moneyRights, moneySingular, purposes } from './contracts'
import type { MoneyDocument, MoneyKind, MoneyLookups, MoneyValues } from './contracts'
import { MoneyForm } from './MoneyForm'
import { useMoneyNotice } from './notices'
import { useMoneyDocument, useMoneyLookups, useMoneyMutations } from './queries'

const displayAmount = (value: number | null) => value === null ? '—' : amount(value)

function ReadMoney({ doc, kind, lookups }: { doc: MoneyDocument; kind: MoneyKind; lookups?: MoneyLookups }) {
  const user = useCurrentUser().data; const currencyIds = lookups?.currencyIds ?? []; const source = lookups?.accounts.find(x => x.id === doc.sourceAccountId); const destination = lookups?.accounts.find(x => x.id === doc.destinationAccountId)
  const fields: [string, string][] = [['Дата документа', dateLabel(doc.document.documentDate)]]
  if (kind !== 'transfers') fields.push(['Призначення', doc.purpose ? purposes[doc.purpose] : 'Не вказано'])
  if (kind !== 'receipts') fields.push(['Рахунок списання', source?.name || 'Назву рахунку не завантажено'], ['Сума списання', `${displayAmount(doc.sourceAmount)} ${source ? currencyName(source.currencyId, currencyIds) : ''}`])
  if (kind !== 'payments') fields.push(['Рахунок зарахування', destination?.name || 'Назву рахунку не завантажено'], ['Сума зарахування', `${displayAmount(doc.destinationAmount)} ${destination ? currencyName(destination.currencyId, currencyIds) : ''}`])
  if (doc.purpose === 'SETTLEMENT') fields.push(['Контрагент', lookups?.counterparties.find(x => x.id === doc.counterpartyId)?.name || 'Назву контрагента не завантажено'], ['Валюта взаєморозрахунку', doc.settlementCurrencyId ? currencyName(doc.settlementCurrencyId, currencyIds) : '—'], ['Сума взаєморозрахунку', displayAmount(doc.settlementAmount)], ['Курс взаєморозрахунку', String(doc.settlementExchangeRate ?? '—')])
  const rate = kind === 'transfers' && source && destination && source.currencyId !== destination.currencyId ? effectiveRate(doc.sourceAmount, doc.destinationAmount) : null
  if (rate !== null && source && destination) fields.push(['Ефективний курс', `1 ${currencyName(source.currencyId, currencyIds)} = ${new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 8 }).format(rate)} ${currencyName(destination.currencyId, currencyIds)}`])
  fields.push(['Коментар', doc.document.comment || '—'], ['Внутрішня примітка', doc.document.internalNote || '—'])
  return <Paper variant="outlined" sx={{ p: 2 }}><Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'minmax(130px, 0.8fr) minmax(0, 1.2fr)' }, gap: 2 }}>{fields.map(([label, value]) => <Box key={label} sx={{ display: 'contents' }}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" variant="body2" sx={{ m: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{value}</Typography></Box>)}</Box>{doc.purpose === 'SETTLEMENT' && doc.counterpartyId && user && hasAnyPermission(user, ['settlement.view']) && <Button component={Link} to={`/settlements/${doc.counterpartyId}`} sx={{ mt: 2 }}>Відкрити взаєморозрахунки контрагента</Button>}</Paper>
}
export function MoneyDocumentPage({ kind, newDocument = false }: { kind: MoneyKind; newDocument?: boolean }) { const { id } = useParams(); const location = useLocation(); const prefill = kind === 'payments' && newDocument ? expensePaymentPrefill(location.state) : undefined; return <MoneyDetail key={`${kind}-${newDocument ? 'new' : id}`} kind={kind} id={newDocument ? undefined : id} prefill={prefill} /> }
export function MoneyDetail({ kind, id, prefill, embedded = false, onDirtyChange, onDeleted }: { embedded?: boolean; onDirtyChange?: (dirty: boolean) => void; onDeleted?: () => void; kind: MoneyKind; id?: string; prefill?: Partial<MoneyValues> }) {
  const query = useMoneyDocument(kind, id); const lookups = useMoneyLookups(); const user = useCurrentUser().data; const navigate = useNavigate(); const notify = useMoneyNotice(); const mutations = useMoneyMutations(kind)
  const [created, setCreated] = useState<MoneyDocument | undefined>(); const [revision, setRevision] = useState(0); const [confirm, setConfirm] = useState<'unpost' | 'delete' | null>(null); const [error, setErrorState] = useState<unknown>(null)
  const canWrite = Boolean(user && hasAnyPermission(user, [moneyRights[kind]])); const canRead = Boolean(user && hasAnyPermission(user, ['money.view']))
  const refresh = async () => { await query.refetch(); setRevision(x => x + 1); setErrorState(null) }
  if (id && query.isPending) return <ListLoading label="Завантаження грошового документа" />
  if (id && query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const doc = id ? query.data : created; const posted = doc?.document.status === 'POSTED'; const busy = mutations.transition.isPending || mutations.remove.isPending
  const perform = async () => {
    if (!doc || !confirm) return
    try {
      if (confirm === 'delete') { await mutations.remove.mutateAsync(doc.document.id); notify({ severity: 'success', message: 'Чернетку логічно видалено.' }); if (onDeleted) onDeleted(); else if (canRead) void navigate(`/money/${kind}`, { replace: true }); else { setCreated(undefined); setRevision(x => x + 1) } }
      else { const result = await mutations.transition.mutateAsync({ id: doc.document.id, action: 'unpost' }); if (!id) setCreated(result); notify({ severity: 'success', message: 'Документ повернуто в чернетку.' }) }
      setErrorState(null)
    } catch (caught) { setErrorState(caught); notify({ severity: 'error', message: moneyError(caught) }) } finally { setConfirm(null) }
  }
  return <Stack spacing={2}><Button component={Link} to={embedded ? `/money/${kind}/${id}` : canRead ? `/money/${kind}` : '/money'} startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>{embedded ? 'Відкрити окремо' : 'До списку'}</Button><PageHeader secondary={embedded} title={doc?.document.number || `${doc ? 'Чернетка' : 'Новий документ'}: ${moneySingular[kind].toLowerCase()}`} description={doc && <PurchaseStatus status={doc.document.status} />} />{error != null && <Alert severity="error" action={id && <Button onClick={() => { void refresh() }}>Оновити дані</Button>}>{moneyError(error)}</Alert>}
    {(!posted && canWrite) ? <MoneyForm onDirtyChange={onDirtyChange} key={revision} kind={kind} money={doc} prefill={prefill} refresh={id ? () => { void refresh() } : undefined} remove={doc ? () => setConfirm('delete') : undefined} saved={result => { if (!id && canRead) void navigate(`/money/${kind}/${result.document.id}`, { replace: true }); else if (!id) setCreated(result) }} /> : doc ? <ReadMoney doc={doc} kind={kind} lookups={lookups.data} /> : null}
    {lookups.isError && <RequestErrorState error={lookups.error} retry={() => { void lookups.refetch() }} />}
    {posted && canWrite && <DocumentActions danger={<Button color="error" variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>Розпровести</Button>} />}
    <ConfirmDialog open={Boolean(confirm)} busy={busy} danger title={confirm === 'delete' ? 'Видалити чернетку?' : 'Розпровести грошовий документ?'} action={confirm === 'delete' ? 'Видалити' : 'Розпровести'} close={() => setConfirm(null)} confirm={() => { void perform() }}>{confirm === 'delete' ? 'Документ буде логічно видалено. Бізнес-дані збережуться.' : 'Грошові рухи та пов’язаний взаєморозрахунок буде скасовано. Документ повернеться в чернетку.'}</ConfirmDialog>
  </Stack>
}
