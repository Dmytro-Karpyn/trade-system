import type { CurrentUser } from '../../auth/contracts'
import type { Dashboard,ReportTable } from './contracts'
const costKeys=new Set(['currentCost','currentUnitCost','valuation','stockCost','cogs','writeoffs'])
export function visibleTable(table:ReportTable,cost:boolean):ReportTable { return cost?table:{...table,columns:table.columns.filter(c=>!costKeys.has(c.key)),metrics:table.metrics.filter(m=>!costKeys.has(m.key)),rows:table.rows.map(r=>Object.fromEntries(Object.entries(r).filter(([key])=>!costKeys.has(key))))} }
export function visibleDashboard(data:Dashboard,user:CurrentUser):Dashboard {
  const has=(p:string)=>user.permissions.includes(p);const sales=has('sale.view')||has('reports.view'),expenses=has('expense.view')||has('reports.view'),stock=has('stock.view')||has('reports.view')
  const allowed=(key:string)=>['sales','returns','netSales'].includes(key)?sales:['grossProfit','managementResult'].includes(key)?has('profit.view'):key==='cogs'?has('cost.view'):key==='expenses'?expenses:key==='writeoffs'?has('cost.view')&&(has('stock.writeoff')||has('reports.view')):key==='money'?has('money.view'):['receivables','payables'].includes(key)?has('settlement.view'):false
  return {...data,kpis:data.kpis.filter(m=>allowed(m.key)),salesTrend:sales?data.salesTrend:[],profitTrend:has('profit.view')?data.profitTrend:[],blocks:data.blocks.filter(b=>b.key==='moneyAccounts'?has('money.view'):['debtors','creditors'].includes(b.key)?has('settlement.view'):b.key==='stock'?stock:b.key==='expenseCategories'?expenses:true).map(b=>visibleTable(b,has('cost.view')))}
}
