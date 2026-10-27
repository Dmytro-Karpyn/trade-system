import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { EmptyState } from './EmptyState'

export function LoadingState() {
  return <Stack role="status" aria-label="Завантаження застосунку" spacing={2} sx={{ alignItems: 'center', justifyContent: 'center', minHeight: '70dvh' }}><CircularProgress size={28} /><Typography color="text.secondary" variant="body2">Завантажуємо робочий простір…</Typography></Stack>
}

export function ForbiddenState() {
  return <Box role="alert"><EmptyState icon="lock" title="Недостатньо прав" description="У вас немає доступу до цього розділу. Зверніться до адміністратора, якщо доступ потрібен для роботи." action={<Button component={Link} to="/" variant="outlined" sx={{ mt: 1 }}>На головну</Button>} /></Box>
}

export function RequestErrorState({ error, retry }: { error: unknown; retry: () => void }) {
  if (error instanceof ApiError && error.status === 403) return <ForbiddenState />
  return <Stack spacing={2} sx={{ maxWidth: 560, mx: 'auto', py: 8, px: 3 }}><Alert severity="error">{error instanceof Error ? error.message : 'Не вдалося завантажити дані.'}</Alert><Button onClick={retry} variant="outlined">Спробувати ще раз</Button></Stack>
}
