import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { currentUserKey } from '../../auth/contracts'
import {
  auditDetailSchema,
  auditLookupsSchema,
  auditPageSchema,
  permissionSchema,
  roleSchema,
  userLookupsSchema,
  userSchema,
} from './contracts'
import type { RoleValues, UserValues } from './contracts'
export function useManagedUsers(enabled = true) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'users'],
    enabled,
    queryFn: ({ signal }) => api.request('/admin/users', { signal, schema: z.array(userSchema) }),
  })
}
export function useManagedUser(id: string) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'user', id],
    enabled: Boolean(id),
    queryFn: ({ signal }) => api.request('/admin/users/' + id, { signal, schema: userSchema }),
  })
}
export function useUserLookups() {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'user-lookups'],
    queryFn: ({ signal }) => api.request('/admin/users/lookups', { signal, schema: userLookupsSchema }),
  })
}
export function useManagedRoles() {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'roles'],
    queryFn: ({ signal }) => api.request('/admin/roles', { signal, schema: z.array(roleSchema) }),
  })
}
export function useManagedRole(id: string) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'role', id],
    enabled: Boolean(id),
    queryFn: ({ signal }) => api.request('/admin/roles/' + id, { signal, schema: roleSchema }),
  })
}
export function usePermissionDefinitions() {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'permissions'],
    queryFn: ({ signal }) => api.request('/admin/permissions', { signal, schema: z.array(permissionSchema) }),
  })
}
export function useUserMutations() {
  const { api } = useAppServices()
  const client = useQueryClient()
  const changed = async () => {
    await Promise.all([['admin'], currentUserKey].map((queryKey) => client.invalidateQueries({ queryKey })))
  }
  return {
    save: useMutation({
      mutationFn: ({ id, values }: { id?: string; values: UserValues }) =>
        api.request('/admin/users' + (id ? '/' + id : ''), {
          method: id ? 'PUT' : 'POST',
          body: {
            username: values.username,
            fullName: values.fullName,
            counterpartyId: values.counterpartyId || null,
            roleIds: values.roleIds,
            active: values.active,
            ...(!id ? { password: values.password } : {}),
          },
          schema: userSchema,
        }),
      onSuccess: changed,
    }),
    active: useMutation({
      mutationFn: ({ id, active }: { id: string; active: boolean }) =>
        api.request(`/admin/users/${id}/${active ? 'activate' : 'deactivate'}`, {
          method: 'POST',
          schema: userSchema,
        }),
      onSuccess: changed,
    }),
  }
}
export function useRoleMutations() {
  const { api } = useAppServices()
  const client = useQueryClient()
  const changed = async () => {
    await Promise.all([['admin'], currentUserKey].map((queryKey) => client.invalidateQueries({ queryKey })))
  }
  return {
    save: useMutation({
      mutationFn: ({ id, values }: { id?: string; values: RoleValues }) =>
        api.request('/admin/roles' + (id ? '/' + id : ''), {
          method: id ? 'PUT' : 'POST',
          body: values,
          schema: roleSchema,
        }),
      onSuccess: changed,
    }),
    permissions: useMutation({
      mutationFn: ({ id, codes }: { id: string; codes: string[] }) =>
        api.request(`/admin/roles/${id}/permissions`, {
          method: 'PUT',
          body: { permissionCodes: codes },
          schema: roleSchema,
        }),
      onSuccess: changed,
    }),
  }
}
export interface AuditFilters {
  from: string
  to: string
  userId: string
  eventType: string
  entityType: string
  documentId: string
  search: string
  skip: number
}
export function useAudit(filters: AuditFilters) {
  const { api } = useAppServices()
  const params = new URLSearchParams(
    Object.entries({
      ...filters,
      from: filters.from ? new Date(filters.from + 'T00:00:00').toISOString() : '',
      to: filters.to ? new Date(filters.to + 'T23:59:59.999').toISOString() : '',
      take: 50,
    })
      .filter(([, v]) => v !== '')
      .map(([k, v]) => [k, String(v)]),
  )
  return useQuery({
    queryKey: ['admin', 'audit', filters],
    queryFn: ({ signal }) => api.request('/admin/audit?' + params, { signal, schema: auditPageSchema }),
  })
}
export function useAuditDetail(id: string) {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'audit-detail', id],
    enabled: Boolean(id),
    queryFn: ({ signal }) => api.request('/admin/audit/' + id, { signal, schema: auditDetailSchema }),
  })
}
export function useAuditLookups() {
  const { api } = useAppServices()
  return useQuery({
    queryKey: ['admin', 'audit-lookups'],
    queryFn: ({ signal }) => api.request('/admin/audit/lookups', { signal, schema: auditLookupsSchema }),
  })
}
