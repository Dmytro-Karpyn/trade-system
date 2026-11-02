import type { ReactNode } from 'react'
import {
  Box,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Paper,
  Typography,
} from '@mui/material'
import { DocumentActions } from './DocumentActions'

// The same editor fields/actions serve the desktop pane and the mobile dialog.
export function EditorSurface({
  title,
  embedded,
  children,
  actions,
  close,
  busy,
}: {
  title: string
  embedded: boolean
  children: ReactNode
  actions: ReactNode
  close: () => void
  busy: boolean
}) {
  return embedded ? (
    <Paper variant="outlined">
      <Typography variant="h2" component="h2" sx={{ px: 2, pt: 2 }}>
        {title}
      </Typography>
      <Box sx={{ p: 2 }}>{children}</Box>
      <DocumentActions>{actions}</DocumentActions>
    </Paper>
  ) : (
    <Dialog open onClose={busy ? undefined : close} maxWidth="sm" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>{children}</DialogContent>
      <DialogActions>{actions}</DialogActions>
    </Dialog>
  )
}
