import { useOutletContext } from 'react-router-dom'
export interface ManagementNotice {
  severity: 'success' | 'error'
  message: string
}
export const useManagementNotice = () => useOutletContext<(n: ManagementNotice) => void>()
