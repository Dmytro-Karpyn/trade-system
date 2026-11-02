import { Chip } from '@mui/material'
import { tokens } from '../theme/tokens'
// A settlement sign describes who owes whom; it does not represent success/error.
export function BalanceBadge({ balance }: { balance: number }) {
  return <Chip size="small" variant="outlined" label={balance > 0 ? 'Нам винні' : balance < 0 ? 'Ми винні' : 'Нульовий баланс'} sx={{ bgcolor: tokens.statuses.neutral.background, color: tokens.statuses.neutral.color, borderColor: tokens.statuses.neutral.border }} />
}
