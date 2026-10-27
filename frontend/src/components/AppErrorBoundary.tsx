import { Component } from 'react'
import type { ReactNode } from 'react'
import { Button } from '@mui/material'
import { EmptyState } from './EmptyState'

export class AppErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  render() {
    if (this.state.failed) return <EmptyState title="Не вдалося відкрити сторінку" description="Оновіть сторінку та спробуйте ще раз." action={<Button variant="contained" onClick={() => window.location.reload()}>Оновити сторінку</Button>} />
    return this.props.children
  }
}
