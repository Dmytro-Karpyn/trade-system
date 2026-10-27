import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { useCreateLocation } from './queries'
import type { Location } from './contracts'

const schema = z.object({ name: z.string().trim().min(1, 'Введіть назву складу.') })
export function CreateLocationDialog({ close, created }: { close: () => void; created: (location: Location) => void }) {
  const mutation = useCreateLocation()
  const { register, handleSubmit, formState: { errors } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { name: '' } })
  const submit = handleSubmit(async input => { try { created(await mutation.mutateAsync(input.name)); close() } catch { /* The dialog retains input and shows the API error below. */ } })
  return <Dialog open onClose={mutation.isPending ? undefined : close} fullWidth maxWidth="xs"><DialogTitle>Новий склад</DialogTitle><form onSubmit={event => { void submit(event) }} noValidate><DialogContent sx={{ pt: '12px !important' }}>{mutation.isError && <Alert severity="error" sx={{ mb: 2 }}>{mutation.error.message}</Alert>}<TextField label="Назва складу" autoFocus fullWidth disabled={mutation.isPending} {...register('name')} error={Boolean(errors.name)} helperText={errors.name?.message} /></DialogContent><DialogActions sx={{ px: 3, pb: 2.5 }}><Button onClick={close} disabled={mutation.isPending}>Скасувати</Button><Button type="submit" variant="contained" disabled={mutation.isPending}>{mutation.isPending ? 'Додаємо…' : 'Додати склад'}</Button></DialogActions></form></Dialog>
}
