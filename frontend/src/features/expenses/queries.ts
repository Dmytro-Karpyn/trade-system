import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { z } from 'zod'
import { useAppServices } from '../../app/context'
import { categoryInput, categorySchema, costImpactSchema, expenseLookupsSchema, expenseSchema } from './contracts'
import type { CategoryValues, Expense, ExpenseFilters } from './contracts'
export const expenseKeys = { all: ['expenses'] as const, detail: (id: string) => ['expenses', 'detail', id] as const, lookups: ['expense-lookups'] as const, categories: ['expense-categories'] as const }
export function useExpenses(filters: ExpenseFilters) { const { api } = useAppServices(); const params = new URLSearchParams(Object.entries(filters).filter(([, value]) => value !== '')); return useQuery({ queryKey: ['expenses', 'list', filters], queryFn: ({ signal }) => api.request(`/expenses${params.size ? `?${params}` : ''}`, { signal, schema: z.array(expenseSchema) }) }) }
export function useExpense(id?: string) { const { api } = useAppServices(); return useQuery({ queryKey: expenseKeys.detail(id ?? ''), enabled: Boolean(id), refetchOnWindowFocus: false, queryFn: ({ signal }) => api.request(`/expenses/${id}`, { signal, schema: expenseSchema }) }) }
export function useExpenseLookups() { const { api } = useAppServices(); return useQuery({ queryKey: expenseKeys.lookups, queryFn: ({ signal }) => api.request('/expenses/lookups', { signal, schema: expenseLookupsSchema }) }) }
export function useExpenseCategories() { const { api } = useAppServices(); return useQuery({ queryKey: expenseKeys.categories, queryFn: ({ signal }) => api.request('/expense-categories', { signal, schema: z.array(categorySchema) }) }) }
export function useCostImpact(id: string, enabled: boolean) { const { api } = useAppServices(); return useQuery({ queryKey: ['expenses', 'cost-impact', id], enabled, queryFn: ({ signal }) => api.request(`/expenses/${id}/cost-impact`, { signal, schema: costImpactSchema }) }) }
export function useSaveCategory() { const { api } = useAppServices(); const client = useQueryClient(); return useMutation({ mutationFn: ({ id, input }: { id?: string; input: CategoryValues }) => api.request('/expense-categories' + (id ? `/${id}` : ''), { method: id ? 'PUT' : 'POST', body: categoryInput(input), schema: categorySchema }), onSuccess: async () => { await Promise.all([expenseKeys.categories, expenseKeys.lookups, expenseKeys.all].map(queryKey => client.invalidateQueries({ queryKey }))) } }) }
export function useExpenseMutations() {
  const { api } = useAppServices(); const client = useQueryClient()
  const changed = async (expense?: Expense) => { if (expense) client.setQueryData(expenseKeys.detail(expense.document.id), expense); await Promise.all([expenseKeys.all, expenseKeys.lookups, ['purchases'], ['stock'], ['sales'], ['batches'], ['settlements'], ['counterparties']].map(queryKey => client.invalidateQueries({ queryKey }))) }
  const save = useMutation({ mutationFn: ({ id, input }: { id?: string; input: unknown }) => api.request('/expenses' + (id ? `/${id}` : ''), { method: id ? 'PUT' : 'POST', body: input, schema: expenseSchema }), onSuccess: changed })
  const transition = useMutation({ mutationFn: ({ id, action }: { id: string; action: 'post' | 'unpost' }) => api.request(`/expenses/${id}/${action}`, { method: 'POST', schema: expenseSchema }), onSuccess: changed })
  const remove = useMutation({ mutationFn: (id: string) => api.request(`/expenses/${id}`, { method: 'DELETE' }), onSuccess: async () => { await changed() } })
  return { save, transition, remove }
}
