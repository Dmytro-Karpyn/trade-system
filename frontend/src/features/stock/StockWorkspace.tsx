import { Alert, Snackbar } from '@mui/material'
import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { tokens } from '../../theme/tokens'
import type { StockNotice } from './notices'

export function StockWorkspace() {
  const [notice, setNotice] = useState<StockNotice | null>(null)
  return <><Outlet context={(value: StockNotice) => setNotice(value)} /><Snackbar key={notice?.message} open={Boolean(notice)} autoHideDuration={6000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} sx={{ top: { xs: tokens.headerHeight + 12, sm: tokens.headerHeight + 12 } }}><Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>{notice?.message}</Alert></Snackbar></>
}
