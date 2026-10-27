import '@testing-library/jest-dom/vitest'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

afterEach(() => cleanup())

export function setViewport(width = 1440) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: width })
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn((query: string) => ({
      matches: (!query.includes('min-width') || width >= Number(query.match(/min-width:\s*([\d.]+)/)?.[1] ?? 0)) &&
        (!query.includes('max-width') || width <= Number(query.match(/max-width:\s*([\d.]+)/)?.[1] ?? Infinity)),
      media: query, onchange: null, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
    })),
  })
}
setViewport()
