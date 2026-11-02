import { Box, Button, Paper, Stack, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { Icon } from './Icon'
import type { IconName } from './Icon'

export interface CatalogSection {
  path: string
  title: string
  description: string
  icon: IconName
}
export function CatalogSections({
  sections,
  action = 'Відкрити',
}: {
  sections: CatalogSection[]
  action?: string
}) {
  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: {
          xs: 'minmax(0,1fr)',
          md: 'repeat(2,minmax(0,1fr))',
          xl: 'repeat(3,minmax(0,1fr))',
        },
        gap: 1.5,
      }}
    >
      {sections.map((section) => (
        <Paper variant="outlined" key={section.path} sx={{ p: 2, minWidth: 0 }}>
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'flex-start' }}>
            <Icon name={section.icon} sx={{ color: 'text.secondary', mt: 0.25 }} />
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Typography variant="h2" component="h2">
                {section.title}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ my: 0.75 }}>
                {section.description}
              </Typography>
              <Button
                component={Link}
                to={section.path}
                size="small"
                endIcon={<Icon name="arrow" />}
                sx={{ p: 0 }}
              >
                {action}
              </Button>
            </Box>
          </Stack>
        </Paper>
      ))}
    </Box>
  )
}
