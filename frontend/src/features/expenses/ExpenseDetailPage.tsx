import { useState } from 'react'
import { Alert, Box, Button, DialogContentText, Paper, Stack, Typography } from '@mui/material'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { PageHeader } from '../../components/PageHeader'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { partyReadPermissions } from '../counterparties/contracts'
import { currencyName, dateLabel, money } from '../purchases/contracts'
import { PurchaseStatus } from '../purchases/PurchaseStatus'
import { costWarning, expenseAmountUah, expenseError } from './contracts'
import type { Expense, ExpenseLookups } from './contracts'
import { ExpenseCostImpact } from './ExpenseCostImpact'
import { ExpenseForm, ExpensePurchaseCard } from './ExpenseForm'
import { useExpenseNotice } from './notices'
import { useExpense, useExpenseLookups, useExpenseMutations } from './queries'

function ReadExpense({ expense, lookups }: { expense: Expense; lookups?: ExpenseLookups }) {
  const user = useCurrentUser().data; const currencies = lookups?.currencyIds ?? []
  const fields: [string, React.ReactNode][] = [['Дата документа', dateLabel(expense.document.documentDate)], ['Контрагент', user && hasAnyPermission(user, partyReadPermissions) ? <Link to={`/counterparties/${expense.counterpartyId}`}>{lookups?.counterparties.find(p => p.id === expense.counterpartyId)?.name || 'Контрагент'}</Link> : lookups?.counterparties.find(p => p.id === expense.counterpartyId)?.name || '—'], ['Категорія', lookups?.categories.find(c => c.id === expense.expenseCategoryId)?.name || '—'], ['Тип', expense.purchaseInvoiceId ? 'Закупівельна витрата' : 'Загальна витрата'], ['Валюта', currencyName(expense.currencyId, currencies)], ['Сума витрати', money(expense.amount)], ['Курс до UAH', String(expense.exchangeRate)], ['Еквівалент у гривні', `${money(expenseAmountUah(expense.amount, expense.exchangeRate) ?? 0)} UAH`], ['Коментар', expense.document.comment || '—'], ['Внутрішня примітка', expense.document.internalNote || '—']]
  const purchase = lookups?.purchases.find(p => p.id === expense.purchaseInvoiceId)
  return <Stack spacing={2}><Paper variant="outlined" sx={{ p: 2 }}><Box component="dl" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'minmax(130px, 0.8fr) minmax(0, 1.2fr)' }, gap: 2, m: 0 }}>{fields.map(([label, value]) => <Box key={label} sx={{ display: 'contents' }}><Typography component="dt" variant="body2" color="text.secondary">{label}</Typography><Typography component="dd" variant="body2" sx={{ m: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{value}</Typography></Box>)}</Box></Paper>{purchase && <ExpensePurchaseCard purchase={purchase} currencies={currencies} />}</Stack>
}
export function ExpenseDetailPage({ newDocument = false }: { newDocument?: boolean }) { const { id } = useParams(); const [params] = useSearchParams(); const rawPurchase = params.get('purchaseInvoiceId') ?? ''; const purchaseId = z.guid().safeParse(rawPurchase).success ? rawPurchase : ''; return <ExpenseDetail key={newDocument ? 'new' : id} id={newDocument ? undefined : id} purchaseId={newDocument ? purchaseId : ''} invalidPrefill={Boolean(newDocument && rawPurchase && !purchaseId)} /> }
export function ExpenseDetail({ id, purchaseId = '', invalidPrefill = false, embedded = false, onDirtyChange, onDeleted }: { id?: string; purchaseId?: string; invalidPrefill?: boolean; embedded?: boolean; onDirtyChange?: (dirty: boolean) => void; onDeleted?: () => void }) {
  const query = useExpense(id); const lookups = useExpenseLookups(); const user = useCurrentUser().data; const mutations = useExpenseMutations(); const navigate = useNavigate(); const notify = useExpenseNotice()
  const [created, setCreated] = useState<Expense | undefined>(); const [revision, setRevision] = useState(0); const [confirm, setConfirm] = useState<'post' | 'unpost' | 'delete' | null>(null); const [error, setError] = useState<unknown>(null)
  const canRead = Boolean(user && hasAnyPermission(user, ['expense.view'])); const canCreate = Boolean(user && hasAnyPermission(user, ['expense.create'])); const canCost = Boolean(user && hasAnyPermission(user, ['cost.view']))
  const busy = mutations.transition.isPending || mutations.remove.isPending
  const refresh = async () => { await query.refetch(); setRevision(x => x + 1); setError(null) }
  if (id && query.isPending) return <ListLoading label="Завантаження витрати" />
  if (id && query.isError) return <RequestErrorState error={query.error} retry={() => { void query.refetch() }} />
  const doc = id ? query.data : created; const posted = doc?.document.status === 'POSTED'
  const canPost = Boolean(user && !posted && hasAnyPermission(user, ['expense.post'])); const canUnpost = Boolean(user && posted && hasAnyPermission(user, ['expense.unpost'])); const canDelete = Boolean(user && doc && !posted && hasAnyPermission(user, ['expense.delete']))
  const perform = async () => {
    if (!doc || !confirm) return
    try { if (confirm === 'delete') { await mutations.remove.mutateAsync(doc.document.id); notify({ severity: 'success', message: 'Чернетку витрати видалено.' }); if (onDeleted) onDeleted(); else if (canRead) void navigate('/expenses', { replace: true }); else { setCreated(undefined); setRevision(x => x + 1) } } else { const result = await mutations.transition.mutateAsync({ id: doc.document.id, action: confirm }); if (!id) setCreated(result); notify({ severity: 'success', message: confirm === 'post' ? 'Витрату проведено.' : 'Витрату повернуто в чернетку.' }) } setError(null) }
    catch (caught) { setError(caught); notify({ severity: 'error', message: expenseError(caught) }) } finally { setConfirm(null) }
  }
  return <Stack spacing={2}><Button component={Link} to={embedded ? `/expenses/${id}` : '/expenses'} startIcon={<Icon name="back" />} sx={{ alignSelf: 'flex-start' }}>{embedded ? 'Відкрити окремо' : 'До витрат'}</Button><PageHeader secondary={embedded} title={doc?.document.number || (doc ? 'Чернетка витрати' : 'Нова витрата')} description={doc && <PurchaseStatus status={doc.document.status} />} />
    {invalidPrefill && <Alert severity="warning">Невірне посилання на закупівлю. Виберіть закупівлю у формі.</Alert>}
    {error != null && <Alert severity="error" action={id && <Button onClick={() => { void refresh() }}>Оновити дані</Button>}>{expenseError(error)}</Alert>}
    {(posted || !canCreate) && doc?.purchaseInvoiceId && <Alert severity="info"><Typography variant="subtitle1">Впливає на собівартість</Typography>{posted ? costWarning : 'Пряма закупівельна витрата. Поточну собівартість розраховує backend.'}</Alert>}
    {!posted && canCreate ? <ExpenseForm summary={doc?.purchaseInvoiceId && canCost && <ExpenseCostImpact id={doc.document.id} currencyIds={lookups.data?.currencyIds} />} onDirtyChange={onDirtyChange} key={revision} expense={doc} initialPurchaseId={purchaseId} refresh={id ? () => { void refresh() } : undefined} remove={canDelete ? () => setConfirm('delete') : undefined} saved={result => { if (!id && canRead) void navigate(`/expenses/${result.document.id}`, { replace: true }); else if (!id) setCreated(result) }} /> : doc ? <ReadExpense expense={doc} lookups={lookups.data} /> : null}
    {(posted || !canCreate) && doc?.purchaseInvoiceId && canCost && <ExpenseCostImpact id={doc.document.id} currencyIds={lookups.data?.currencyIds} />}
    {lookups.isError && <RequestErrorState error={lookups.error} retry={() => { void lookups.refetch() }} />}
    {(!canCreate || posted) && <DocumentActions danger={(canUnpost || canDelete) && <>{canUnpost && <Button color="error" variant="outlined" disabled={busy} onClick={() => setConfirm('unpost')}>Розпровести</Button>}{canDelete && <Button color="error" disabled={busy} onClick={() => setConfirm('delete')}>Видалити</Button>}</>}>{canPost && doc && <Button variant="contained" disabled={busy} onClick={() => setConfirm('post')}>Провести</Button>}{posted && doc && user && hasAnyPermission(user, ['money.payment']) && <Button component={Link} to="/money/payments/new" variant="outlined" state={{ expensePaymentPrefill: { counterpartyId: doc.counterpartyId, currencyId: doc.currencyId, amount: doc.amount, exchangeRate: doc.exchangeRate } }}>Створити виплату</Button>}</DocumentActions>}
    <ConfirmDialog open={Boolean(confirm)} busy={busy} danger={confirm !== 'post'} title={confirm === 'post' ? 'Провести витрату?' : confirm === 'unpost' ? 'Розпровести витрату?' : 'Видалити чернетку витрати?'} action={confirm === 'post' ? 'Провести' : confirm === 'unpost' ? 'Розпровести' : 'Видалити'} close={() => setConfirm(null)} confirm={() => { void perform() }}><DialogContentText>{confirm === 'post' ? 'Після проведення буде створено заборгованість перед контрагентом.' : confirm === 'unpost' ? 'Взаєморозрахунок цієї витрати буде скасовано. Документ повернеться в чернетку.' : 'Документ буде логічно видалено. Бізнес-дані збережуться.'}</DialogContentText>{confirm === 'post' && doc?.purchaseInvoiceId && <DialogContentText sx={{ mt: 2 }}>Витрата також буде включена до поточної собівартості партій цієї закупівлі.</DialogContentText>}{confirm === 'unpost' && doc?.purchaseInvoiceId && <DialogContentText sx={{ mt: 2 }}>{costWarning}</DialogContentText>}</ConfirmDialog>
  </Stack>
}
