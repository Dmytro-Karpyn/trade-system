import { useEffect, useRef, useState } from 'react'
import { Alert, Box, Button, Chip, FormControl, FormHelperText, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography } from '@mui/material'
import { zodResolver } from '@hookform/resolvers/zod'
import { Controller, useFieldArray, useForm, useWatch } from 'react-hook-form'
import { useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../api/client'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DocumentActions } from '../../components/DocumentActions'
import { RequestErrorState } from '../../components/RequestState'
import { usePriceTypes } from '../counterparties/queries'
import { locationName } from '../purchases/contracts'
import { AllocationDialog } from './AllocationDialog'
import { CustomerBalance } from './CustomerBalance'
import { defaults, emptyLine, saleError, saleFormSchema, saleInput, snapshot } from './contracts'
import type { Sale, SaleFormLine, SaleFormValues } from './contracts'
import { SaleLines } from './SaleLines'
import type { LinePrice } from './SaleLines'
import type { SaleProduct } from './SaleProductSelector'
import { SaleTotals } from './SaleTotals'
import { useSaleNotice } from './notices'
import { useCustomers, useSaleLookups, useSaleMutations, useSalePrices } from './queries'

const postMessage = 'Після проведення буде списано товар зі складу та створено взаєморозрахунок з покупцем.'
export function SaleForm({ sale, saved, refresh, remove, compact = false, onDirtyChange }: { compact?: boolean; onDirtyChange?: (dirty: boolean) => void; sale?: Sale; saved: (sale: Sale) => void; refresh?: () => void; remove?: () => void }) {
  const user = useCurrentUser().data
  const [baseline, setBaseline] = useState(sale)
  const customers = useCustomers(true); const priceTypes = usePriceTypes(); const lookups = useSaleLookups()
  const mutations = useSaleMutations(baseline?.document.id); const notify = useSaleNotice(); const queryClient = useQueryClient()
  const [selectedProducts, setSelectedProducts] = useState<Map<string, SaleProduct>>(() => new Map(sale?.products.map(product => [product.productId, { id: product.productId, name: product.name, code: product.code, itemType: product.itemType }]) ?? []))
  const [postValues, setPostValues] = useState<SaleFormValues | null>(null); const [actionError, setActionError] = useState<unknown>(null)
  const [allocation, setAllocation] = useState<{ product?: SaleProduct; quantity?: number } | null>(null)
  const retailInitialized = useRef(Boolean(sale))
  const { control, register, handleSubmit, setValue, getValues, setError, reset, formState: { errors, isDirty } } = useForm<SaleFormValues>({ resolver: zodResolver(saleFormSchema), defaultValues: defaults(sale) })
  useEffect(() => { onDirtyChange?.(isDirty) }, [isDirty, onDirtyChange])
  const { fields, append, remove: removeLine, replace } = useFieldArray({ control, name: 'lines' })
  const values = useWatch({ control }) as SaleFormValues
  const lines = values.lines ?? []
  const prices = useSalePrices(lines.map(line => line.productId), values.priceTypeId)
  const busy = mutations.save.isPending || mutations.transition.isPending
  const canLower = Boolean(user && hasAnyPermission(user, ['sale.change_price']))
  const products = new Map(selectedProducts)
  for (const product of sale?.products ?? []) if (!products.has(product.productId)) products.set(product.productId, { id: product.productId, name: product.name, code: product.code, itemType: product.itemType })
  const buyer = customers.data?.find(customer => customer.id === values.counterpartyId)
  useEffect(() => {
    if (!retailInitialized.current && customers.isSuccess) {
      retailInitialized.current = true
      if (!getValues('counterpartyId')) {
        const retail = customers.data?.find(customer => customer.isSystem && customer.isCustomer && customer.active && customer.name === 'Роздрібний покупець')
        if (retail) { setValue('counterpartyId', retail.id, { shouldDirty: true }); if (retail.defaultPriceTypeId && !getValues('priceTypeId')) setValue('priceTypeId', retail.defaultPriceTypeId, { shouldDirty: true }) }
      }
    }
  }, [customers.data, customers.isSuccess, getValues, setValue])
  const priceFor = (line: SaleFormLine): LinePrice => {
    const old = snapshot(baseline, values.priceTypeId, line)
    const option = prices.options.get(line.productId)?.find(price => price.batchId === (line.batchId || null))
    const recommended = old?.recommendedUnitPrice ?? option?.unitPrice ?? null
    return { recommended, fallbackActual: old?.unitPrice ?? recommended, missingServicePrice: Boolean(!old && line.itemType === 'SERVICE' && values.priceTypeId && prices.options.get(line.productId) !== undefined && option === undefined) }
  }
  const error = (caught: unknown) => {
    setActionError(caught)
    if (caught instanceof ApiError) {
      for (const [key, messages] of Object.entries(caught.fieldErrors)) {
        const path = key.replace(/^\$\./, '').replace(/\[(\d+)\]/g, '.$1').replace(/^[A-Z]/, value => value.toLowerCase()).replace(/\.([A-Z])/g, (_, value: string) => `.${value.toLowerCase()}`)
        if (/^(documentDate|counterpartyId|storageLocationId|priceTypeId|discountPercent|comment|internalNote|lines\.\d+\.(productId|batchId|quantity|unitPrice))$/.test(path)) setError(path as 'documentDate', { message: messages.join(' ') })
      }
      if (caught.code === 'INSUFFICIENT_STOCK') {
        getValues('lines').forEach((line, index) => { if (line.itemType === 'GOODS') setError(`lines.${index}.quantity`, { message: 'Недостатньо доступного залишку.' }) })
        void queryClient.invalidateQueries({ queryKey: ['stock'] })
      }
    }
    notify({ severity: 'error', message: saleError(caught) })
  }
  const validPrices = (input: SaleFormValues) => {
    let valid = true
    input.lines.forEach((line, index) => {
      const price = priceFor(line)
      if (price.recommended === null) { setError(`lines.${index}.unitPrice`, { message: line.itemType === 'SERVICE' ? 'Для послуги не задано ціну для вибраного типу цін.' : 'Не визначено рекомендовану ціну. Перевірте партію та тип ціни.' }); valid = false }
      else if (!canLower && (line.unitPrice ?? price.fallbackActual ?? 0) < price.recommended) { setError(`lines.${index}.unitPrice`, { message: 'Недостатньо прав для зниження ціни нижче рекомендованої.' }); valid = false }
    })
    return valid
  }
  const persist = async (input: SaleFormValues, post: boolean) => {
    setActionError(null); let result = baseline
    try {
      if (!baseline || isDirty) { result = await mutations.save.mutateAsync(saleInput(input, baseline)); setBaseline(result); reset(defaults(result)) }
      if (!result) return
      if (post) result = await mutations.transition.mutateAsync({ action: 'post', documentId: result.document.id })
      setPostValues(null); notify({ severity: 'success', message: post ? 'Продаж проведено.' : 'Чернетку продажу збережено.' }); saved(result)
    } catch (caught) { setPostValues(null); error(caught); if (!sale && result && user && hasAnyPermission(user, ['sale.view'])) saved(result) }
  }
  const submit = handleSubmit(input => { if (validPrices(input)) return persist(input, false) })
  const askPost = handleSubmit(input => { if (!input.lines.length) { setError('lines', { message: 'Для проведення додайте хоча б один рядок.' }); return } if (validPrices(input)) setPostValues(input) })
  const selectProduct = (index: number, product: SaleProduct | null) => {
    if (product) setSelectedProducts(old => new Map(old).set(product.id, product))
    setValue(`lines.${index}.productId`, product?.id ?? '', { shouldDirty: true })
    setValue(`lines.${index}.itemType`, product?.itemType ?? 'GOODS', { shouldDirty: true })
    setValue(`lines.${index}.batchId`, '', { shouldDirty: true, shouldValidate: true }); setValue(`lines.${index}.unitPrice`, null, { shouldDirty: true })
  }
  const reallocate = (index?: number) => {
    const line = index === undefined ? undefined : getValues('lines')[index]
    const product = line ? products.get(line.productId) : undefined
    setAllocation(product ? { product, quantity: getValues('lines').filter(row => row.productId === product.id).reduce((sum, row) => sum + row.quantity, 0) } : {})
  }
  if (!user) return null
  const locations = lookups.data?.locations ?? []
  return <>
    <Stack component="form" noValidate onSubmit={event => { void submit(event) }} spacing={2}>
      {actionError != null && <Alert severity="error" action={refresh && <Button disabled={busy} onClick={refresh}>Оновити дані</Button>}>{saleError(actionError)}</Alert>}
      <Paper variant="outlined" sx={{ p: 2 }}><Typography variant="h2" component="h2" sx={{ mb: 2 }}>Реквізити продажу</Typography><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', xl: 'repeat(3, 1fr)' }, gap: 2 }}>
        <TextField label="Дата документа" type="date" {...register('documentDate')} disabled={busy} error={Boolean(errors.documentDate)} helperText={errors.documentDate?.message} slotProps={{ inputLabel: { shrink: true } }} />
        <Controller name="counterpartyId" control={control} render={({ field }) => <FormControl size="small" fullWidth required error={Boolean(errors.counterpartyId)} disabled={busy || customers.isPending || customers.isError || !customers.permitted}><InputLabel id="sale-buyer">Покупець</InputLabel><Select labelId="sale-buyer" label="Покупець" {...field} onChange={event => { field.onChange(event); const customer = customers.data?.find(customer => customer.id === event.target.value); if (customer?.defaultPriceTypeId && !getValues('priceTypeId') && !getValues('lines').length) setValue('priceTypeId', customer.defaultPriceTypeId, { shouldDirty: true }) }}><MenuItem value="" disabled>Виберіть покупця</MenuItem>{field.value && !customers.data?.some(customer => customer.id === field.value) && <MenuItem value={field.value} disabled>{sale?.counterpartyName || 'Поточний покупець (недоступний)'}</MenuItem>}{customers.data?.filter(customer => customer.active && customer.isCustomer).map(customer => <MenuItem key={customer.id} value={customer.id}>{customer.name}{customer.isSystem ? ' · Системний' : ''}</MenuItem>)}</Select><FormHelperText>{errors.counterpartyId?.message ?? (!customers.permitted ? 'Потрібне право перегляду контрагентів.' : !customers.data?.length ? 'Немає активних покупців.' : undefined)}</FormHelperText></FormControl>} />
        <Controller name="storageLocationId" control={control} render={({ field }) => <FormControl size="small" fullWidth required error={Boolean(errors.storageLocationId)} disabled={busy || lookups.isPending || lookups.isError}><InputLabel id="sale-location">Склад</InputLabel><Select labelId="sale-location" label="Склад" {...field} onChange={event => { field.onChange(event); getValues('lines').forEach((line, index) => { if (line.itemType === 'GOODS') { setValue(`lines.${index}.batchId`, '', { shouldDirty: true }); setValue(`lines.${index}.unitPrice`, null, { shouldDirty: true }) } }) }}><MenuItem value="" disabled>Виберіть склад</MenuItem>{locations.map(location => <MenuItem key={location.id} value={location.id}>{locationName(location.id, locations)}</MenuItem>)}</Select><FormHelperText>{errors.storageLocationId?.message ?? (!locations.length ? 'Доступних складів ще немає.' : undefined)}</FormHelperText></FormControl>} />
        <Controller name="priceTypeId" control={control} render={({ field }) => <FormControl size="small" fullWidth disabled={busy || priceTypes.isPending || priceTypes.isError || !priceTypes.permitted}><InputLabel id="sale-price-type" shrink>Тип ціни</InputLabel><Select labelId="sale-price-type" label="Тип ціни" displayEmpty notched {...field}><MenuItem value="">Не вибрано</MenuItem>{field.value && !priceTypes.data?.some(type => type.id === field.value) && <MenuItem value={field.value} disabled>Поточний тип ціни (недоступний)</MenuItem>}{priceTypes.data?.map(type => <MenuItem key={type.id} value={type.id}>{type.name}{type.active ? '' : ' (неактивний)'}</MenuItem>)}</Select><FormHelperText>{!priceTypes.permitted ? 'Немає доступу до типів цін.' : 'Рекомендовану ціну визначає вибраний тип ціни.'}</FormHelperText></FormControl>} />
        <TextField label="Знижка, %" type="number" {...register('discountPercent', { valueAsNumber: true })} disabled={busy} error={Boolean(errors.discountPercent)} helperText={errors.discountPercent?.message} slotProps={{ htmlInput: { min: 0, max: 100, step: '0.0001' } }} />
        <Box sx={{ alignSelf: 'center' }}><Chip label="UAH" size="small" variant="outlined" />{buyer?.isSystem && <Chip label="Системний покупець" size="small" sx={{ ml: 1 }} />}</Box>
        <TextField label="Коментар" fullWidth multiline minRows={2} {...register('comment')} disabled={busy} /><TextField label="Внутрішня примітка" fullWidth multiline minRows={2} {...register('internalNote')} disabled={busy} />
      </Box>{(lookups.isError || customers.isError || priceTypes.isError) && <RequestErrorState error={lookups.error ?? customers.error ?? priceTypes.error} retry={() => { void lookups.refetch(); void customers.refetch(); void priceTypes.refetch() }} />}</Paper>
      {buyer && !buyer.isSystem && <CustomerBalance id={buyer.id} />}
      {prices.error && <RequestErrorState error={prices.error} retry={() => { void prices.refetch() }} />}
      <SaleLines compact={compact} fields={fields} lines={lines} control={control} register={register} setValue={setValue} errors={errors} products={products} selectProduct={selectProduct} storageLocationId={values.storageLocationId} sale={baseline} priceFor={priceFor} priceOptions={prices.options} disabled={busy} canLower={canLower} append={() => append(emptyLine())} remove={removeLine} reallocate={reallocate} />
      <SaleTotals lines={lines.map(line => ({ quantity: line.quantity, unitPrice: line.unitPrice ?? priceFor(line).fallbackActual ?? 0 }))} discount={values.discountPercent} />
      <DocumentActions danger={remove && <Button color="error" onClick={remove} disabled={busy}>Видалити</Button>}><Button type="submit" variant="contained" disabled={busy || lookups.isPending || prices.pending}>{mutations.save.isPending ? 'Зберігаємо…' : 'Зберегти'}</Button>{hasAnyPermission(user, ['sale.post']) && <Button variant="contained" disabled={busy || lookups.isPending || prices.pending} onClick={() => { void askPost() }}>{mutations.transition.isPending ? 'Проводимо…' : 'Провести'}</Button>}</DocumentActions>
    </Stack>
    {allocation && <AllocationDialog locationId={values.storageLocationId} saleId={baseline?.document.id} priceTypeId={values.priceTypeId} initialProduct={allocation.product} initialQuantity={allocation.quantity} close={() => setAllocation(null)} apply={(product, allocated) => {
      setSelectedProducts(old => new Map(old).set(product.id, product))
      const current = getValues('lines'); const existing = new Map(current.filter(line => line.productId === product.id).map(line => [line.batchId, line]))
      const replacement = allocated.map(row => ({ ...(existing.get(row.batch.batchId) ?? emptyLine()), productId: product.id, itemType: 'GOODS' as const, batchId: row.batch.batchId, quantity: row.quantity }))
      replace([...current.filter(line => line.productId !== product.id), ...replacement])
    }} />}
    <ConfirmDialog open={Boolean(postValues)} title="Провести продаж?" action="Провести" close={() => setPostValues(null)} busy={busy} confirm={() => { if (postValues) void persist(postValues, true) }}>{postMessage}<Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>Незбережені зміни буде збережено перед проведенням.</Typography></ConfirmDialog>
  </>
}
