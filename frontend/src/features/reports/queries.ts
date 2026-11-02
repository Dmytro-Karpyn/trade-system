import { useQuery } from '@tanstack/react-query'
import { useAppServices } from '../../app/context'
import { useCurrentUser } from '../../auth/queries'
import { dashboardSchema,reportLookupsSchema,reportParams,reportSchema,validPeriod } from './contracts'
import type { ReportFilters,ReportKind } from './contracts'

export function useDashboard(filters:{from:string;to:string}) { const {api}=useAppServices(); const user=useCurrentUser().data; return useQuery({queryKey:['dashboard',filters,user?.permissions],enabled:Boolean(user && validPeriod(filters)),queryFn:({signal})=>api.request('/dashboard/summary?'+reportParams(filters),{signal,schema:dashboardSchema})}) }
export function useReport(kind:ReportKind,filters:ReportFilters,enabled=true) { const {api}=useAppServices(); const user=useCurrentUser().data; return useQuery({queryKey:['reports',kind,filters,user?.permissions],enabled:enabled&&validPeriod(filters),queryFn:({signal})=>api.request(`/reports/${kind}?`+reportParams(filters),{signal,schema:reportSchema})}) }
export function useReportLookups(enabled=true) { const {api}=useAppServices(); const user=useCurrentUser().data; return useQuery({queryKey:['reports','lookups',user?.permissions],enabled,queryFn:({signal})=>api.request('/reports/lookups',{signal,schema:reportLookupsSchema})}) }
