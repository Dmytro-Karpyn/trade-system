import { useState } from 'react'
import { Alert, Snackbar } from '@mui/material'
import { Outlet } from 'react-router-dom'
import type { ProductNotice } from './notices'

export function ProductWorkspace() {
  const [notice, setNotice] = useState<ProductNotice | null>(null)
  return <>
    <Outlet context={(next: ProductNotice) => setNotice(next)} />
    <Snackbar open={Boolean(notice)} autoHideDuration={6000} onClose={() => setNotice(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
      <Alert severity={notice?.severity ?? 'success'} onClose={() => setNotice(null)}>{notice?.message}</Alert>
    </Snackbar>
  </>
}
