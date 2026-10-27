import { useOutletContext } from 'react-router-dom'
export interface PurchaseNotice { severity: 'success' | 'error'; message: string }
export const usePurchaseNotice = () => useOutletContext<(notice: PurchaseNotice) => void>()
