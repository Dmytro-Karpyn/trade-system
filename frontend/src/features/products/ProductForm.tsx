import { useCategories } from '../directories/queries'
import { directoryTree } from '../directories/contracts'
import { useEffect, useState } from 'react'
import { useCurrentUser } from '../../auth/queries'
import { usePriceTypes } from '../counterparties/queries'
import { priceTextSchema, useSaveServicePrice } from './service-pricing'
import { RequestErrorState } from '../../components/RequestState'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Checkbox, FormControl, FormControlLabel, FormHelperText, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography } from '@mui/material'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { ApiError } from '../../api/client'
import { DocumentActions } from '../../components/DocumentActions'
import { Icon } from '../../components/Icon'
import { CreateUnitDialog } from './CreateUnitDialog'
import { productDefaults, productFormSchema, toProductInput } from './contracts'
import type { Product, ProductFormValues } from './contracts'
import { useSaveProduct, useUnits } from './queries'
import { useProductNotice } from './notices'

const fieldNames: (keyof ProductFormValues)[] = ['itemType', 'code', 'name', 'unitId', 'categoryId', 'barcode', 'description', 'active']
export function ProductForm({ product, saved, cancel, onDirtyChange }: { onDirtyChange?: (dirty: boolean) => void; product?: Product; saved: (product: Product) => void; cancel: () => void }) {
  const [unitDialog, setUnitDialog] = useState(false)
  const [created, setCreated] = useState<Product | null>(null)
  const [initialPrices, setInitialPrices] = useState<Record<string, string>>({})
  const [priceErrors, setPriceErrors] = useState<Record<string, string>>({})
  const [partialError, setPartialError] = useState('')
  const [savingPrices, setSavingPrices] = useState(false)
  const [completedPrices, setCompletedPrices] = useState<Record<string, number>>({})
  const priceSave = useSaveServicePrice()
  const canPrice = Boolean(useCurrentUser().data?.permissions.includes('settings.manage'))
  const units = useUnits(); const categories=useCategories()
  const save = useSaveProduct(product?.id)
  const notify = useProductNotice()
  const { control, register, handleSubmit, setValue, setError, formState: { errors, isDirty } } = useForm<ProductFormValues>({
    resolver: zodResolver(productFormSchema),
    defaultValues: product ? { itemType: product.itemType, code: product.code, name: product.name, unitId: product.unitId, categoryId: product.categoryId??'', barcode: product.barcode ?? '', description: product.description ?? '', active: product.active } : productDefaults,
  })
  useEffect(() => { onDirtyChange?.(isDirty) }, [isDirty, onDirtyChange])
  const itemType = useWatch({ control, name: 'itemType' })
  const initialTypes = usePriceTypes(!product && itemType === 'SERVICE' && canPrice)
  const busy = save.isPending || savingPrices
  const unitId = useWatch({ control, name: 'unitId' })
  const unitOptions = units.data ?? []
  const selectedUnitUnknown = unitId && !unitOptions.some(unit => unit.id === unitId)
  const submit = handleSubmit(async values => {
    try {
      const wanted = !product && values.itemType === 'SERVICE' && canPrice ? (initialTypes.data ?? []).filter(t => t.active && (initialPrices[t.id] ?? '').trim() !== '') : []
      const invalid = Object.fromEntries(wanted.flatMap(t => { const parsed = priceTextSchema.safeParse(initialPrices[t.id]); return parsed.success ? [] : [[t.id, parsed.error.issues[0].message]] }))
      setPriceErrors(invalid)
      if (Object.keys(invalid).length) return
      const result = created ?? await save.mutateAsync(toProductInput(values, product))
      if (!product) setCreated(result)
      setSavingPrices(true)
      const pending = wanted.filter(t => completedPrices[t.id] !== Number(initialPrices[t.id]))
      const outcomes = await Promise.allSettled(pending.map(async t => { const value = Number(initialPrices[t.id]); await priceSave.mutateAsync({ productId: result.id, priceTypeId: t.id, price: value }); setCompletedPrices(previous => ({ ...previous, [t.id]: value })) }))
      setSavingPrices(false)
      const failed = pending.filter((_, i) => outcomes[i].status === 'rejected')
      if (failed.length) { setPartialError('Послугу створено. Не вдалося зберегти ціни: ' + failed.map(t => t.name).join(', ') + '. Повторіть збереження або завершіть без решти цін.'); notify({ severity: 'error', message: 'Послугу створено, частину цін не збережено.' }); return }
      setPartialError('')
      notify({ severity: 'success', message: product ? 'Зміни збережено.' : 'Товар або послугу створено.' })
      saved(result)
    } catch (error) {
      setSavingPrices(false)
      if (error instanceof ApiError) {
        for (const [key, messages] of Object.entries(error.fieldErrors)) {
          const field = fieldNames.find(name => name.toLowerCase() === key.replace(/^\$\./, '').toLowerCase())
          if (field) setError(field, { message: messages.join(' ') })
        }
        if (error.detail?.includes('Unit must belong')) setError('unitId', { message: 'Одиниця виміру має належати поточній організації.' })
        else if (error.detail?.includes('purchase lines')) setError('itemType', { message: 'Тип не можна змінити: товар уже використано в закупівлях.' })
        else if (error.detail?.includes('sale lines')) setError('itemType', { message: 'Тип не можна змінити: номенклатуру вже використано в продажах.' })
        else if (error.detail?.includes('ProductPrice')) setError('itemType', { message: 'Тип не можна змінити: для номенклатури задано ціни.' })
      }
      notify({ severity: 'error', message: error instanceof Error ? error.message : 'Не вдалося зберегти товар.' })
    }
  })
  return <>
    <Paper component="form" noValidate variant="outlined" onSubmit={event => { void submit(event) }} sx={{ overflow: 'visible' }}>
      <Box sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider' }}><Typography variant="h2" component="h2">Основні дані</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Заповніть код, назву та одиницю виміру.</Typography></Box>
      <Box component="fieldset" disabled={busy || Boolean(created)} sx={{ border: 0, m: 0, p: 2, minWidth: 0 }}>
        {save.isError && <Alert severity="error" sx={{ mb: 3 }}>{save.error.message}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
          <Controller name="itemType" control={control} render={({ field }) => <FormControl size="small" fullWidth error={Boolean(errors.itemType)}><InputLabel id="product-item-type">Тип *</InputLabel><Select labelId="product-item-type" label="Тип *" {...field}><MenuItem value="GOODS">Товар</MenuItem><MenuItem value="SERVICE">Послуга</MenuItem></Select><FormHelperText>{errors.itemType?.message ?? (itemType === 'SERVICE' ? 'Послуга не має складських залишків і партій' : 'Товар підтримує партії та складський облік')}</FormHelperText></FormControl>} />
          <TextField label="Код" required fullWidth {...register('code')} error={Boolean(errors.code)} helperText={errors.code?.message} />
          <TextField label="Назва" required fullWidth {...register('name')} error={Boolean(errors.name)} helperText={errors.name?.message} sx={{ gridColumn: '1 / -1' }} />
          <Controller name="categoryId" control={control} render={({field})=><TextField {...field} select label="Категорія" disabled={categories.isPending} error={Boolean(errors.categoryId)} helperText={errors.categoryId?.message??(categories.isError?'Не вдалося завантажити категорії. Поточне значення збережеться.':undefined)}><MenuItem value="">Не вибрано</MenuItem>{field.value&&!categories.data?.some(c=>c.id===field.value)&&<MenuItem value={field.value} disabled>Попередня категорія (назву не задано)</MenuItem>}{directoryTree(categories.data??[]).filter(n=>n.node.active||n.node.id===field.value).map(n=><MenuItem key={n.node.id} value={n.node.id}>{n.path}{n.node.active?'':' (неактивна)'}</MenuItem>)}</TextField>}/>

          <Box>
            <Controller name="unitId" control={control} render={({ field }) => <FormControl size="small" fullWidth required error={Boolean(errors.unitId)} disabled={units.isPending || units.isError}><InputLabel id="product-unit">Одиниця виміру</InputLabel><Select labelId="product-unit" label="Одиниця виміру *" {...field}>
              <MenuItem value="" disabled>Виберіть одиницю</MenuItem>{selectedUnitUnknown && <MenuItem value={unitId} disabled>{units.isPending ? 'Завантаження…' : 'Поточна одиниця (назву не вказано)'}</MenuItem>}{unitOptions.filter(unit=>unit.active!==false||unit.id===unitId).map(unit => <MenuItem key={unit.id} value={unit.id}>{unit.name}</MenuItem>)}
            </Select><FormHelperText>{errors.unitId?.message ?? (units.isPending ? 'Завантаження одиниць…' : unitOptions.length ? undefined : 'Додайте одиницю виміру для створення товару.')}</FormHelperText></FormControl>} />
            {units.isError && <Alert severity="warning" sx={{ mt: 1 }} action={<Button color="inherit" onClick={() => { void units.refetch() }}>Повторити</Button>}>Не вдалося завантажити одиниці виміру.</Alert>}
            <Button size="small" startIcon={<Icon name="plus" sx={{ fontSize: 16 }} />} onClick={() => setUnitDialog(true)} sx={{ mt: 0.5 }}>Додати одиницю</Button>
          </Box>
          <TextField label="Штрихкод" fullWidth {...register('barcode')} error={Boolean(errors.barcode)} helperText={errors.barcode?.message} />
          <Box sx={{ display: 'flex', alignItems: 'flex-start' }}><Controller name="active" control={control} render={({ field }) => <FormControlLabel label="Активний" control={<Checkbox checked={field.value} onChange={field.onChange} slotProps={{ input: { ref: field.ref } }} />} />} /></Box>
          <TextField label="Опис" fullWidth multiline minRows={3} {...register('description')} error={Boolean(errors.description)} helperText={errors.description?.message} sx={{ gridColumn: '1 / -1' }} />
        </Box>
      </Box>
      {!product && itemType === 'SERVICE' && canPrice && <Box component="section" aria-label="Початкові ціни" sx={{ p: 2, borderTop: '1px solid', borderColor: 'divider' }}><Typography variant="h2" sx={{ mb: 1 }}>Початкові ціни</Typography><Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>Необов’язково. Порожнє поле залишає ціну незаданою; 0 — задана нульова ціна. UAH.</Typography>{initialTypes.isError ? <RequestErrorState error={initialTypes.error} retry={() => { void initialTypes.refetch() }} /> : <Stack spacing={2}>{(initialTypes.data ?? []).filter(t => t.active).map(t => <TextField key={t.id} label={'Початкова ціна · ' + t.name} type="number" value={initialPrices[t.id] ?? ''} onChange={e => setInitialPrices({ ...initialPrices, [t.id]: e.target.value })} disabled={busy} error={Boolean(priceErrors[t.id])} helperText={priceErrors[t.id] ?? 'UAH'} slotProps={{ htmlInput: { min: 0, step: '0.0001' } }} />)}</Stack>}</Box>}
      {partialError && <Alert severity="warning" sx={{ m: 3 }} action={created && <Button disabled={busy} onClick={() => saved(created)}>Завершити без решти цін</Button>}>{partialError}</Alert>}
      <DocumentActions><Button onClick={cancel} color="inherit" disabled={busy}>Скасувати</Button><Button type="submit" variant="contained" disabled={busy}>{busy ? 'Зберігаємо…' : created ? 'Повторити незбережені ціни' : product ? 'Зберегти зміни' : 'Створити'}</Button></DocumentActions>
    </Paper>
    {unitDialog && <CreateUnitDialog close={() => setUnitDialog(false)} created={unit => { setValue('unitId', unit.id, { shouldValidate: true, shouldDirty: true }); notify({ severity: 'success', message: 'Одиницю виміру додано.' }) }} />}
  </>
}
