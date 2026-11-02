import { StatusBadge } from './StatusBadge'
export function ActiveBadge({ active }: { active: boolean }) { return <StatusBadge status={active ? 'ACTIVE' : 'INACTIVE'} /> }
