import { useCallback, useState } from 'react'
// Local page state only; the existing query remains the source of business data.
export function useDetailSelection() {
  const [id, setId] = useState<string | null>(null), [pending, setPending] = useState<string | null>(null), [dirty, setDirty] = useState(false)
  const dirtyChanged = useCallback((value: boolean) => setDirty(value), [])
  return { id, pending, dirtyChanged, select: (value: string) => { if (id === value) return; if (dirty) setPending(value); else setId(value) }, cancel: () => setPending(null), discard: () => { setId(pending); setPending(null); setDirty(false) }, deleted: () => { setId(null); setDirty(false) } }
}
