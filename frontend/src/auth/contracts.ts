import { z } from 'zod'

export const currentUserSchema = z.object({
  userId: z.guid(),
  username: z.string(),
  fullName: z.string(),
  organizationId: z.guid(),
  roles: z.array(z.string()),
  permissions: z.array(z.string()),
})
export type CurrentUser = z.infer<typeof currentUserSchema>
export const currentUserKey = ['current-user'] as const

export const loginSchema = z.object({
  username: z.string().trim().min(1, 'Введіть логін.'),
  password: z.string().min(1, 'Введіть пароль.'),
})
export type LoginInput = z.infer<typeof loginSchema>

export function hasAnyPermission(user: CurrentUser, permissions: readonly string[] = []) {
  return permissions.length === 0 || permissions.some(permission => user.permissions.includes(permission))
}
