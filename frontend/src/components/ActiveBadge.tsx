import { Chip } from '@mui/material'

export function ActiveBadge({ active }: { active: boolean }) {
  return <Chip size="small" variant="outlined" label={active ? 'Активний' : 'Неактивний'} sx={{ color: active ? 'success.main' : 'text.secondary', borderColor: active ? '#c8e6da' : 'divider', bgcolor: active ? '#f2faf6' : 'background.default' }} />
}
