import { lazy, Suspense } from 'react'
import { ListLoading } from './ListLoading'
import type { ReturnKind } from '../features/returns/contracts'
const RelatedReturns = lazy(() => import('../features/returns/RelatedReturns').then(module => ({ default: module.RelatedReturns })))
export function RelatedReturnsSection({ kind, sourceId }: { kind: ReturnKind; sourceId: string }) { return <Suspense fallback={<ListLoading label="Завантаження пов’язаних повернень" />}><RelatedReturns kind={kind} sourceId={sourceId} /></Suspense> }
