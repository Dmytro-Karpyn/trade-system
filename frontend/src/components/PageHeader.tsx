import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'

export function PageHeader({ title, description, actions, secondary = false }: {
  title: string
  description?: ReactNode
  actions?: ReactNode
  secondary?: boolean
}) {
  return <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{
    justifyContent: 'space-between', flexWrap: 'wrap', rowGap: 1.5,
    alignItems: { xs: 'stretch', sm: 'center' }, minWidth: 0,
  }}>
    <Box sx={{ minWidth: 0 }}>
      <Typography variant={secondary ? 'h2' : 'h1'} component={secondary ? 'h2' : 'h1'} sx={{ overflowWrap: 'anywhere' }}>{title}</Typography>
      {description && <Box sx={{ mt: 0.5, color: 'text.secondary', fontSize: 13 }}>{description}</Box>}
    </Box>
    {actions && <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1, flexShrink: 1 }}>{actions}</Stack>}
  </Stack>
}
