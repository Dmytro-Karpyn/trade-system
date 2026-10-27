import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, Checkbox, FormControl, FormControlLabel, FormHelperText, InputLabel, MenuItem, Paper, Select, Stack, TextField, Typography } from '@mui/material'
import { Controller, useForm } from 'react-hook-form'
import { ApiError } from '../../api/client'
import { legalTypes, partyDefaults, partyFormSchema, partyInput } from './contracts'
import type { Counterparty, PartyFormValues } from './contracts'
import { usePriceTypes, useSaveCounterparty } from './queries'
import { usePartyNotice } from './notices'

export function CounterpartyForm({ party, saved, cancel }: { party?: Counterparty; saved: (party: Counterparty) => void; cancel: () => void }) {
  const prices = usePriceTypes()
  const save = useSaveCounterparty(party?.id)
  const notify = usePartyNotice()
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<PartyFormValues>({
    resolver: zodResolver(partyFormSchema),
    defaultValues: party ? { name: party.name, legalType: party.legalType ?? '', taxId: party.taxId ?? '', registrationCode: party.registrationCode ?? '', phone: party.phone ?? '', email: party.email ?? '', address: party.address ?? '', defaultPriceTypeId: party.defaultPriceTypeId ?? '', isCustomer: party.isCustomer, isSupplier: party.isSupplier, isEmployee: party.isEmployee, active: party.active, note: party.note ?? '' } : partyDefaults,
  })
  const submit = handleSubmit(async values => {
    try {
      const result = await save.mutateAsync(partyInput(values, party))
      notify({ severity: 'success', message: party ? 'Зміни контрагента збережено.' : 'Контрагента створено.' }); saved(result)
    } catch (error) {
      if (error instanceof ApiError) {
        for (const [key, messages] of Object.entries(error.fieldErrors)) {
          const field = (Object.keys(partyDefaults) as (keyof PartyFormValues)[]).find(name => name.toLowerCase() === key.replace(/^\$\./, '').toLowerCase())
          if (field) setError(field, { message: messages.join(' ') })
        }
      }
      notify({ severity: 'error', message: error instanceof Error ? error.message : 'Не вдалося зберегти контрагента.' })
    }
  })
  const text = (name: 'name' | 'taxId' | 'registrationCode' | 'phone' | 'email' | 'address' | 'note', label: string, wide = false, multiline = false) => <TextField key={name} label={label} required={name === 'name'} type={name === 'email' ? 'email' : name === 'phone' ? 'tel' : 'text'} fullWidth multiline={multiline} minRows={multiline ? 3 : undefined} {...register(name)} disabled={save.isPending} error={Boolean(errors[name])} helperText={errors[name]?.message} sx={wide ? { gridColumn: '1 / -1' } : undefined} />
  return <Paper component="form" noValidate variant="outlined" onSubmit={event => { void submit(event) }} sx={{ overflow: 'hidden' }}>
    <Box sx={{ px: { xs: 2.5, md: 3 }, py: 2.5, borderBottom: '1px solid', borderColor: 'divider' }}><Typography variant="h2" component="h2">Основні дані</Typography><Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>Вкажіть назву, контакти та ролі контрагента.</Typography></Box>
    <Box sx={{ p: { xs: 2.5, md: 3 } }}>
      {save.isError && <Alert severity="error" sx={{ mb: 3 }}>{save.error.message}</Alert>}
      {party?.isSystem && <Alert severity="info" sx={{ mb: 3 }}>Системний контрагент. Системну ознаку буде збережено.</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2.5 }}>
        {text('name', 'Назва', true)}
        <Controller name="legalType" control={control} render={({ field }) => <FormControl fullWidth size="small" disabled={save.isPending}><InputLabel id="party-legal-type" shrink>Тип контрагента</InputLabel><Select labelId="party-legal-type" label="Тип контрагента" displayEmpty notched {...field}><MenuItem value="">Не вказано</MenuItem>{Object.entries(legalTypes).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}</Select></FormControl>} />
        <Controller name="defaultPriceTypeId" control={control} render={({ field }) => <FormControl size="small" fullWidth disabled={save.isPending || !prices.permitted || prices.isPending || prices.isError} error={Boolean(errors.defaultPriceTypeId)}><InputLabel id="party-price-type" shrink>Тип ціни за замовчуванням</InputLabel><Select labelId="party-price-type" label="Тип ціни за замовчуванням" displayEmpty notched {...field}><MenuItem value="">Не вибрано</MenuItem>{field.value && !prices.data?.some(type => type.id === field.value) && <MenuItem value={field.value} disabled>Поточний тип ціни (назва недоступна)</MenuItem>}{prices.data?.map(type => <MenuItem key={type.id} value={type.id}>{type.name}{type.active ? '' : ' (неактивний)'}</MenuItem>)}</Select><FormHelperText>{errors.defaultPriceTypeId?.message ?? (!prices.permitted ? 'Немає доступу до довідника типів цін.' : prices.isPending ? 'Завантаження типів цін…' : !prices.data?.length ? 'Типів цін поки немає. Поле необов’язкове.' : undefined)}</FormHelperText></FormControl>} />
        {text('taxId', 'ІПН')}{text('registrationCode', 'Реєстраційний код')}{text('phone', 'Телефон')}{text('email', 'Email')}{text('address', 'Адреса', true)}
        <Box sx={{ gridColumn: '1 / -1' }}><Typography variant="subtitle1" sx={{ mb: 1 }}>Ролі контрагента</Typography><Stack direction="row" spacing={2} sx={{ flexWrap: 'wrap', rowGap: 1 }}>{([['isCustomer', 'Покупець'], ['isSupplier', 'Постачальник'], ['isEmployee', 'Працівник']] as const).map(([name, label]) => <Controller key={name} name={name} control={control} render={({ field }) => <FormControlLabel label={label} control={<Checkbox disabled={save.isPending} checked={field.value} onChange={field.onChange} slotProps={{ input: { ref: field.ref } }} />} />} />)}</Stack><FormHelperText>Можна вибрати кілька ролей одночасно.</FormHelperText></Box>
        <Controller name="active" control={control} render={({ field }) => <FormControlLabel label="Активний" control={<Checkbox disabled={save.isPending} checked={field.value} onChange={field.onChange} slotProps={{ input: { ref: field.ref } }} />} />} />
        {text('note', 'Примітка', true, true)}
      </Box>
      {prices.permitted && prices.isError && <Alert severity="warning" sx={{ mt: 2 }} action={<Button onClick={() => { void prices.refetch() }}>Повторити</Button>}>Не вдалося завантажити типи цін. Поточне значення буде збережено.</Alert>}
    </Box>
    <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'flex-end', p: 2.5, borderTop: '1px solid', borderColor: 'divider', bgcolor: '#fafbfd' }}><Button color="inherit" onClick={cancel} disabled={save.isPending}>Скасувати</Button><Button variant="contained" type="submit" disabled={save.isPending}>{save.isPending ? 'Зберігаємо…' : party ? 'Зберегти зміни' : 'Створити контрагента'}</Button></Stack>
  </Paper>
}
