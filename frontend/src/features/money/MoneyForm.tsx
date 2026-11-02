import { useEffect, useState } from 'react'
import { Alert, Box, Button, DialogContentText, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName } from '../purchases/contracts'
import { effectiveRate, moneyDefaults, moneyError, moneyFormSchema, moneyInput, purposes, uahId } from './contracts'
import type { MoneyDocument, MoneyKind, MoneyValues } from './contracts'
import { useMoneyNotice } from './notices'
import { useMoneyLookups, useMoneyMutations } from './queries'

export function MoneyForm({ kind, money, prefill, saved, refresh, remove, onDirtyChange }: { onDirtyChange?: (dirty: boolean) => void; kind: MoneyKind; money?: MoneyDocument; prefill?: Partial<MoneyValues>; saved: (result: MoneyDocument) => void; refresh?: () => void; remove?: () => void }) {
  const lookups = useMoneyLookups(); const accounts = lookups.data?.accounts ?? []; const user = useCurrentUser().data
  const [baseline, setBaseline] = useState(money); const [confirm, setConfirm] = useState<MoneyValues | null>(null); const [error, setErrorState] = useState<unknown>(null)
  const mutations = useMoneyMutations(kind); const notify = useMoneyNotice()
  const { control, register, setValue, reset, handleSubmit, setError, formState: { errors, isDirty } } = useForm<MoneyValues>({ resolver: zodResolver(moneyFormSchema(kind, accounts, Boolean(baseline && baseline.purpose === null))), defaultValues: { ...moneyDefaults(kind, money), ...(!money ? prefill : {}) } })
  useEffect(() => { onDirtyChange?.(isDirty) }, [isDirty, onDirtyChange])
  const values = useWatch({ control }) as MoneyValues
  const busy = mutations.save.isPending || mutations.transition.isPending
  const currencies = [...new Set([...(lookups.data?.currencyIds ?? [uahId]), ...(values.settlementCurrencyId ? [values.settlementCurrencyId] : [])])]
  const source = accounts.find(a => a.id === values.sourceAccountId); const destination = accounts.find(a => a.id === values.destinationAccountId)
  const rate = kind === 'transfers' && source && destination && source.currencyId !== destination.currencyId ? effectiveRate(values.sourceAmount, values.destinationAmount) : null
  const persist = async (input: MoneyValues, post: boolean) => {
    let result = baseline; setErrorState(null)
    try {
      if (!result || isDirty) { result = await mutations.save.mutateAsync({ id: result?.document.id, input: moneyInput(kind, input, baseline) }); setBaseline(result); reset(moneyDefaults(kind, result)) }
      if (!result) return
      if (post) result = await mutations.transition.mutateAsync({ id: result.document.id, action: 'post' })
      notify({ severity: 'success', message: post ? 'Грошовий документ проведено.' : 'Чернетку збережено.' }); saved(result)
    } catch (caught) {
      setErrorState(caught); notify({ severity: 'error', message: moneyError(caught) })
      if (caught instanceof ApiError) for (const [key, messages] of Object.entries(caught.fieldErrors)) {
        const field = key.replace(/^\$\./, '').replace(/^[A-Z]/, x => x.toLowerCase())
        if (/^(documentDate|purpose|sourceAccountId|destinationAccountId|sourceAmount|destinationAmount|counterpartyId|settlementCurrencyId|settlementAmount|settlementExchangeRate)$/.test(field)) setError(field as 'sourceAmount', { message: messages.join(' ') })
      }
      if (!money && result) saved(result)
    } finally { setConfirm(null) }
  }
  const submit = handleSubmit(input => persist(input, false)); const askPost = handleSubmit(input => setConfirm(input))
  const amount = (field: 'sourceAmount' | 'destinationAmount' | 'settlementAmount' | 'settlementExchangeRate', label: string, helper?: string, fixed = false) => <Controller name={field} control={control} render={({ field: input }) => <TextField label={label} type="number" required fullWidth value={input.value ?? ''} inputRef={input.ref} onBlur={input.onBlur} onChange={event => input.onChange(event.target.value === '' ? null : Number(event.target.value))} disabled={busy || fixed} error={Boolean(errors[field])} helperText={errors[field]?.message ?? helper} slotProps={{ htmlInput: { min: field === 'settlementExchangeRate' ? '0.00000001' : '0.0001', step: field === 'settlementExchangeRate' ? '0.00000001' : '0.0001' } }} />} />
  const account = (field: 'sourceAccountId' | 'destinationAccountId', label: string) => <Controller name={field} control={control} render={({ field: input }) => <TextField {...input} select label={label} required disabled={busy || lookups.isPending || lookups.isError} error={Boolean(errors[field])} helperText={errors[field]?.message}><MenuItem value="">Виберіть рахунок</MenuItem>{accounts.map(a => <MenuItem value={a.id} key={a.id}>{a.name} · {currencyName(a.currencyId, currencies)}{a.active ? '' : ' (неактивний)'}</MenuItem>)}</TextField>} />
  return <><Stack component="form" noValidate onSubmit={event => { void submit(event) }} spacing={2}>
    {error != null && <Alert severity="error" action={refresh && <Button disabled={busy} onClick={refresh}>Оновити дані</Button>}>{moneyError(error)}</Alert>}
    <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 2 }}>Реквізити грошової операції</Typography><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}><TextField label="Дата документа" type="date" {...register('documentDate')} disabled={busy} error={Boolean(errors.documentDate)} helperText={errors.documentDate?.message} slotProps={{ inputLabel: { shrink: true } }} />
    {kind !== 'transfers' && <Controller name="purpose" control={control} render={({ field }) => <TextField {...field} select label="Призначення" required disabled={busy} error={Boolean(errors.purpose)} helperText={errors.purpose?.message} onChange={event => { field.onChange(event); if (event.target.value !== 'SETTLEMENT') { setValue('counterpartyId', '', { shouldDirty: true }); setValue('settlementAmount', null, { shouldDirty: true }); setValue('settlementExchangeRate', null, { shouldDirty: true }); setValue('settlementCurrencyId', '', { shouldDirty: true }) } else { setValue('settlementCurrencyId', uahId, { shouldDirty: true }); setValue('settlementExchangeRate', 1, { shouldDirty: true }) } }} >{baseline?.purpose === null && <MenuItem value="" disabled>Не вказано</MenuItem>}{Object.entries(purposes).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}</TextField>} />}
    {kind !== 'receipts' && <>{account('sourceAccountId', 'Рахунок списання')}{amount('sourceAmount', 'Сума списання', source ? currencyName(source.currencyId, currencies) : undefined)}</>}
    {kind !== 'payments' && <>{account('destinationAccountId', 'Рахунок зарахування')}{amount('destinationAmount', 'Сума зарахування', destination ? currencyName(destination.currencyId, currencies) : undefined)}</>}
    <TextField label="Коментар" {...register('comment')} multiline minRows={2} disabled={busy} /><TextField label="Внутрішня примітка" {...register('internalNote')} multiline minRows={2} disabled={busy} /></Box></Paper>
    {kind !== 'transfers' && values.purpose === 'SETTLEMENT' && <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 2 }}>Взаєморозрахунок</Typography><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}><Controller name="counterpartyId" control={control} render={({ field }) => <TextField {...field} select label="Контрагент" required disabled={busy || lookups.isPending || lookups.isError} error={Boolean(errors.counterpartyId)} helperText={errors.counterpartyId?.message}><MenuItem value="">Виберіть контрагента</MenuItem>{field.value && !lookups.data?.counterparties.some(p => p.id === field.value) && <MenuItem value={field.value} disabled>{lookups.isPending ? 'Завантаження контрагента…' : 'Контрагент недоступний'}</MenuItem>}{lookups.data?.counterparties.filter(p => p.active || p.id === field.value).map(p => <MenuItem value={p.id} key={p.id}>{p.name}{p.active ? '' : ' (неактивний)'}</MenuItem>)}</TextField>} /><Controller name="settlementCurrencyId" control={control} render={({ field }) => <TextField {...field} select label="Валюта взаєморозрахунку" required disabled={busy || lookups.isPending || lookups.isError} error={Boolean(errors.settlementCurrencyId)} helperText={errors.settlementCurrencyId?.message} onChange={event => { field.onChange(event); if (event.target.value === uahId) setValue('settlementExchangeRate', 1, { shouldDirty: true, shouldValidate: true }) }}><MenuItem value="">Виберіть валюту</MenuItem>{currencies.map(id => <MenuItem value={id} key={id}>{currencyName(id, currencies)}</MenuItem>)}</TextField>} />{amount('settlementAmount', 'Сума взаєморозрахунку')}{amount('settlementExchangeRate', 'Курс взаєморозрахунку', values.settlementCurrencyId === uahId ? 'Для UAH курс дорівнює 1.' : 'Курс валюти взаєморозрахунку до UAH.', values.settlementCurrencyId === uahId)}</Box>{values.counterpartyId && user && hasAnyPermission(user, ['settlement.view']) && <Button component={Link} to={`/settlements/${values.counterpartyId}`} sx={{ mt: 2 }}>Відкрити взаєморозрахунки контрагента</Button>}</Paper>}
    {kind !== 'transfers' && values.purpose !== 'SETTLEMENT' && <Alert severity="info">{values.purpose === 'OWNER' ? 'Операція власника створює тільки грошовий рух і не є доходом чи витратою.' : values.purpose === 'OPENING_BALANCE' ? 'Початковий баланс створює тільки грошовий рух.' : 'Ця операція не створює взаєморозрахунок.'}</Alert>}
    {rate !== null && source && destination && <Alert severity="info">Ефективний курс: 1 {currencyName(source.currencyId, currencies)} = {new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 8 }).format(rate)} {currencyName(destination.currencyId, currencies)}. Розраховано із введених сум.</Alert>}
    {lookups.isError && <RequestErrorState error={lookups.error} retry={() => { void lookups.refetch() }} />}
    <DocumentActions danger={remove && <Button color="error" disabled={busy} onClick={remove}>Видалити</Button>}><Button variant="contained" type="submit" disabled={busy || lookups.isPending || lookups.isError}>{mutations.save.isPending ? 'Зберігаємо…' : 'Зберегти'}</Button><Button variant="contained" disabled={busy || lookups.isPending || lookups.isError} onClick={() => { void askPost() }}>Провести</Button></DocumentActions>
  </Stack><ConfirmDialog open={Boolean(confirm)} busy={busy} busyLabel="Проводимо…" title="Провести грошовий документ?" action="Провести" close={() => setConfirm(null)} confirm={() => { if (confirm) void persist(confirm, true) }}><DialogContentText>{kind === 'transfers' ? 'Буде списано суму з рахунку-джерела та зараховано суму на рахунок призначення.' : values.purpose === 'SETTLEMENT' ? 'Буде створено грошовий рух і взаєморозрахунок із контрагентом.' : 'Буде створено грошовий рух без взаєморозрахунку.'}</DialogContentText><Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>Незбережені зміни буде збережено перед проведенням.</Typography></ConfirmDialog></>
}
