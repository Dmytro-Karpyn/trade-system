import { useState } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { Outlet } from 'react-router-dom'
import type { ManagementNotice } from './notices'
import { tokens } from '../../theme/tokens'
export function ManagementWorkspace() {
  const [notice, setNotice] = useState<ManagementNotice | null>(null)
  return (
    <>
      <Outlet context={setNotice} />
      <Snackbar
        key={notice?.message}
        open={Boolean(notice)}
        onClose={() => setNotice(null)}
        autoHideDuration={6000}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        sx={{ top: { xs: tokens.headerHeight + 12, sm: tokens.headerHeight + 12 } }}
      >
        <Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>
          {notice?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
