import { useState } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { Outlet } from 'react-router-dom'
import type { PurchaseNotice } from './notices'
import { tokens } from '../../theme/tokens'

export function PurchaseWorkspace() {
  const [notice, setNotice] = useState<PurchaseNotice | null>(null)
  return <><Outlet context={(next: PurchaseNotice) => setNotice(next)} /><Snackbar key={notice?.message} open={Boolean(notice)} autoHideDuration={6000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} sx={{ top: { xs: tokens.headerHeight + 12, sm: tokens.headerHeight + 12 } }}><Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>{notice?.message}</Alert></Snackbar></>
}
