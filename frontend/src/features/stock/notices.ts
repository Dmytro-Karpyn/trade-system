import { useOutletContext } from 'react-router-dom'
export interface StockNotice { severity: 'success' | 'error'; message: string }
export function useStockNotice() { return useOutletContext<(notice: StockNotice) => void>() }
