import { Chip, Stack } from '@mui/material'
import type { Counterparty } from './contracts'

export function PartyBadges({ party }: { party: Counterparty }) {
  return <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', rowGap: 0.5 }}>
    {party.isCustomer && <Chip size="small" label="Покупець" sx={{ bgcolor: '#edf3ff', color: '#355eab' }} />}
    {party.isSupplier && <Chip size="small" label="Постачальник" sx={{ bgcolor: '#f2effb', color: '#7754a5' }} />}
    {party.isEmployee && <Chip size="small" label="Працівник" variant="outlined" />}
    {party.isSystem && <Chip size="small" label="Системний" sx={{ bgcolor: '#fff3eb', color: '#986523' }} />}
  </Stack>
}
