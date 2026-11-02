import { useOutletContext } from 'react-router-dom'
export interface MoneyNotice { severity: 'success' | 'error'; message: string }
export function useMoneyNotice() { return useOutletContext<(notice: MoneyNotice) => void>() }
