import { useState } from 'react'
import { Autocomplete, Box, CircularProgress, TextField, Typography } from '@mui/material'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { useSaleProducts } from './queries'

export interface SaleProduct { id: string; name: string; code: string; itemType: 'GOODS' | 'SERVICE' }
export function SaleProductSelector({ label, value, change, disabled = false, goodsOnly = false, error }: { label: string; value: SaleProduct | null; change: (product: SaleProduct | null) => void; disabled?: boolean; goodsOnly?: boolean; error?: string }) {
  const user = useCurrentUser().data; const [open, setOpen] = useState(false); const [input, setInput] = useState('')
  const permitted = Boolean(user && hasAnyPermission(user, ['product.view']))
  const products = useSaleProducts(useDebouncedValue(input), open && !disabled && permitted, goodsOnly)
  return <Autocomplete<SaleProduct> fullWidth open={open && !disabled && permitted} onOpen={() => setOpen(true)} onClose={() => setOpen(false)} value={value} onChange={(_, value) => change(value)} onInputChange={(_, value, reason) => { if (reason === 'input') setInput(value); else if (reason === 'clear') setInput('') }} options={(products.data ?? []).filter(product => product.active && (!goodsOnly || product.itemType === 'GOODS'))} filterOptions={options => options} getOptionLabel={product => product.name} isOptionEqualToValue={(option, value) => option.id === value.id} disabled={disabled || !permitted} loading={products.isFetching} loadingText="Завантаження номенклатури…" noOptionsText={products.isError ? 'Не вдалося завантажити номенклатуру.' : 'Номенклатури не знайдено.'}
    renderOption={(props, product) => { const { key, ...rest } = props; return <Box component="li" key={key} {...rest}><Box><Typography variant="body2">{product.name}</Typography><Typography variant="caption" color="text.secondary">{product.code} · {product.itemType === 'GOODS' ? 'Товар' : 'Послуга'}</Typography></Box></Box> }}
    renderInput={params => <TextField {...params} label={label} placeholder="Назва, код або штрихкод" error={Boolean(error)} helperText={error ?? (!permitted ? 'Потрібне право перегляду номенклатури.' : undefined)} slotProps={{ ...params.slotProps, input: { ...params.slotProps.input, endAdornment: <>{products.isFetching && <CircularProgress size={16} aria-label="Завантаження номенклатури" />}{params.slotProps.input.endAdornment}</> } }} />} />
}
