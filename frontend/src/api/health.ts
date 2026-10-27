import { z } from 'zod'
import type { ApiClient } from './client'

const healthSchema = z.object({ status: z.enum(['Healthy', 'Degraded', 'Unhealthy']) })
export function fetchHealth(api: ApiClient, signal: AbortSignal) {
  return api.request('/health', { signal, schema: healthSchema })
}
