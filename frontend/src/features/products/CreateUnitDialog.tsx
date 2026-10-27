import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../../api/client'
import { useCreateUnit } from './queries'
import type { Unit } from './contracts'

const schema = z.object({ name: z.string().trim().min(1, 'Введіть назву одиниці виміру.') })
export function CreateUnitDialog({ close, created }: { close: () => void; created: (unit: Unit) => void }) {
  const mutation = useCreateUnit()
  const { register, handleSubmit, setError, formState: { errors } } = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { name: '' } })
  const submit = handleSubmit(async input => {
    try { created(await mutation.mutateAsync(input.name)); close() }
    catch (error) {
      if (error instanceof ApiError) {
        const message = error.fieldErrors.Name ?? error.fieldErrors.name
        if (message) setError('name', { message: message.join(' ') })
      }
    }
  })
  return <Dialog open onClose={mutation.isPending ? undefined : close} fullWidth maxWidth="xs">
    <DialogTitle>Нова одиниця виміру</DialogTitle>
    <form onSubmit={event => { void submit(event) }} noValidate>
      <DialogContent sx={{ pt: '12px !important' }}>
        {mutation.isError && <Alert severity="error" sx={{ mb: 2 }}>{mutation.error instanceof ApiError && mutation.error.status === 409 ? 'Одиниця з такою назвою вже існує.' : mutation.error.message}</Alert>}
        <TextField label="Назва одиниці виміру" autoFocus fullWidth disabled={mutation.isPending} {...register('name')} error={Boolean(errors.name)} helperText={errors.name?.message} />
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}><Button onClick={close} disabled={mutation.isPending}>Скасувати</Button><Button type="submit" variant="contained" disabled={mutation.isPending}>{mutation.isPending ? 'Додаємо…' : 'Додати одиницю'}</Button></DialogActions>
    </form>
  </Dialog>
}
