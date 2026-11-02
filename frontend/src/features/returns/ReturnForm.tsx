import { useEffect, useRef, useState } from 'react'
import { Alert, Box, Button, Checkbox, DialogContentText, FormControlLabel, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { DocumentLink } from '../../components/DocumentLink'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { currencyName, dateLabel, locationName, money } from '../purchases/contracts'
import { formLines, returnConfig, returnDefaults, returnError, returnFormSchema, returnInput } from './contracts'
import type { ReturnDocument, ReturnKind, ReturnLookups, ReturnValues } from './contracts'
import { ReturnLines } from './ReturnLines'
import { useReturnMutations, useReturnPreview, useReturnable } from './queries'
import { useReturnNotice } from './notices'

export function ReturnForm({ kind, doc, lookups, initialSourceId, saved, refresh, remove, onDirtyChange, compact = false }: { onDirtyChange?: (dirty: boolean) => void; compact?: boolean; kind: ReturnKind; doc?: ReturnDocument; lookups: ReturnLookups; initialSourceId?: string; saved: (doc: ReturnDocument) => void; refresh?: () => void; remove?: () => void }) {
  const config = returnConfig[kind]; const user = useCurrentUser().data; const notify = useReturnNotice(); const mutations = useReturnMutations(kind)
  const [baseline, setBaseline] = useState(doc); const [error, setErrorState] = useState<unknown>(null); const [confirm, setConfirm] = useState<ReturnValues | null>(null)
  const { register, control, setValue, getValues, setError, reset, handleSubmit, formState: { errors, isDirty } } = useForm<ReturnValues>({ resolver: zodResolver(returnFormSchema), defaultValues: returnDefaults(doc, initialSourceId) })
  useEffect(() => { onDirtyChange?.(isDirty) }, [isDirty, onDirtyChange])
  const values = useWatch({ control }) as ReturnValues; const source = lookups.sources.find(s => s.id === values.sourceId)
  const ownId = kind === 'supplier' && baseline?.sourceId === values.sourceId ? baseline.document.id : undefined
  const lines = useReturnable(kind, values.sourceId, values.storageLocationId, ownId, Boolean(source)); const initializedSource = useRef<string | null>(null)
  useEffect(() => { if (source && !getValues('storageLocationId')) setValue('storageLocationId', source.storageLocationId); if (lines.data && initializedSource.current !== values.sourceId) { setValue('lines', formLines(lines.data, baseline?.sourceId === values.sourceId ? baseline : undefined)); initializedSource.current = values.sourceId } }, [source, lines.data, values.sourceId, baseline, getValues, setValue])
  const parsed = returnFormSchema.safeParse(values); const input = parsed.success ? returnInput(kind, parsed.data, undefined, true) : null
  const previewKey = input ? JSON.stringify(input) : ''; const debouncedKey = useDebouncedValue(previewKey, 300); const debouncedInput: unknown = debouncedKey ? JSON.parse(debouncedKey) : null; const inputCurrent = previewKey === debouncedKey
  const preview = useReturnPreview(kind, debouncedInput, Boolean(source && debouncedInput)); const previewReady = Boolean(inputCurrent && preview.isSuccess)
  const busy = mutations.save.isPending || mutations.transition.isPending; const currency = source ? currencyName(source.currencyId, lookups.sources.map(s => s.currencyId)) : ''
  const selected = values.lines.filter(l => l.selected); const refunds = new Map(previewReady ? preview.data?.lines.map(l => [l.sourceLineId, l.refundAmount]) : [])
  const limitsValid = (v: ReturnValues) => {
    let valid = true
    if (!lookups.sources.some(s => s.id === v.sourceId)) { setError('sourceId', { message: 'Виберіть проведений документ своєї організації.' }); valid = false }
    v.lines.forEach((line, i) => { if (!line.selected) return; const row = lines.data?.find(r => r.sourceLineId === line.sourceLineId); if (!row) { valid = false; return }
      const max = kind === 'supplier' ? row.maxReturnableQuantity : row.returnableQuantity
      if (line.itemType === 'GOODS' && (line.quantity ?? 0) > (max ?? 0)) { setError(`lines.${i}.quantity`, { message: kind === 'supplier' ? 'Недостатньо доступного товару або перевищено ліміт закупівлі.' : 'Кількість перевищує доступну до повернення.' }); valid = false }
      if (line.itemType === 'SERVICE' && (line.refundAmount ?? 0) > (row.refundableAmount ?? 0)) { setError(`lines.${i}.refundAmount`, { message: 'Сума перевищує залишок компенсації.' }); valid = false }
    }); return valid
  }
  const persist = async (v: ReturnValues, post: boolean) => {
    let result = baseline; setErrorState(null)
    try {
      if (!result || isDirty) {
        result = await mutations.save.mutateAsync({ id: result?.document.id, input: returnInput(kind, v, baseline) }); setBaseline(result)
        reset({ ...v, lines: v.lines.map(l => ({ ...l, id: result!.lines.find(r => r.sourceLineId === l.sourceLineId)?.id ?? null })) })
      }
      if (!result) return
      if (post) result = await mutations.transition.mutateAsync({ id: result.document.id, action: 'post' })
      notify({ severity: 'success', message: post ? 'Повернення проведено.' : 'Чернетку повернення збережено.' }); saved(result)
    } catch (caught) {
      setErrorState(caught); notify({ severity: 'error', message: returnError(caught) })
      if (caught instanceof ApiError) {
        for (const [key, messages] of Object.entries(caught.fieldErrors)) { const field = key.replace(/^\$\./, '').replace(/^[A-Z]/, c => c.toLowerCase()); const mapped = field === config.sourceKey ? 'sourceId' : field; if (/^(documentDate|sourceId|storageLocationId|comment|internalNote)$/.test(mapped)) setError(mapped as 'sourceId', { message: messages.join(' ') }) }
        if (caught.code === 'INSUFFICIENT_STOCK') v.lines.forEach((line, i) => { if (line.selected && line.itemType === 'GOODS') setError(`lines.${i}.quantity`, { message: 'Перевірте доступний залишок цього рядка. Оновіть дані.' }) })
      }
      if (!doc && result) saved(result)
    } finally { setConfirm(null) }
  }
  const submit = handleSubmit(v => { if (limitsValid(v)) return persist(v, false) }); const askPost = handleSubmit(v => { if (limitsValid(v)) setConfirm(v) })
  const controls = (row: NonNullable<typeof lines.data>[number], index: number) => {
    const line = values.lines[index]; if (!line || line.sourceLineId !== row.sourceLineId) return null
    const name = lookups.products.find(p => p.productId === row.productId)?.name || 'Номенклатура'
    const numeric = (field: 'quantity' | 'refundAmount', label: string) => <Controller name={`lines.${index}.${field}`} control={control} render={({ field: binding }) => <TextField label={label} type="number" value={binding.value ?? ''} onChange={e => binding.onChange(e.target.value === '' ? null : Number(e.target.value))} onBlur={binding.onBlur} inputRef={binding.ref} disabled={busy || !line.selected} error={Boolean(errors.lines?.[index]?.[field])} helperText={errors.lines?.[index]?.[field]?.message} slotProps={{ htmlInput: { min: field === 'quantity' && row.itemType === 'SERVICE' ? '0' : '0.0001', step: '0.0001', 'aria-label': `${label} · ${name}` } }} fullWidth />} />
    return <Stack spacing={2}><Controller name={`lines.${index}.selected`} control={control} render={({ field }) => <FormControlLabel control={<Checkbox checked={field.value} onChange={e => field.onChange(e.target.checked)} disabled={busy} />} label={`Повернути · ${name}`} />} />{numeric('quantity', 'Кількість')}{kind === 'customer' && row.itemType === 'SERVICE' ? numeric('refundAmount', 'Сума компенсації') : <TextField label="Сума компенсації" value={line.selected && refunds.has(row.sourceLineId) ? money(refunds.get(row.sourceLineId)!) : '—'} slotProps={{ input: { readOnly: true }, htmlInput: { 'aria-label': `Сума компенсації · ${name}` } }} helperText="За початковою ціною та знижкою." />}</Stack>
  }
  return <><Stack component="form" noValidate spacing={2} onSubmit={e => { void submit(e) }}>
    {error != null && <Alert severity="error" action={refresh && <Button disabled={busy} onClick={refresh}>Оновити дані</Button>}>{returnError(error)}</Alert>}
    <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" sx={{ mb: 2 }}>Реквізити повернення</Typography><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}><TextField label="Дата документа" type="date" {...register('documentDate')} disabled={busy} error={Boolean(errors.documentDate)} helperText={errors.documentDate?.message} slotProps={{ inputLabel: { shrink: true } }} /><Controller name="sourceId" control={control} render={({ field }) => <TextField {...field} select required label={config.source} disabled={busy} error={Boolean(errors.sourceId) || Boolean(values.sourceId && !source)} helperText={errors.sourceId?.message ?? (values.sourceId && !source ? 'Документ-підстава недоступний. Потрібен проведений документ своєї організації.' : kind === 'supplier' ? 'Тільки проведені закупівлі. Початкові залишки виключено.' : 'Тільки проведені продажі поточної організації.')} onChange={e => { field.onChange(e); setValue('lines', [], { shouldDirty: true }); const next = lookups.sources.find(s => s.id === e.target.value); setValue('storageLocationId', next?.storageLocationId ?? '', { shouldDirty: true }); initializedSource.current = null }}><MenuItem value="">Виберіть документ</MenuItem>{field.value && !source && <MenuItem disabled value={field.value}>Документ недоступний</MenuItem>}{lookups.sources.map(s => <MenuItem key={s.id} value={s.id}>{s.number || 'Без номера'} · {dateLabel(s.documentDate)} · {s.counterpartyName}</MenuItem>)}</TextField>} /><TextField label={config.party} value={source?.counterpartyName ?? ''} slotProps={{ input: { readOnly: true } }} helperText="Визначається документом-підставою." /><Controller name="storageLocationId" control={control} render={({ field }) => <TextField {...field} select required label="Склад повернення" disabled={busy} error={Boolean(errors.storageLocationId)} helperText={errors.storageLocationId?.message}>{!field.value && <MenuItem value="">Виберіть склад</MenuItem>}{field.value && !lookups.locations.some(l => l.id === field.value) && <MenuItem value={field.value} disabled>Склад недоступний</MenuItem>}{lookups.locations.map(l => <MenuItem key={l.id} value={l.id}>{locationName(l.id, lookups.locations)}</MenuItem>)}</TextField>} /><TextField label="Коментар" {...register('comment')} multiline minRows={2} disabled={busy} /><TextField label="Внутрішня примітка" {...register('internalNote')} multiline minRows={2} disabled={busy} /></Box>{source && <Typography variant="body2" sx={{ mt: 2 }}>Підстава: <DocumentLink id={source.id} type={kind === 'customer' ? 'SALE_INVOICE' : 'PURCHASE_INVOICE'} number={source.number} /> · {currency} · Початкова знижка {money(source.discountPercent)}%{kind === 'supplier' ? ` · Курс ${money(source.exchangeRate)}` : ''}</Typography>}</Paper>
    {kind === 'supplier' && <Alert severity="info">Чернетка резервує товар у вибраному складі. Компенсація враховує лише початкову ціну, знижку та валюту закупівлі. Закупівельні витрати до неї не входять.</Alert>}
    {!lookups.sources.length && <EmptyState title={kind === 'customer' ? 'Немає проведених продажів' : 'Немає проведених закупівель'} description="Спочатку проведіть документ-підставу." />}
    {lines.isFetching && !lines.data ? <ListLoading label="Завантаження рядків для повернення" /> : lines.isError ? <RequestErrorState error={lines.error} retry={() => { void lines.refetch() }} /> : lines.data && <ReturnLines compact={compact} kind={kind} rows={lines.data} lookups={lookups} inputs={controls} />}
    {typeof errors.lines?.message === 'string' && <Alert severity="error">{errors.lines.message}</Alert>}
    {preview.isError && Boolean(debouncedInput) && inputCurrent && <Alert severity="error" action={<Button onClick={() => { void lines.refetch(); void preview.refetch() }}>Оновити розрахунок</Button>}>{returnError(preview.error)}</Alert>}
    <DocumentActions summary={<Box><Typography variant="caption" color="text.secondary">Загальна сума компенсації</Typography><Typography variant="subtitle1" aria-live="polite">{!selected.length ? '—' : previewReady ? money(preview.data!.totalRefund) : input ? 'Розрахунок…' : 'Перевірте рядки'} {currency}</Typography></Box>} danger={remove && <Button color="error" disabled={busy} onClick={remove}>Видалити</Button>}><Button type="submit" variant="contained" disabled={busy || lines.isFetching}>{busy ? 'Зберігаємо…' : 'Зберегти'}</Button>{user && hasAnyPermission(user, [`${config.permission}.post`]) && <Button variant="contained" disabled={busy || lines.isFetching} onClick={() => { void askPost() }}>Провести</Button>}</DocumentActions>
  </Stack><ConfirmDialog open={Boolean(confirm)} busy={busy} busyLabel="Провести" title="Провести повернення?" action="Провести" close={() => setConfirm(null)} confirm={() => { if (confirm) void persist(confirm, true) }}><DialogContentText>{config.confirmation}</DialogContentText><Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>Незбережені зміни буде збережено перед проведенням.</Typography></ConfirmDialog></>
}
