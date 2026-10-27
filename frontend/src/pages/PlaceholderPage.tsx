import { Chip, Paper, Stack, Typography } from '@mui/material'
import type { NavigationItem } from '../routes/navigation'
import { EmptyState } from '../components/EmptyState'

export function PlaceholderPage({ item }: { item: NavigationItem }) {
  return <Stack spacing={3}>
    <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}><Typography variant="h1" component="h1">{item.title}</Typography><Chip label="Незабаром" size="small" variant="outlined" /></Stack>
    <Paper variant="outlined"><EmptyState icon={item.icon} title="Розділ буде реалізовано наступним етапом" /></Paper>
  </Stack>
}
