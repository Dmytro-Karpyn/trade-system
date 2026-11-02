import { Box, Stack } from '@mui/material'
import type { ReactNode } from 'react'
export function DocumentActions({ children, danger, summary }: { children?: ReactNode; danger?: ReactNode; summary?: ReactNode }) {
  return <Stack direction="row" spacing={1} sx={{ position: 'sticky', bottom: 0, zIndex: theme => theme.zIndex.appBar - 1, bgcolor: 'background.paper', p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 1, alignItems: 'center', flexWrap: 'wrap', rowGap: 1 }}>{danger && <Box sx={{ display: 'flex', gap: 1, pr: 1, mr: 1, borderRight: '1px solid', borderColor: 'divider' }}>{danger}</Box>}<Box sx={{ flex: 1, ...(summary && { flexBasis: { xs: '100%', sm: 'auto' }, order: { xs: -1, sm: 0 } }) }}>{summary}</Box>{children}</Stack>
}
