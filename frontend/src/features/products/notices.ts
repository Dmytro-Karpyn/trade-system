import { useOutletContext } from 'react-router-dom'
export interface ProductNotice { severity: 'success' | 'error'; message: string }
export function useProductNotice() { return useOutletContext<(notice: ProductNotice) => void>() }
