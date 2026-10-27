import { useState } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { Outlet } from 'react-router-dom'
import type { PartyNotice } from './notices'

export function CounterpartyWorkspace() {
  const [notice, setNotice] = useState<PartyNotice | null>(null)
  return <><Outlet context={(next: PartyNotice) => setNotice(next)} /><Snackbar key={notice?.message} open={Boolean(notice)} autoHideDuration={6000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}><Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>{notice?.message}</Alert></Snackbar></>
}
