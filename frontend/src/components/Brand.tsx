import { Box, Stack, Typography } from '@mui/material'

export function Brand({ light = false }: { light?: boolean }) {
  return (
    <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
      <Box aria-hidden="true" sx={{ width: 36, height: 36, bgcolor: 'primary.main', borderRadius: 1, display: 'grid', placeItems: 'center', color: '#fff', fontWeight: 800, fontSize: 23 }}>T</Box>
      <Box>
        <Typography sx={{ fontSize: 19, letterSpacing: '-0.04em', fontWeight: 750, lineHeight: 1.1, color: light ? '#fff' : 'text.primary' }}>Trade System</Typography>
        <Typography sx={{ fontSize: 10, letterSpacing: '0.08em', mt: 0.5, color: light ? '#96a8c4' : 'text.secondary' }}>УПРАВЛІННЯ БІЗНЕСОМ</Typography>
      </Box>
    </Stack>
  )
}
