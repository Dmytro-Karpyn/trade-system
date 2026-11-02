import { useState } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { Outlet } from 'react-router-dom'
import { tokens } from '../../theme/tokens'
import type { ReturnNotice } from './notices'
export function ReturnWorkspace() { const [notice, setNotice] = useState<ReturnNotice | null>(null); return <><Outlet context={(value: ReturnNotice) => setNotice(value)} /><Snackbar key={notice?.message} open={Boolean(notice)} autoHideDuration={6000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} sx={{ top: { xs: tokens.headerHeight + 12, sm: tokens.headerHeight + 12 } }}><Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>{notice?.message}</Alert></Snackbar></> }
