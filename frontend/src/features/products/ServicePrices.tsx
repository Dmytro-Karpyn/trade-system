import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useCurrentUser } from '../../auth/queries'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { usePriceTypes } from '../counterparties/queries'
import { MoneyAmount } from '../../components/MoneyAmount'
import { EmptyState } from '../../components/EmptyState'
import { useProductNotice } from './notices'
import { priceTextSchema, useSaveServicePrice, useServicePrices } from './service-pricing'

function PriceDialog({
  productId,
  type,
  close,
}: {
  productId: string
  type: { id: string; name: string; value?: number }
  close: () => void
}) {
  const save = useSaveServicePrice(),
    notify = useProductNotice()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<{ price: string }>({
    resolver: zodResolver(z.object({ price: priceTextSchema })),
    defaultValues: { price: type.value === undefined ? '' : String(type.value) },
  })
  const submit = handleSubmit(async (values) => {
    try {
      await save.mutateAsync({ productId, priceTypeId: type.id, price: Number(values.price) })
      notify({ severity: 'success', message: 'Ціну послуги збережено.' })
      close()
    } catch (e) {
      notify({ severity: 'error', message: e instanceof Error ? e.message : 'Не вдалося зберегти ціну.' })
    }
  })
  return (
    <Dialog open fullWidth maxWidth="xs" onClose={save.isPending ? undefined : close}>
      <DialogTitle>Ціна послуги · {type.name}</DialogTitle>
      <DialogContent>
        <Stack
          component="form"
          id="service-price-form"
          noValidate
          spacing={2}
          sx={{ pt: 1 }}
          onSubmit={(e) => {
            void submit(e)
          }}
        >
          <Typography variant="body2">Тип ціни: {type.name}</Typography>
          {save.isError && <Alert severity="error">{save.error.message}</Alert>}
          <TextField
            label="Ціна, UAH"
            type="number"
            required
            autoFocus
            {...register('price')}
            disabled={save.isPending}
            error={Boolean(errors.price)}
            helperText={errors.price?.message ?? 'Ціна за одиницю послуги в UAH.'}
            slotProps={{ htmlInput: { min: 0, step: '0.0001' } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={save.isPending}>
          Скасувати
        </Button>
        <Button type="submit" form="service-price-form" variant="contained" disabled={save.isPending}>
          Зберегти ціну
        </Button>
      </DialogActions>
    </Dialog>
  )
}
export function ServicePrices({ productId, compact = false }: { productId: string; compact?: boolean }) {
  const prices = useServicePrices(productId),
    types = usePriceTypes(),
    user = useCurrentUser().data
  const narrow = useMediaQuery(useTheme().breakpoints.down('md'))
  const mobile = compact || narrow
  const [editing, setEditing] = useState<{ id: string; name: string; value?: number } | null>(null)
  const rows = (types.data ?? [])
    .filter((t) => t.active)
    .map((t) => ({ ...t, value: prices.data?.find((p) => p.priceTypeId === t.id)?.unitPrice }))
  const edit = (row: (typeof rows)[number]) =>
    user?.permissions.includes('settings.manage') ? (
      <Button size="small" aria-label={`Редагувати ціну ${row.name}`} onClick={() => setEditing(row)}>
        {row.value === undefined ? 'Задати ціну' : 'Редагувати'}
      </Button>
    ) : null
  return (
    <Paper component="section" aria-label="Ціни послуги" variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={2}>
        <Typography variant="h2" component="h2">
          Ціни
        </Typography>
        <Typography variant="body2" color="text.secondary">
          UAH · Ціни застосовуються до нових рядків продажу. Збережені рекомендації в документах не
          змінюються.
        </Typography>
        {types.isPending || prices.isPending ? (
          <ListLoading label="Завантаження цін послуги" />
        ) : types.isError || prices.isError ? (
          <RequestErrorState
            error={types.error ?? prices.error}
            retry={() => {
              void types.refetch()
              void prices.refetch()
            }}
          />
        ) : !rows.length ? (
          <EmptyState title="Активних типів цін ще немає." />
        ) : mobile ? (
          <Stack component="ul" aria-label="Ціни послуги" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {rows.map((row) => (
              <Box component="li" key={row.id} sx={{ py: 1, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, borderBottom: '1px solid', borderColor: 'divider' }}>
                <Box sx={{ minWidth: 0 }}><Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>{row.name}</Typography><Typography variant="body2">{row.value === undefined ? 'Не задано' : <MoneyAmount amount={row.value} currency="UAH" />}</Typography></Box>
                {edit(row)}
              </Box>
            ))}
          </Stack>
        ) : (
          <Table aria-label="Ціни послуги">
            <TableHead>
              <TableRow>
                <TableCell>Тип ціни</TableCell>
                <TableCell align="right">Поточна ціна</TableCell>
                <TableCell>Дії</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={row.id}>
                  <TableCell>{row.name}</TableCell>
                  <TableCell align="right">{row.value === undefined ? 'Не задано' : <MoneyAmount amount={row.value} currency="UAH" />}</TableCell>
                  <TableCell>{edit(row)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Stack>
      {editing && <PriceDialog productId={productId} type={editing} close={() => setEditing(null)} />}
    </Paper>
  )
}
