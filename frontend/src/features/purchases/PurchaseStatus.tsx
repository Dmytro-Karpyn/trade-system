import { StatusBadge } from '../../components/StatusBadge'
export function PurchaseStatus({ status }: { status: 'DRAFT' | 'POSTED' }) { return <StatusBadge status={status} /> }
