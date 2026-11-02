import { Chip, Stack } from '@mui/material'
import { tokens } from '../../theme/tokens'
import type { Counterparty } from './contracts'

export function PartyBadges({ party }: { party: Counterparty }) {
  return <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
    {party.isCustomer && <Chip size="small" label="Покупець" sx={{ bgcolor: tokens.statuses.neutral.background, color: tokens.statuses.neutral.color }} />}
    {party.isSupplier && <Chip size="small" label="Постачальник" sx={{ bgcolor: tokens.statuses.neutral.background, color: tokens.statuses.neutral.color }} />}
    {party.isEmployee && <Chip size="small" label="Працівник" variant="outlined" />}
    {party.isSystem && <Chip size="small" label="Системний" sx={{ bgcolor: tokens.statuses.neutral.background, color: tokens.statuses.neutral.color }} />}
  </Stack>
}
