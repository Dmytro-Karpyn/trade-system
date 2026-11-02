import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useId } from 'react'
import type { ReactNode } from 'react'
export function ConfirmDialog({ open, title, children, action, confirm, close, busy = false, danger = false, busyLabel = 'Виконуємо…' }: { open: boolean; title: string; children: ReactNode; action: string; confirm: () => void; close: () => void; busy?: boolean; danger?: boolean; busyLabel?: string }) {
  const id = useId()
  return <Dialog open={open} onClose={busy ? undefined : close} fullWidth maxWidth="xs" aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`}><DialogTitle id={`${id}-title`}>{title}</DialogTitle><DialogContent><DialogContentText component="div" id={`${id}-description`}>{children}</DialogContentText></DialogContent><DialogActions><Button disabled={busy} onClick={close}>Скасувати</Button><Button variant="contained" color={danger ? 'error' : 'primary'} disabled={busy} onClick={confirm}>{busy ? busyLabel : action}</Button></DialogActions></Dialog>
}
