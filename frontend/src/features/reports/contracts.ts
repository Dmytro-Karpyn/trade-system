import { z } from 'zod'
import { localDate } from '../purchases/contracts'

export const reportKinds = ['sales','profitability','stock','purchases','money','expenses','settlements'] as const
export type ReportKind = typeof reportKinds[number]
export const reportTitles: Record<ReportKind,string> = { sales:'Продажі',profitability:'Рентабельність',stock:'Склад',purchases:'Закупівлі',money:'Гроші',expenses:'Витрати',settlements:'Взаєморозрахунки' }
export const reportExtraPermission: Partial<Record<ReportKind,string>> = { profitability:'profit.view',money:'money.view',settlements:'settlement.view' }
export const metricSchema = z.object({ key:z.string(),label:z.string(),value:z.number(),currencyId:z.guid().nullable(),format:z.string() })
export const reportSchema = z.object({ key:z.string(),title:z.string(),columns:z.array(z.object({ key:z.string(),label:z.string(),format:z.string() })),rows:z.array(z.record(z.string(),z.unknown())),metrics:z.array(metricSchema),notes:z.array(z.string()) })
export type ReportTable = z.infer<typeof reportSchema>; export type Metric = z.infer<typeof metricSchema>
const trendSchema = z.object({ date:z.iso.date(),sales:z.number(),returns:z.number(),netSales:z.number(),grossProfit:z.number().nullable() })
export const dashboardSchema = z.object({ from:z.iso.date(),to:z.iso.date(),kpis:z.array(metricSchema),salesTrend:z.array(trendSchema),profitTrend:z.array(trendSchema),blocks:z.array(reportSchema),notes:z.array(z.string()) })
export type Dashboard = z.infer<typeof dashboardSchema>
const option = z.object({ id:z.guid(),name:z.string() })
export const reportLookupsSchema = z.object({ products:z.array(option),categories:z.array(option),counterparties:z.array(option),managers:z.array(option),batches:z.array(option),priceTypes:z.array(option),locations:z.array(option),accounts:z.array(option),expenseCategories:z.array(option),currencyIds:z.array(z.guid()) })
export type ReportLookups = z.infer<typeof reportLookupsSchema>
export type PeriodPreset = 'today'|'week'|'month'|'previous'|'custom'
export function periodDates(preset:PeriodPreset, today=localDate()) { const date = new Date(today+'T12:00:00'); const iso = (d:Date) => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`; if(preset==='today') return {from:today,to:today}; if(preset==='week') { date.setDate(date.getDate()-6); return {from:iso(date),to:today} } if(preset==='previous') return {from:iso(new Date(date.getFullYear(),date.getMonth()-1,1)),to:iso(new Date(date.getFullYear(),date.getMonth(),0))}; return {from:iso(new Date(date.getFullYear(),date.getMonth(),1)),to:today} }
export interface ReportFilters { from:string;to:string;productId:string;categoryId:string;counterpartyId:string;createdBy:string;batchId:string;priceTypeId:string;storageLocationId:string;currencyId:string;accountId:string;operationType:string;isPurchaseExpense:string;withStock:boolean;withReservations:boolean;view:string;retailPriceTypeId:string;wholesalePriceTypeId:string }
export function defaultReportFilters():ReportFilters { return {...periodDates('month'),productId:'',categoryId:'',counterpartyId:'',createdBy:'',batchId:'',priceTypeId:'',storageLocationId:'',currencyId:'',accountId:'',operationType:'',isPurchaseExpense:'',withStock:false,withReservations:false,view:'',retailPriceTypeId:'',wholesalePriceTypeId:''} }
export const validPeriod = (f:{from:string;to:string}) => z.iso.date().safeParse(f.from).success && z.iso.date().safeParse(f.to).success && f.from<=f.to
export function reportParams(filters:Partial<ReportFilters>) { return new URLSearchParams(Object.entries(filters).filter(([,v]) => v!=='' && v!==false && v!==undefined).map(([k,v]) => [k,String(v)])).toString() }
export function reportCurrencies(table:ReportTable) { return [...new Set([...table.metrics.flatMap(m => m.currencyId ? [m.currencyId] : []),...table.rows.flatMap(r => typeof r.currencyId==='string' ? [r.currencyId] : [])])].sort() }
