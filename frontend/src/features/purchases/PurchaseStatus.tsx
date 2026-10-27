import { Chip } from '@mui/material'
export function PurchaseStatus({ status }: { status: 'DRAFT' | 'POSTED' }) { return <Chip size="small" label={status === 'DRAFT' ? 'Чернетка' : 'Проведено'} variant="outlined" color={status === 'POSTED' ? 'success' : 'default'} /> }
