import { Box, Paper, Typography } from '@mui/material'
import type { ReactNode } from 'react'
export function KpiCard({ label, value, testId, children, ariaLabel }: { label: string; value: ReactNode; testId?: string; children?: ReactNode; ariaLabel?: string }) {
  return <Paper component="section" aria-label={ariaLabel} variant="outlined" sx={{ p: 1.5, minWidth: 0 }}><Typography component="h2" variant="body2" color="text.secondary" sx={{ minHeight: 38 }}>{label}</Typography><Typography data-testid={testId} sx={{ mt: 0.75, fontSize: { xs: 20, md: 21 }, fontWeight: 650, fontVariantNumeric: 'tabular-nums', overflowWrap: 'anywhere' }}>{value}</Typography>{children && <Box sx={{ mt: 1 }}>{children}</Box>}</Paper>
}
