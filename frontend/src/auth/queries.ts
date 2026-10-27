import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { useAppServices } from '../app/context'
import { currentUserKey, currentUserSchema } from './contracts'
import type { LoginInput } from './contracts'

export function useCurrentUser() {
  const { api } = useAppServices()
  return useQuery({
    queryKey: currentUserKey,
    queryFn: async ({ signal }) => {
      try { return await api.request('/auth/me', { signal, schema: currentUserSchema }) }
      catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    retry: false,
    refetchOnWindowFocus: 'always',
    refetchInterval: 60_000,
  })
}

export function useLogin() {
  const { api, queryClient } = useAppServices()
  return useMutation({
    gcTime: 0, // Do not retain submitted credentials after the login form unmounts.
    mutationFn: async (input: LoginInput) => {
      api.resetCsrf()
      await api.request('/auth/login', { method: 'POST', body: input, handleUnauthorized: false })
      api.resetCsrf()
      await api.refreshCsrf() // The anonymous request token cannot be reused after Identity signs in.
      return api.request('/auth/me', { schema: currentUserSchema })
    },
    onSuccess: async user => {
      await queryClient.cancelQueries()
      queryClient.removeQueries({ predicate: query => query.queryKey[0] !== currentUserKey[0] })
      queryClient.setQueryData(currentUserKey, user)
    },
  })
}

export function useLogout() {
  const { api, clearSession } = useAppServices()
  return useMutation({
    mutationFn: async () => {
      try { await api.request('/auth/logout', { method: 'POST' }) }
      catch (error) { if (!(error instanceof ApiError && error.status === 401)) throw error }
    },
    onSuccess: clearSession,
  })
}
