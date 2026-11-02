import { Chip } from '@mui/material'
import { tokens } from '../theme/tokens'

export type Status = 'DRAFT' | 'POSTED' | 'WAITING' | 'PENDING' | 'ERROR' | 'PROBLEM' | 'ACTIVE' | 'INACTIVE'
const statuses = {
  DRAFT: ['Чернетка', 'neutral'], POSTED: ['Проведено', 'success'],
  WAITING: ['Очікує', 'warning'], PENDING: ['В обробці', 'warning'],
  ERROR: ['Помилка', 'error'], PROBLEM: ['Потребує уваги', 'error'],
  ACTIVE: ['Активний', 'success'], INACTIVE: ['Неактивний', 'inactive'],
} as const
export function StatusBadge({ status }: { status: Status }) {
  const [label, tone] = statuses[status]
  const style = tokens.statuses[tone]
  return <Chip data-status={status} size="small" variant="outlined" label={label} sx={{ bgcolor: style.background, color: style.color, borderColor: style.border, height: 24 }} />
}
