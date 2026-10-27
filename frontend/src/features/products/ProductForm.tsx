import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Checkbox, FormControl, FormControlLabel, FormHelperText, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography } from '@mui/material'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { ApiError } from '../../api/client'
import { Icon } from '../../components/Icon'
import { CreateUnitDialog } from './CreateUnitDialog'
import { productDefaults, productFormSchema, toProductInput } from './contracts'
import type { Product, ProductFormValues } from './contracts'
import { useSaveProduct, useUnits } from './queries'
import { useProductNotice } from './notices'

const fieldNames: (keyof ProductFormValues)[] = ['itemType', 'code', 'name', 'unitId', 'barcode', 'description', 'active']
export function ProductForm({ product, saved, cancel }: { product?: Product; saved: (product: Product) => void; cancel: () => void }) {
  const [unitDialog, setUnitDialog] = useState(false)
  const units = useUnits()
  const save = useSaveProduct(product?.id)
  const notify = useProductNotice()
  const { control, register, handleSubmit, setValue, setError, formState: { errors } } = useForm<ProductFormValues>({
    resolver: zodResolver(productFormSchema),
    defaultValues: product ? { itemType: product.itemType, code: product.code, name: product.name, unitId: product.unitId, barcode: product.barcode ?? '', description: product.description ?? '', active: product.active } : productDefaults,
  })
  const itemType = useWatch({ control, name: 'itemType' })
  const unitId = useWatch({ control, name: 'unitId' })
  const unitOptions = units.data ?? []
  const selectedUnitUnknown = unitId && !unitOptions.some(unit => unit.id === unitId)
  const submit = handleSubmit(async values => {
    try {
      const result = await save.mutateAsync(toProductInput(values, product))
      notify({ severity: 'success', message: product ? 'Зміни збережено.' : 'Товар або послугу створено.' })
      saved(result)
    } catch (error) {
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
    <Paper component="form" noValidate variant="outlined" onSubmit={event => { void submit(event) }} sx={{ overflow: 'hidden' }}>
      <Box sx={{ p: { xs: 2.5, md: 3 }, borderBottom: '1px solid', borderColor: 'divider' }}><Typography variant="h2" component="h2">Основні дані</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Заповніть код, назву та одиницю виміру.</Typography></Box>
      <Box component="fieldset" disabled={save.isPending} sx={{ border: 0, m: 0, p: { xs: 2.5, md: 3 }, minWidth: 0 }}>
        {save.isError && <Alert severity="error" sx={{ mb: 3 }}>{save.error.message}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 3 }}>
          <Controller name="itemType" control={control} render={({ field }) => <FormControl size="small" fullWidth error={Boolean(errors.itemType)}><InputLabel id="product-item-type">Тип *</InputLabel><Select labelId="product-item-type" label="Тип *" {...field}><MenuItem value="GOODS">Товар</MenuItem><MenuItem value="SERVICE">Послуга</MenuItem></Select><FormHelperText>{errors.itemType?.message ?? (itemType === 'SERVICE' ? 'Послуга не має складських залишків і партій' : 'Товар підтримує партії та складський облік')}</FormHelperText></FormControl>} />
          <TextField label="Код" required fullWidth {...register('code')} error={Boolean(errors.code)} helperText={errors.code?.message} />
          <TextField label="Назва" required fullWidth {...register('name')} error={Boolean(errors.name)} helperText={errors.name?.message} sx={{ gridColumn: '1 / -1' }} />
          <Box><FormControl fullWidth size="small" disabled><InputLabel id="product-category" shrink>Категорія</InputLabel><Select labelId="product-category" label="Категорія" notched value={product?.categoryId ? 'existing' : ''} displayEmpty><MenuItem value="">Не вибрано</MenuItem>{product?.categoryId && <MenuItem value="existing">Поточна категорія (назву не вказано)</MenuItem>}</Select><FormHelperText>Довідник категорій поки недоступний.</FormHelperText></FormControl></Box>
          <Box>
            <Controller name="unitId" control={control} render={({ field }) => <FormControl size="small" fullWidth required error={Boolean(errors.unitId)} disabled={units.isPending || units.isError}><InputLabel id="product-unit">Одиниця виміру</InputLabel><Select labelId="product-unit" label="Одиниця виміру *" {...field}>
              <MenuItem value="" disabled>Виберіть одиницю</MenuItem>{selectedUnitUnknown && <MenuItem value={unitId} disabled>{units.isPending ? 'Завантаження…' : 'Поточна одиниця (назву не вказано)'}</MenuItem>}{unitOptions.map(unit => <MenuItem key={unit.id} value={unit.id}>{unit.name}</MenuItem>)}
            </Select><FormHelperText>{errors.unitId?.message ?? (units.isPending ? 'Завантаження одиниць…' : unitOptions.length ? undefined : 'Додайте одиницю виміру для створення товару.')}</FormHelperText></FormControl>} />
            {units.isError && <Alert severity="warning" sx={{ mt: 1 }} action={<Button color="inherit" onClick={() => { void units.refetch() }}>Повторити</Button>}>Не вдалося завантажити одиниці виміру.</Alert>}
            <Button size="small" startIcon={<Icon name="plus" sx={{ fontSize: 16 }} />} onClick={() => setUnitDialog(true)} sx={{ mt: 0.5 }}>Додати одиницю</Button>
          </Box>
          <TextField label="Штрихкод" fullWidth {...register('barcode')} error={Boolean(errors.barcode)} helperText={errors.barcode?.message} />
          <Box sx={{ display: 'flex', alignItems: 'flex-start' }}><Controller name="active" control={control} render={({ field }) => <FormControlLabel label="Активний" control={<Checkbox checked={field.value} onChange={field.onChange} slotProps={{ input: { ref: field.ref } }} />} />} /></Box>
          <TextField label="Опис" fullWidth multiline minRows={3} {...register('description')} error={Boolean(errors.description)} helperText={errors.description?.message} sx={{ gridColumn: '1 / -1' }} />
        </Box>
      </Box>
      <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end', p: 2.5, borderTop: '1px solid', borderColor: 'divider', bgcolor: '#fafbfd' }}><Button onClick={cancel} color="inherit" disabled={save.isPending}>Скасувати</Button><Button type="submit" variant="contained" disabled={save.isPending}>{save.isPending ? 'Зберігаємо…' : product ? 'Зберегти зміни' : 'Створити'}</Button></Stack>
    </Paper>
    {unitDialog && <CreateUnitDialog close={() => setUnitDialog(false)} created={unit => { setValue('unitId', unit.id, { shouldValidate: true, shouldDirty: true }); notify({ severity: 'success', message: 'Одиницю виміру додано.' }) }} />}
  </>
}
