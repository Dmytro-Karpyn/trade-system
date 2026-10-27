import { useOutletContext } from 'react-router-dom'
export interface PartyNotice { severity: 'success' | 'error'; message: string }
export function usePartyNotice() { return useOutletContext<(notice: PartyNotice) => void>() }
