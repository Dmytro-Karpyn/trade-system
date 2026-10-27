import { Chip } from '@mui/material'
import type { Product } from './contracts'

export function TypeBadge({ type }: { type: Product['itemType'] }) {
  return <Chip size="small" label={type === 'GOODS' ? 'Товар' : 'Послуга'} sx={{ bgcolor: type === 'GOODS' ? '#edf3ff' : '#f2effb', color: type === 'GOODS' ? '#355eab' : '#7754a5' }} />
}
