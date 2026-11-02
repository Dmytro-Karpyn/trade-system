import type { ReactNode } from 'react'
import { useState } from 'react'
import { Alert, Box, Button, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ApiError } from '../../api/client'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { RequestErrorState } from '../../components/RequestState'
import { locationName } from '../purchases/contracts'
import { operationDefaults, operationFormSchema, operationInput, stockError } from './contracts'
import type { OperationKind, OperationValues, StockOperation, StockRow } from './contracts'
import { OperationLines } from './OperationLines'
import { useStockNotice } from './notices'
import { useOperationMutations, useReasons, useStockCandidates, useStockLookups } from './queries'

export function OperationForm({ kind, doc, rows, saved, refresh, remove, summary }: { summary?: ReactNode; kind: OperationKind; doc?: StockOperation; rows: StockRow[]; saved: (result: StockOperation) => void; refresh?: () => void; remove?: () => void }) {
  const [baseline, setBaseline] = useState(doc); const [confirm, setConfirm] = useState<OperationValues | null>(null); const [error, setErrorState] = useState<unknown>(null)
  const mutations = useOperationMutations(kind); const lookups = useStockLookups(); const reasons = useReasons(kind === 'write-offs'); const notify = useStockNotice()
  const { control, register, handleSubmit, setError, setValue, reset, formState: { errors, isDirty } } = useForm<OperationValues>({ resolver: zodResolver(operationFormSchema), defaultValues: operationDefaults(doc) })
  const values = useWatch({ control }) as OperationValues
  const { fields, append, remove: removeLine } = useFieldArray({ control, name: 'lines' })
  const candidates = useStockCandidates(values.source, baseline?.document.id)
  const busy = mutations.save.isPending || mutations.transition.isPending
  const locations = lookups.data?.operationLocations ?? []
  const valid = (input: OperationValues, post: boolean) => {
    let ok = true
    if (kind === 'transfers' && !input.destination) { setError('destination', { message: 'Виберіть склад призначення.' }); ok = false }
    if (kind === 'transfers' && input.source === input.destination) { setError('destination', { message: 'Склади джерела та призначення мають відрізнятися.' }); ok = false }
    if (kind === 'write-offs' && !input.reason) { setError('reason', { message: 'Виберіть причину списання.' }); ok = false }
    if (post && !input.lines.length) { setError('lines', { message: 'Для проведення додайте хоча б один рядок.' }); ok = false }
    input.lines.forEach((line, index) => { const candidate = candidates.data?.find(x => x.stock.batchId === line.batchId); if (candidates.isSuccess && (!candidate || line.quantity > candidate.availableForDocument)) { setError(`lines.${index}.quantity`, { message: 'Недостатньо доступного залишку.' }); ok = false } })
    return ok
  }
  const persist = async (input: OperationValues, post: boolean) => {
    let result = baseline; setErrorState(null)
    try {
      if (!result || isDirty) { result = await mutations.save.mutateAsync({ id: result?.document.id, input: operationInput(kind, input, baseline) }); setBaseline(result); reset(operationDefaults(result)) }
      if (!result) return
      if (post) result = await mutations.transition.mutateAsync({ id: result.document.id, action: 'post' })
      notify({ severity: 'success', message: post ? 'Документ проведено.' : 'Чернетку збережено.' }); saved(result)
    } catch (caught) {
      setErrorState(caught); notify({ severity: 'error', message: stockError(caught) })
      if (caught instanceof ApiError) {
        if (caught.code === 'INSUFFICIENT_STOCK' || caught.code === 'HISTORICAL_NEGATIVE_STOCK') input.lines.forEach((_, index) => setError(`lines.${index}.quantity`, { message: stockError(caught) }))
        for (const [key, messages] of Object.entries(caught.fieldErrors)) {
          const path = key.replace(/^\$\./, '').replace(/\[(\d+)\]/g, '.$1').replace(/^[A-Z]/, x => x.toLowerCase()).replace(/\.([A-Z])/g, (_, x: string) => `.${x.toLowerCase()}`)
          const mapped = ({ fromLocationId: 'source', storageLocationId: 'source', toLocationId: 'destination', writeOffReasonId: 'reason' } as Record<string, string>)[path] ?? path
          if (/^(source|destination|reason|documentDate|lines\.\d+\.(batchId|quantity))$/.test(mapped)) setError(mapped as 'source', { message: messages.join(' ') })
        }
      }
      if (!doc && result) saved(result)
    } finally { setConfirm(null) }
  }
  const submit = handleSubmit(input => { if (valid(input, false)) return persist(input, false) })
  const askPost = handleSubmit(input => { if (valid(input, true)) setConfirm(input) })
  return <><Stack component="form" noValidate spacing={2} onSubmit={event => { void submit(event) }}>
    {error != null && <Alert severity="error" action={refresh && <Button onClick={refresh} disabled={busy}>Оновити дані</Button>}>{stockError(error)}</Alert>}
    <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 2 }}>Реквізити документа</Typography><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}><TextField label="Дата документа" type="date" disabled={busy} {...register('documentDate')} error={Boolean(errors.documentDate)} helperText={errors.documentDate?.message} slotProps={{ inputLabel: { shrink: true } }} /><Controller name="source" control={control} render={({ field }) => <TextField {...field} select label={kind === 'transfers' ? 'Зі складу' : 'Склад'} required disabled={busy || lookups.isPending || lookups.isError} error={Boolean(errors.source)} helperText={errors.source?.message ?? 'Доступні склади, уже використані організацією.'} onChange={event => { field.onChange(event); values.lines.forEach((_, index) => setValue(`lines.${index}.batchId`, '', { shouldDirty: true })) }}><MenuItem value="">Виберіть склад</MenuItem>{locations.map(location => <MenuItem key={location.id} value={location.id}>{locationName(location.id, locations)}</MenuItem>)}</TextField>} />{kind === 'transfers' ? <Controller name="destination" control={control} render={({ field }) => <TextField {...field} select label="На склад" required disabled={busy || lookups.isPending || lookups.isError} error={Boolean(errors.destination)} helperText={errors.destination?.message}><MenuItem value="">Виберіть склад</MenuItem>{locations.map(location => <MenuItem key={location.id} value={location.id}>{locationName(location.id, locations)}</MenuItem>)}</TextField>} /> : <Controller name="reason" control={control} render={({ field }) => <TextField {...field} select label="Причина списання" required disabled={busy || reasons.isPending || reasons.isError} error={Boolean(errors.reason)} helperText={errors.reason?.message ?? (!reasons.data?.some(x => x.active) ? 'Активних причин немає. Додайте їх через наявний API довідника.' : undefined)}><MenuItem value="">Виберіть причину</MenuItem>{reasons.data?.filter(reason => reason.active || reason.id === field.value).map(reason => <MenuItem key={reason.id} value={reason.id} disabled={!reason.active}>{reason.name}{reason.active ? '' : ' (неактивна)'}</MenuItem>)}</TextField>} />}<TextField label="Коментар" {...register('comment')} disabled={busy} multiline minRows={2} /><TextField label="Внутрішня примітка" {...register('internalNote')} disabled={busy} multiline minRows={2} /></Box></Paper>
    {(lookups.isError || reasons.isError || candidates.isError) && <RequestErrorState error={lookups.error ?? reasons.error ?? candidates.error} retry={() => { void lookups.refetch(); if (kind === 'write-offs') void reasons.refetch(); if (values.source) void candidates.refetch() }} />}
    <Alert severity="info">Чернетка резервує товар у {kind === 'transfers' ? 'складі-джерелі. Після проведення той самий Batch переміститься на склад призначення.' : 'вибраному складі. Після проведення товар буде списано.'}</Alert>
    <OperationLines fields={fields} lines={values.lines ?? []} control={control} register={register} setValue={setValue} errors={errors} candidates={candidates.data ?? []} knownRows={rows} disabled={busy || !values.source || candidates.isPending || candidates.isError} append={() => append({ lineId: null, batchId: '', quantity: 1 })} remove={removeLine} />
    {summary}
    <Typography variant="body2" color="text.secondary">Рядків: {fields.length}. Кількість показана в одиницях кожного товару.</Typography>
    <DocumentActions danger={remove && <Button disabled={busy} color="error" onClick={remove}>Видалити</Button>}><Button variant="contained" type="submit" disabled={busy || lookups.isPending || candidates.isFetching}>{mutations.save.isPending ? 'Зберігаємо…' : 'Зберегти'}</Button><Button variant="contained" disabled={busy || lookups.isPending || candidates.isFetching} onClick={() => { void askPost() }}>Провести</Button></DocumentActions>
  </Stack><ConfirmDialog open={Boolean(confirm)} busy={busy} title="Провести документ?" action="Провести" close={() => setConfirm(null)} confirm={() => { if (confirm) void persist(confirm, true) }}>{kind === 'transfers' ? 'Товар буде переміщено зі складу-джерела на склад призначення в тих самих партіях.' : 'Товар буде списано зі складу. Грошові рухи та взаєморозрахунки не створюються.'}</ConfirmDialog></>
}
