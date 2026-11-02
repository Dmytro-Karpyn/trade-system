import { useOutletContext } from 'react-router-dom'
export interface ExpenseNotice { severity: 'success' | 'error'; message: string }
export function useExpenseNotice() { return useOutletContext<(notice: ExpenseNotice) => void>() }
