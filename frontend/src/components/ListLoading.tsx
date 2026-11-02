import { Paper, Skeleton, Stack } from '@mui/material'

export function ListLoading({ label = 'Завантаження…', rows = 5 }: { label?: string; rows?: number }) {
  return <Paper variant="outlined" role="status" aria-label={label} sx={{ p: 3 }}><Stack spacing={2}>{Array.from({ length: rows }, (_, index) => <Skeleton key={index} variant="rounded" height={40} />)}</Stack></Paper>
}
