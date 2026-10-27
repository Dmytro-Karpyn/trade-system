import { useState } from 'react'
import { Autocomplete, Box, Button, CircularProgress, TextField, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { usePurchaseProductSearch } from './queries'

export interface SelectedProduct { id: string; name: string; code: string }
export function ProductSelector({ index, value, change, disabled, locked, error }: { index: number; value: SelectedProduct | null; change: (product: SelectedProduct | null) => void; disabled: boolean; locked: boolean; error?: string }) {
  const user = useCurrentUser().data
  const [open, setOpen] = useState(false)
  const [input, setInput] = useState('')
  const search = useDebouncedValue(input)
  const permitted = Boolean(user && hasAnyPermission(user, ['product.view']))
  const products = usePurchaseProductSearch(search, open && !disabled && !locked && permitted)
  return <Autocomplete<SelectedProduct> open={open && !disabled && !locked && permitted} onOpen={() => setOpen(true)} onClose={() => setOpen(false)} value={value} onChange={(_, product) => change(product)} onInputChange={(_, text, reason) => { if (reason === 'input') setInput(text); else if (reason === 'clear') setInput('') }} options={(products.data ?? []).filter(product => product.active && product.itemType === 'GOODS')} filterOptions={options => options} isOptionEqualToValue={(option, selected) => option.id === selected.id} getOptionLabel={option => option.name} loading={products.isFetching} loadingText="Завантаження товарів…" disabled={disabled || locked || !permitted} fullWidth
    renderOption={(props, option) => { const { key, ...rest } = props; return <Box component="li" key={key} {...rest}><Box><Typography variant="body2">{option.name}</Typography><Typography variant="caption" color="text.secondary">{option.code}</Typography></Box></Box> }}
    noOptionsText={<Box><Typography variant="body2">{products.isError ? 'Не вдалося завантажити товари.' : 'Товарів не знайдено.'}</Typography>{products.isError && <Button size="small" onMouseDown={event => event.preventDefault()} onClick={() => { void products.refetch() }}>Повторити</Button>}{user && hasAnyPermission(user, ['product.create']) && <Button component={Link} to="/products/new" target="_blank" rel="noopener" size="small">Створити товар</Button>}</Box>}
    renderInput={params => <TextField {...params} label={`Товар ${index + 1}`} placeholder="Назва, код або штрихкод" error={Boolean(error)} helperText={error ?? (locked ? 'Товар не можна змінити після створення партії.' : !permitted ? 'Потрібне право перегляду товарів.' : undefined)} slotProps={{ ...params.slotProps, input: { ...params.slotProps.input, endAdornment: <>{products.isFetching && <CircularProgress size={16} aria-label="Завантаження товарів" />}{params.slotProps.input.endAdornment}</> } }} />} />
}
