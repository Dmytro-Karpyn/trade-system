import { SvgIcon } from '@mui/material'
import type { SvgIconProps } from '@mui/material'

const paths = {
  edit: 'm16 3 5 5-12 12H4v-5L16 3Zm-3 3 5 5',
  back: 'M19 12H5m6-6-6 6 6 6',
  home: 'M3 10.5 12 3l9 7.5M5 9v11h5v-6h4v6h5V9',
  box: 'm12 3 9 5-9 5-9-5 9-5Zm-9 5v9l9 5 9-5V8M12 13v9M7.5 5.5l9 5',
  purchase: 'M6 3h12v18l-3-2-3 2-3-2-3 2V3Zm3 5h6m-6 4h6m-6 4h3',
  sale: 'M3 3h2l2.5 12h10l3-9H6m3 13h.01M17 19h.01M9 19a1 1 0 1 0 0 .01M17 19a1 1 0 1 0 0 .01',
  stock: 'm3 9 9-6 9 6v12H3V9Zm4 12V11h10v10M7 15h10M7 18h10',
  wallet: 'M20 8V5H5a2 2 0 0 1 0-4h13v4M5 5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h15V8H5m15 4h-5v5h5m-3-2.5h.01',
  expense: 'M5 3h14v18l-3-2-4 2-4-2-3 2V3Zm3 5h8m-8 4h8m-8 4h5',
  settlement: 'M3 7h16m-4-4 4 4-4 4M21 17H5m4-4-4 4 4 4',
  people: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm8 1a4 4 0 0 1 0 7m5 10v-2a4 4 0 0 0-3-3.87',
  chart: 'M4 3v18h17M8 16v-4m5 4V7m5 9v-6',
  folder: 'M3 7V4h6l2 3h10v14H3V7Zm0 4h18',
  settings: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8ZM9 3h6l1 3 3-1 3 5-2 2 2 2-3 5-3-1-1 3H9l-1-3-3 1-3-5 2-2-2-2 3-5 3 1 1-3Z',
  search: 'M10.5 3a7.5 7.5 0 1 0 0 15 7.5 7.5 0 0 0 0-15ZM16 16l5 5',
  plus: 'M12 5v14M5 12h14',
  down: 'm6 9 6 6 6-6',
  menu: 'M4 6h16M4 12h16M4 18h16',
  close: 'm6 6 12 12M6 18 18 6',
  logout: 'M9 3H4v18h5m5-14 5 5-5 5m-6-5h11',
  arrow: 'M5 12h14m-6-6 6 6-6 6',
  trend: 'm3 17 6-6 4 4 8-10m-6 0h6v6',
  downLeft: 'M18 6 6 18m0-8v8h8',
  upRight: 'M6 18 18 6m-8 0h8v8',
  calendar: 'M8 2v4m8-4v4M3 10h18M3 5h18v16H3V5Z',
  inbox: 'M4 4h16l2 12v4H2v-4L4 4Zm-2 12h6l2 3h4l2-3h6',
  lock: 'M7 10V7a5 5 0 0 1 10 0v3M4 10h16v12H4V10Zm8 5v3',
  eye: 'M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Zm10-3a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z',
  eyeOff: 'm3 3 18 18M10.6 5.1 12 5c6.5 0 10 7 10 7a20 20 0 0 1-3 3.8M6 6.3A22 22 0 0 0 2 12s3.5 7 10 7c1.8 0 3.5-.5 5-1.3M10 10a3 3 0 0 0 4 4',
  check: 'm5 12 4 4L19 6',
  refresh: 'M20 8a9 9 0 1 0 0 8M20 3v5h-5',
  shield: 'm12 2 9 4v6c0 5-9 10-9 10S3 17 3 12V6l9-4Zm-4 10 3 3 5-6',
} as const
export type IconName = keyof typeof paths

export function Icon({ name, ...props }: SvgIconProps & { name: IconName }) {
  return <SvgIcon viewBox="0 0 24 24" {...props}><path d={paths[name]} fill="none" stroke="currentColor" strokeWidth="1.65" strokeLinecap="round" strokeLinejoin="round" /></SvgIcon>
}
