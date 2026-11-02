import { Chip } from '@mui/material'
import { tokens } from '../../theme/tokens'
import type { Product } from './contracts'

export function TypeBadge({ type }: { type: Product['itemType'] }) {
  return <Chip size="small" label={type === 'GOODS' ? 'Товар' : 'Послуга'} sx={{ bgcolor: tokens.statuses.neutral.background, color: tokens.statuses.neutral.color }} />
}
