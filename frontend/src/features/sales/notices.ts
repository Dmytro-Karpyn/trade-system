import { useOutletContext } from 'react-router-dom'
import type { SaleNotice } from './SaleWorkspace'
export const useSaleNotice = () => useOutletContext<(notice: SaleNotice) => void>()
