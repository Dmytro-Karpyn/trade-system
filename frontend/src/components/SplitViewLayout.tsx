import { Box } from '@mui/material'
import type { ReactNode } from 'react'
import { tokens } from '../theme/tokens'

// Both panes scroll independently below the fixed application header.
const pane = { maxHeight: `calc(100dvh - ${tokens.headerHeight + tokens.spacing * 5}px)`, overflowY: 'auto', scrollbarGutter: 'stable' } as const
export function SplitViewLayout({ list, detail, split, detailLabel = 'Деталі документа', listWeight = 0.85 }: { list: ReactNode; detail: ReactNode; split: boolean; detailLabel?: string; listWeight?: number }) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: split ? `minmax(360px, ${listWeight}fr) minmax(0, ${2 - listWeight}fr)` : 'minmax(0, 1fr)', gap: 2, alignItems: 'start' }}>
    <Box sx={{ minWidth: 0, ...(split && pane) }}>{list}</Box>
    {split && <Box component="section" aria-label={detailLabel} sx={{ minWidth: 0, borderLeft: '1px solid', borderColor: 'divider', pl: 2, ...pane }}>{detail}</Box>}
  </Box>
}
