import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, Switch, TextField } from '@mui/material'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ApiError } from '../../api/client'
import { currencyName } from '../purchases/contracts'
import { accountFormSchema, accountTypes, moneyError, uahId } from './contracts'
import type { AccountOverview, AccountValues } from './contracts'
import { useMoneyNotice } from './notices'
import { useAccountMutation, useMoneyLookups } from './queries'

export function AccountDialog({ account, close }: { account?: AccountOverview; close: () => void }) {
  const mutation = useAccountMutation(); const lookups = useMoneyLookups(); const notify = useMoneyNotice(); const [error, setErrorState] = useState<unknown>(null)
  const { control, register, handleSubmit, setError, formState: { errors } } = useForm<AccountValues>({ resolver: zodResolver(accountFormSchema), defaultValues: account ? { name: account.account.name, accountType: account.account.accountType, currencyId: account.account.currencyId, active: account.account.active } : { name: '', accountType: 'CASH', currencyId: uahId, active: true } })
  const save = handleSubmit(async input => {
    try { await mutation.mutateAsync({ id: account?.account.id, input }); notify({ severity: 'success', message: 'Рахунок збережено.' }); close() }
    catch (caught) { setErrorState(caught); notify({ severity: 'error', message: moneyError(caught) }); if (caught instanceof ApiError) for (const [key, values] of Object.entries(caught.fieldErrors)) { const field = key.replace(/^[A-Z]/, x => x.toLowerCase()); if (['name', 'accountType', 'currencyId'].includes(field)) setError(field as 'name', { message: values.join(' ') }) } }
  })
  const currencies = [...new Set([...(lookups.data?.currencyIds ?? [uahId]), ...(account ? [account.account.currencyId] : [])])]
  return <Dialog open onClose={mutation.isPending ? undefined : close} fullWidth maxWidth="sm"><DialogTitle>{account ? 'Редагувати рахунок' : 'Новий рахунок'}</DialogTitle><DialogContent><Stack component="form" id="money-account-form" noValidate spacing={2.5} onSubmit={event => { void save(event) }} sx={{ pt: 1 }}>{error != null && <Alert severity="error">{moneyError(error)}</Alert>}<TextField label="Назва рахунку" autoFocus required {...register('name')} disabled={mutation.isPending} error={Boolean(errors.name)} helperText={errors.name?.message} /><Controller name="accountType" control={control} render={({ field }) => <TextField {...field} label="Тип рахунку" select disabled={mutation.isPending}>{Object.entries(accountTypes).map(([value, name]) => <MenuItem value={value} key={value}>{name} ({value})</MenuItem>)}</TextField>} /><Controller name="currencyId" control={control} render={({ field }) => <TextField {...field} label="Валюта рахунку" select required disabled={mutation.isPending || account?.account.currencyLocked || lookups.isPending} error={Boolean(errors.currencyId)} helperText={errors.currencyId?.message ?? (account?.account.currencyLocked ? 'Валюту використаного рахунку змінити не можна.' : 'Валюти з наявних операцій організації; UAH доступна завжди.')} >{currencies.map(id => <MenuItem value={id} key={id}>{currencyName(id, currencies)}</MenuItem>)}</TextField>} /><Controller name="active" control={control} render={({ field }) => <FormControlLabel label="Активний" control={<Switch checked={field.value} onChange={field.onChange} disabled={mutation.isPending} />} />} />{lookups.isError && <Alert severity="error" action={<Button onClick={() => { void lookups.refetch() }}>Повторити</Button>}>Не вдалося завантажити валюти.</Alert>}</Stack></DialogContent><DialogActions><Button disabled={mutation.isPending} onClick={close}>Скасувати</Button><Button type="submit" form="money-account-form" variant="contained" disabled={mutation.isPending || lookups.isPending || lookups.isError}>{mutation.isPending ? 'Зберігаємо…' : 'Зберегти рахунок'}</Button></DialogActions></Dialog>
}
