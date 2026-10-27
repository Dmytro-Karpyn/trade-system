import { Paper, Skeleton, Stack } from '@mui/material'

export function ListLoading({ label = 'Завантаження…' }: { label?: string }) {
  return <Paper variant="outlined" role="status" aria-label={label} sx={{ p: 3 }}><Stack spacing={2}>{[0, 1, 2, 3, 4].map(index => <Skeleton key={index} variant="rounded" height={40} />)}</Stack></Paper>
}
