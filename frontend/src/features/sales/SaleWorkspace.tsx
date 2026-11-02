import { useState } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { Outlet } from 'react-router-dom'
import { tokens } from '../../theme/tokens'

export interface SaleNotice { severity: 'success' | 'error'; message: string }
export function SaleWorkspace() { const [notice, setNotice] = useState<SaleNotice | null>(null); return <><Outlet context={setNotice} /><Snackbar key={notice?.message} open={Boolean(notice)} autoHideDuration={6000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} sx={{ top: { xs: tokens.headerHeight + 12, sm: tokens.headerHeight + 12 } }}><Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>{notice?.message}</Alert></Snackbar></> }
