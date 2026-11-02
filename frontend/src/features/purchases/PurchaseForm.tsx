import { useEffect, useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, FormControl, FormHelperText, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography } from '@mui/material'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { RequestErrorState } from '../../components/RequestState'
import { CreateLocationDialog } from './CreateLocationDialog'
import { PurchaseLines } from './PurchaseLines'
import { PurchaseTotals } from './PurchaseTotals'
import { currencyName, formDefaults, locationName, purchaseError, purchaseFormSchema, purchaseInput, uahId } from './contracts'
import type { Purchase, PurchaseBatch, PurchaseFormValues } from './contracts'
import type { SelectedProduct } from './ProductSelector'
import { usePurchaseLookups, usePurchaseMutations, usePurchaseSuppliers } from './queries'
import { usePurchaseNotice } from './notices'

export function PurchaseForm({ purchase, batches = [], supplierName, saved, refresh, remove, compact = false, linesOnly = false, onDirtyChange }: { compact?: boolean; linesOnly?: boolean; onDirtyChange?: (dirty: boolean) => void; purchase?: Purchase; batches?: PurchaseBatch[]; supplierName?: string; saved: (purchase: Purchase) => void; refresh?: () => void; remove?: () => void }) {
  const user = useCurrentUser().data
  // Keep the version belonging to the displayed form values. A background query
  // refresh must not silently replace it and bypass optimistic concurrency.
  const [baseline, setBaseline] = useState(purchase)
  const lookups = usePurchaseLookups()
  const suppliers = usePurchaseSuppliers(true)
  const mutations = usePurchaseMutations(baseline?.document.id)
  const notify = usePurchaseNotice()
  const [locationDialog, setLocationDialog] = useState(false)
  const [postValues, setPostValues] = useState<PurchaseFormValues | null>(null)
  const [actionError, setActionError] = useState<unknown>(null)
  const [selectedProducts, setSelectedProducts] = useState<Map<string, SelectedProduct>>(() => new Map(purchase?.products.map(product => [product.productId, { id: product.productId, name: product.name, code: product.code }]) ?? []))
  const { control, register, handleSubmit, setValue, setError, reset, formState: { errors, isDirty } } = useForm<PurchaseFormValues>({ resolver: zodResolver(purchaseFormSchema), defaultValues: formDefaults(purchase) })
  useEffect(() => { onDirtyChange?.(isDirty) }, [isDirty, onDirtyChange])
  const values = useWatch({ control })
  const busy = mutations.save.isPending || mutations.transition.isPending
  const locations = lookups.data?.locations ?? []
  const currencies = lookups.data?.currencyIds ?? [uahId]
  const products = new Map(selectedProducts)
  for (const product of purchase?.products ?? []) if (!products.has(product.productId)) products.set(product.productId, { id: product.productId, name: product.name, code: product.code })
  const error = (caught: unknown) => {
    setActionError(caught)
    if (caught instanceof ApiError) for (const [key, messages] of Object.entries(caught.fieldErrors)) {
      const path = key.replace(/^\$\./, '').replace(/\[(\d+)\]/g, '.$1').replace(/^[A-Z]/, value => value.toLowerCase()).replace(/\.([A-Z])/g, (_, value: string) => `.${value.toLowerCase()}`)
      const known = /^(documentDate|counterpartyId|storageLocationId|currencyId|exchangeRate|discountPercent|externalNumber|externalDate|comment|internalNote|lines\.\d+\.(productId|quantity|unitPriceCurrency))$/
      if (known.test(path)) setError(path as 'documentDate', { message: messages.join(' ') })
    }
    notify({ severity: 'error', message: purchaseError(caught) })
  }
  const persist = async (input: PurchaseFormValues, post: boolean) => {
    setActionError(null)
    let result = baseline
    try {
      if (!baseline || isDirty || input.exchangeRate !== baseline.exchangeRate) {
        result = await mutations.save.mutateAsync(purchaseInput(input, baseline))
        setBaseline(result)
        reset(formDefaults(result))
      }
      if (!result) return
      if (post) result = await mutations.transition.mutateAsync({ action: 'post', documentId: result.document.id })
      setPostValues(null)
      notify({ severity: 'success', message: post ? 'Закупівлю проведено.' : 'Чернетку збережено.' })
      saved(result)
    } catch (caught) {
      setPostValues(null)
      error(caught)
      // A successful create followed by a failed/uncertain POST is still one
      // saved document. Open it for a fresh read instead of creating it again.
      if (!purchase && result && user && hasAnyPermission(user, ['purchase.view'])) saved(result)
    }
  }
  const submit = handleSubmit(input => persist(input, false))
  const askPost = handleSubmit(input => {
    if (!input.lines.length) { setError('lines', { message: 'Для проведення додайте хоча б один товар.' }); return }
    setPostValues(input)
  })
  const text = (name: 'documentDate' | 'externalNumber' | 'externalDate' | 'comment' | 'internalNote', label: string, type = 'text', multiline = false) => <TextField label={label} type={type} fullWidth multiline={multiline} minRows={multiline ? 2 : undefined} {...register(name)} disabled={busy} error={Boolean(errors[name])} helperText={errors[name]?.message} slotProps={type === 'date' ? { inputLabel: { shrink: true } } : undefined} />
  if (!user) return null
  return <>
    <Stack component="form" noValidate onSubmit={event => { void submit(event) }} spacing={2}>
      {actionError != null && <Alert severity="error" action={refresh && <Button onClick={refresh} disabled={busy}>Оновити дані</Button>}>{purchaseError(actionError)}</Alert>}
      <Paper variant="outlined" sx={{ p: 2, display: linesOnly ? 'none' : undefined }}><Typography variant="h2" component="h2" sx={{ mb: 2 }}>Реквізити накладної</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', xl: 'repeat(3, 1fr)' }, gap: 2 }}>
          {text('documentDate', 'Дата документа', 'date')}
          <Controller name="counterpartyId" control={control} render={({ field }) => <FormControl fullWidth size="small" required error={Boolean(errors.counterpartyId)} disabled={busy || suppliers.isPending || suppliers.isError || !suppliers.permitted}><InputLabel id="purchase-supplier">Постачальник</InputLabel><Select labelId="purchase-supplier" label="Постачальник *" {...field}><MenuItem value="" disabled>Виберіть постачальника</MenuItem>{field.value && !suppliers.data?.some(item => item.id === field.value) && <MenuItem value={field.value} disabled>{supplierName || 'Поточний постачальник (недоступний)'}</MenuItem>}{suppliers.data?.filter(item => item.isSupplier && item.active).map(item => <MenuItem key={item.id} value={item.id}>{item.name}</MenuItem>)}</Select><FormHelperText>{errors.counterpartyId?.message ?? (!suppliers.permitted ? 'Потрібне право перегляду контрагентів.' : suppliers.isPending ? 'Завантаження постачальників…' : !suppliers.data?.length ? 'Немає активних постачальників.' : undefined)}</FormHelperText></FormControl>} />
          <Box><Controller name="storageLocationId" control={control} render={({ field }) => <FormControl fullWidth size="small" required error={Boolean(errors.storageLocationId)} disabled={busy || lookups.isPending || lookups.isError}><InputLabel id="purchase-location">Склад</InputLabel><Select labelId="purchase-location" label="Склад *" {...field}><MenuItem value="" disabled>Виберіть склад</MenuItem>{locations.map(item => <MenuItem key={item.id} value={item.id}>{locationName(item.id, locations)}</MenuItem>)}</Select><FormHelperText>{errors.storageLocationId?.message ?? (!locations.length ? 'Додайте перший склад.' : undefined)}</FormHelperText></FormControl>} /><Button size="small" disabled={busy || lookups.isPending || lookups.isError} onClick={() => setLocationDialog(true)}>Додати склад</Button></Box>
          <Controller name="currencyId" control={control} render={({ field }) => <FormControl fullWidth size="small" required error={Boolean(errors.currencyId)} disabled={busy || lookups.isPending || lookups.isError}><InputLabel id="purchase-currency">Валюта</InputLabel><Select labelId="purchase-currency" label="Валюта *" {...field} onChange={event => { field.onChange(event); if (event.target.value === uahId) setValue('exchangeRate', 1, { shouldDirty: true, shouldValidate: true }) }}>{currencies.map(id => <MenuItem key={id} value={id}>{currencyName(id, currencies)}</MenuItem>)}</Select><FormHelperText>{errors.currencyId?.message ?? (values.currencyId !== uahId ? 'Назву валюти не задано у довіднику.' : undefined)}</FormHelperText></FormControl>} />
          <TextField label="Курс" type="number" {...register('exchangeRate', { valueAsNumber: true })} disabled={busy || values.currencyId === uahId} error={Boolean(errors.exchangeRate)} helperText={errors.exchangeRate?.message ?? (values.currencyId === uahId ? 'Для UAH курс дорівнює 1.' : 'Кількість гривень за одиницю валюти.')} slotProps={{ htmlInput: { min: '0.00000001', step: '0.00000001' } }} />
          <TextField label="Знижка, %" type="number" {...register('discountPercent', { valueAsNumber: true })} disabled={busy} error={Boolean(errors.discountPercent)} helperText={errors.discountPercent?.message} slotProps={{ htmlInput: { min: 0, max: 100, step: '0.0001' } }} />
          {text('externalNumber', 'Зовнішній номер')}{text('externalDate', 'Зовнішня дата', 'date')}{text('comment', 'Коментар', 'text', true)}{text('internalNote', 'Внутрішня примітка', 'text', true)}
        </Box>
        {(lookups.isError || suppliers.isError) && <RequestErrorState error={lookups.error ?? suppliers.error} retry={() => { void lookups.refetch(); void suppliers.refetch() }} />}
      </Paper>
      <PurchaseLines compact={compact} control={control} register={register} errors={errors} products={products} addProduct={product => setSelectedProducts(previous => new Map(previous).set(product.id, product))} lockedLineIds={new Set(batches.map(batch => batch.purchaseInvoiceLineId))} disabled={busy} />
      <PurchaseTotals lines={(values.lines ?? []).map(line => ({ quantity: line.quantity ?? 0, unitPriceCurrency: line.unitPriceCurrency ?? 0 }))} discountPercent={values.discountPercent ?? 0} exchangeRate={values.exchangeRate ?? 0} currency={currencyName(values.currencyId ?? uahId, currencies)} />
      <DocumentActions danger={remove && <Button color="error" onClick={remove} disabled={busy}>Видалити</Button>}><Button type="submit" variant="contained" disabled={busy || lookups.isPending}>{mutations.save.isPending ? 'Зберігаємо…' : 'Зберегти'}</Button>{hasAnyPermission(user, ['purchase.post']) && <Button variant="contained" disabled={busy || lookups.isPending} onClick={() => { void askPost() }}>{mutations.transition.isPending ? 'Проводимо…' : 'Провести'}</Button>}</DocumentActions>
    </Stack>
    {locationDialog && <CreateLocationDialog close={() => setLocationDialog(false)} created={location => setValue('storageLocationId', location.id, { shouldDirty: true, shouldValidate: true })} />}
    <ConfirmDialog busyLabel="Проводимо…" open={Boolean(postValues)} busy={busy} title="Провести закупівлю?" action="Провести" close={() => setPostValues(null)} confirm={() => { if (postValues) void persist(postValues, true) }}>Після проведення будуть створені партії та складські залишки.<Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>Зміни накладної буде збережено перед проведенням.</Typography></ConfirmDialog>
  </>
}
