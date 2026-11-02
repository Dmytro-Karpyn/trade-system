export const tokens = {
  spacing: 8,
  borderRadius: 8,
  sidebarWidth: 232,
  collapsedSidebarWidth: 72,
  mobileSidebarWidth: 280,
  headerHeight: 64,
  mobileNavHeight: 64,
  contentMaxWidth: 1800,
  fontFamily: 'Inter, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
  overlayShadow: '0 8px 24px rgb(43 33 28 / 12%)',
  colors: {
    sidebar: '#2B211C', sidebarHover: '#3A2C25', sidebarText: '#D8CCC2',
    sidebarActive: '#C85A32', primary: '#C85A32', primaryHover: '#A94728',
    background: '#F6F1EA', surface: '#FFFDFC', border: '#E8DED3',
    text: '#2B2522', muted: '#766D67', selected: '#FBEDE7',
    success: '#5E8C61', warning: '#C99132', error: '#C45A52',
    // Contrast inks preserve the base palette while keeping small ERP text readable.
    actionInk: '#140D08', mutedInk: '#746B65',
  },
  statuses: {
    neutral: { background: '#EFE9E2', color: '#62564D', border: '#D8CDC1' },
    success: { background: '#EAF2E9', color: '#355D38', border: '#C5D8C5' },
    warning: { background: '#FBF1DA', color: '#78551B', border: '#E8D2A3' },
    error: { background: '#F9EAE7', color: '#8E3932', border: '#E5BDB8' },
    inactive: { background: '#EFEDEB', color: '#69635E', border: '#D8D3CF' },
  },
} as const
