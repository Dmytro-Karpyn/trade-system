import { useId, useState } from 'react'
import { Box, Button, Drawer, IconButton, Stack, Typography, useMediaQuery, useTheme } from '@mui/material'
import type { ReactNode } from 'react'
import { Icon } from './Icon'

export function FilterBar({ children, mobileSheet = false }: { children: ReactNode; mobileSheet?: boolean }) {
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  const [open, setOpen] = useState(false)
  const id = useId()
  const content = <Box role="group" aria-label="Фільтри" sx={{ p: 2, display: 'flex', flexWrap: 'wrap', gap: 1.5, borderBottom: '1px solid', borderColor: 'divider', '& > *': { minWidth: 0 } }}>{children}</Box>
  if (!mobileSheet || !mobile) return content
  return <><Box sx={{ p: 1.5, borderBottom: '1px solid', borderColor: 'divider' }}><Button variant="outlined" startIcon={<Icon name="settings" />} onClick={() => setOpen(true)} aria-haspopup="dialog">Фільтри</Button></Box><Drawer anchor="bottom" open={open} onClose={() => setOpen(false)} slotProps={{ paper: { role: 'dialog', 'aria-labelledby': id, sx: { maxHeight: '85dvh', borderRadius: '12px 12px 0 0', pb: 'max(16px, env(safe-area-inset-bottom))' } } }}><Stack direction="row" sx={{ px: 2, pt: 1.5, alignItems: 'center', justifyContent: 'space-between' }}><Typography id={id} variant="h2" component="h2">Фільтри</Typography><IconButton aria-label="Закрити фільтри" onClick={() => setOpen(false)}><Icon name="close" /></IconButton></Stack>{content}<Button sx={{ m: 2 }} variant="contained" onClick={() => setOpen(false)}>Показати результати</Button></Drawer></>
}
