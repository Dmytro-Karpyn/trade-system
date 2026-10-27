import { createTheme } from '@mui/material'
import { tokens } from './tokens'

export const theme = createTheme({
  spacing: tokens.spacing,
  shape: { borderRadius: tokens.borderRadius },
  palette: {
    primary: { main: tokens.colors.primary, dark: '#1d4ed8' },
    background: { default: tokens.colors.background, paper: '#ffffff' },
    text: { primary: tokens.colors.text, secondary: tokens.colors.muted },
    divider: tokens.colors.border,
    success: { main: '#168568' },
    error: { main: '#d14343' },
  },
  typography: {
    fontFamily: tokens.fontFamily,
    fontSize: 14,
    h1: { fontSize: '1.85rem', fontWeight: 700, letterSpacing: '-0.04em', lineHeight: 1.25 },
    h2: { fontSize: '1.2rem', fontWeight: 650, letterSpacing: '-0.025em' },
    h3: { fontSize: '1rem', fontWeight: 650 },
    subtitle1: { fontSize: '0.9rem', fontWeight: 600 },
    body1: { fontSize: '0.9rem', lineHeight: 1.65 },
    body2: { fontSize: '0.8rem', lineHeight: 1.6 },
    caption: { fontSize: '0.72rem', lineHeight: 1.5 },
    overline: { fontSize: '0.65rem', fontWeight: 700, letterSpacing: '0.12em', lineHeight: 2 },
    button: { textTransform: 'none', fontWeight: 600, fontSize: '0.85rem' },
  },
  components: {
    MuiCssBaseline: { styleOverrides: { body: { margin: 0 }, a: { WebkitTapHighlightColor: 'transparent' }, '#root': { minHeight: '100dvh' } } },
    MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { outlined: { borderColor: tokens.colors.border } } },
    MuiButton: { defaultProps: { disableElevation: true }, styleOverrides: { root: { borderRadius: tokens.borderRadius - 2, minHeight: 38 } } },
    MuiTextField: { defaultProps: { size: 'small', variant: 'outlined' } },
    MuiOutlinedInput: { styleOverrides: { root: { borderRadius: tokens.borderRadius - 2 } } },
    MuiTableCell: { defaultProps: { size: 'small' }, styleOverrides: { head: { color: tokens.colors.muted, backgroundColor: '#fafbfd', fontWeight: 600 }, root: { borderColor: tokens.colors.border, padding: '12px 20px' } } },
    MuiTooltip: { defaultProps: { arrow: true } },
    MuiChip: { styleOverrides: { root: { fontSize: '0.7rem', fontWeight: 500, borderRadius: 6 } } },
    MuiMenu: { defaultProps: { slotProps: { paper: { sx: { border: `1px solid ${tokens.colors.border}`, boxShadow: '0 8px 30px rgb(17 31 56 / 8%)', minWidth: 230, mt: 1 } } } } },
  },
})
