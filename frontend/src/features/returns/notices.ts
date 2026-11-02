import { useOutletContext } from 'react-router-dom'
export interface ReturnNotice { severity: 'success' | 'error'; message: string }
export const useReturnNotice = () => useOutletContext<(notice: ReturnNotice) => void>()
