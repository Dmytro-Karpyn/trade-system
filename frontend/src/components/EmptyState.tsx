import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Icon } from './Icon'
import type { IconName } from './Icon'

export function EmptyState({ title, description, icon = 'inbox', action }: { title: string; description?: string; icon?: IconName; action?: ReactNode }) {
  return (
    <Stack spacing={1.5} sx={{ alignItems: 'center', textAlign: 'center', py: { xs: 5, md: 7 }, px: 3 }}>
      <Box sx={{ bgcolor: 'background.default', border: '1px solid', borderColor: 'divider', borderRadius: 2, width: 60, height: 60, display: 'grid', placeItems: 'center', mb: 1 }}>
        <Icon name={icon} sx={{ color: 'text.secondary', fontSize: 28 }} />
      </Box>
      <Typography variant="subtitle1" component="h2">{title}</Typography>
      {description && <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 420 }}>{description}</Typography>}
      {action}
    </Stack>
  )
}
