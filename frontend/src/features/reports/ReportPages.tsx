import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  FormControlLabel,
  MenuItem,
  Paper,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material'
import { Link } from 'react-router-dom'
import { CatalogSections } from '../../components/CatalogSections'
import { PageHeader } from '../../components/PageHeader'
import { FilterBar } from '../../components/FilterBar'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ForbiddenState, RequestErrorState } from '../../components/RequestState'
import { ListLoading } from '../../components/ListLoading'
import { currencyName } from '../purchases/contracts'
import {
  defaultReportFilters,
  reportExtraPermission,
  reportKinds,
  reportTitles,
  validPeriod,
} from './contracts'
import type { ReportFilters, ReportKind, ReportLookups } from './contracts'
import { visibleTable } from './visibility'
import { PeriodControls } from './PeriodControls'
import { CsvButton, Metrics, ReportDataTable, ReportNotes } from './ReportTable'
import { useReport, useReportLookups } from './queries'

const reportDescriptions: Record<ReportKind, string> = {
  sales: 'Продажі, повернення та чисті суми.',
  profitability: 'Валовий прибуток за поточною собівартістю.',
  stock: 'Залишки, партії, рухи та списання.',
  purchases: 'Закупівлі й початкові залишки.',
  money: 'Рух коштів та баланси окремо за валютами.',
  expenses: 'Загальні та закупівельні витрати.',
  settlements: 'Баланси контрагентів за валютами.',
}
export function ReportsIndexPage() {
  const user = useCurrentUser().data
  return (
    <Stack spacing={2}>
      <PageHeader
        title="Звіти"
        description="Проведені документи, поточні залишки та поточна собівартість."
      />
      <CatalogSections
        action="Відкрити звіт"
        sections={reportKinds
          .filter(
            (kind) =>
              user &&
              (!reportExtraPermission[kind] ||
                hasAnyPermission(user, [reportExtraPermission[kind]!])),
          )
          .map((kind) => ({
            path: '/reports/' + kind,
            title: reportTitles[kind],
            description: reportDescriptions[kind],
            icon: 'chart',
          }))}
      />
    </Stack>
  )
}
function Filters({
  kind,
  value,
  change,
  lookups,
}: {
  kind: ReportKind
  value: ReportFilters
  change: (v: ReportFilters) => void
  lookups?: ReportLookups
}) {
  const selects: {
    key: keyof ReportFilters
    label: string
    options: { id: string; name: string }[]
  }[] = []
  const add = (key: keyof ReportFilters, label: string, options?: { id: string; name: string }[]) =>
    selects.push({ key, label, options: options ?? [] })
  if (['sales', 'profitability', 'stock', 'purchases'].includes(kind))
    add('productId', 'Номенклатура', lookups?.products)
  if (['sales', 'profitability', 'stock'].includes(kind))
    add('categoryId', 'Категорія', lookups?.categories)
  if (['sales', 'profitability', 'purchases', 'expenses', 'settlements'].includes(kind))
    add(
      'counterpartyId',
      kind === 'purchases'
        ? 'Постачальник'
        : kind === 'sales' || kind === 'profitability'
          ? 'Покупець'
          : 'Контрагент',
      lookups?.counterparties,
    )
  if (kind === 'sales') {
    add('createdBy', 'Менеджер / автор', lookups?.managers)
    add('priceTypeId', 'Тип ціни', lookups?.priceTypes)
  }
  if (['sales', 'profitability', 'stock'].includes(kind)) add('batchId', 'Партія', lookups?.batches)
  if (kind === 'stock') {
    add('storageLocationId', 'Склад', lookups?.locations)
    if (!['movements', 'writeoffs'].includes(value.view)) {
      add('retailPriceTypeId', 'Тип роздрібної ціни', lookups?.priceTypes)
      add('wholesalePriceTypeId', 'Тип оптової ціни', lookups?.priceTypes)
    }
  }
  if (['purchases', 'money', 'expenses', 'settlements'].includes(kind))
    add(
      'currencyId',
      'Валюта',
      lookups?.currencyIds.map((id) => ({ id, name: currencyName(id, lookups.currencyIds) })),
    )
  if (kind === 'money') {
    add('accountId', 'Рахунок', lookups?.accounts)
    add('operationType', 'Тип операції', [
      { id: 'MONEY_RECEIPT', name: 'Надходження' },
      { id: 'MONEY_PAYMENT', name: 'Виплата' },
      { id: 'MONEY_TRANSFER', name: 'Переказ' },
    ])
  }
  if (kind === 'expenses') {
    add('categoryId', 'Категорія витрати', lookups?.expenseCategories)
    add('isPurchaseExpense', 'Тип витрати', [
      { id: 'false', name: 'Загальні' },
      { id: 'true', name: 'Закупівельні' },
    ])
  }
  return (
    <Stack spacing={1.5}>
      {kind !== 'settlements' && (
        <PeriodControls value={value} change={(period) => change({ ...value, ...period })} />
      )}
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', lg: 'repeat(3,1fr)' },
          gap: 1.5,
        }}
      >
        {selects.map((s) => (
          <TextField
            select
            key={s.key}
            label={s.label}
            value={value[s.key]}
            onChange={(e) => change({ ...value, [s.key]: e.target.value })}
          >
            <MenuItem value="">Усі</MenuItem>
            {s.options.map((o) => (
              <MenuItem key={o.id} value={o.id}>
                {o.name}
              </MenuItem>
            ))}
          </TextField>
        ))}
      </Box>
      {kind === 'stock' && !['movements', 'writeoffs'].includes(value.view) && (
        <Stack direction="row" spacing={2} sx={{ flexWrap: 'wrap' }}>
          <FormControlLabel
            label="Тільки з залишком"
            control={
              <Checkbox
                checked={value.withStock}
                onChange={(e) => change({ ...value, withStock: e.target.checked })}
              />
            }
          />
          <FormControlLabel
            label="Тільки з резервом"
            control={
              <Checkbox
                checked={value.withReservations}
                onChange={(e) => change({ ...value, withReservations: e.target.checked })}
              />
            }
          />
        </Stack>
      )}
    </Stack>
  )
}
export function ReportPage({ kind }: { kind: ReportKind }) {
  const user = useCurrentUser().data
  const permitted = Boolean(
    user &&
      hasAnyPermission(user, ['reports.view']) &&
      (!reportExtraPermission[kind] || hasAnyPermission(user, [reportExtraPermission[kind]!])),
  )
  const [filters, setFilters] = useState(defaultReportFilters)
  const query = useReport(kind, filters, permitted)
  const lookups = useReportLookups(permitted)
  if (!permitted) return <ForbiddenState />
  const data = query.data
    ? visibleTable(query.data, Boolean(user?.permissions.includes('cost.view')))
    : undefined
  const filterFields = (
    <Filters kind={kind} value={filters} change={setFilters} lookups={lookups.data} />
  )
  const tabs =
    kind === 'stock'
      ? [
          ['', 'Поточні залишки'],
          ['batches', 'За партіями'],
          ['movements', 'Рух товару'],
          ['writeoffs', 'Списання'],
        ]
      : kind === 'settlements'
        ? [
            ['', 'Усі'],
            ['debtors', 'Нам винні'],
            ['creditors', 'Ми винні'],
          ]
        : []
  return (
    <Stack spacing={2}>
      <Button component={Link} to="/reports" sx={{ alignSelf: 'flex-start', p: 0 }}>
        До звітів
      </Button>
      <PageHeader
        title={reportTitles[kind]}
        description={reportDescriptions[kind]}
        actions={
          <CsvButton
            data={data}
            disabled={query.isFetching || !validPeriod(filters)}
            filename={`${kind}-${filters.from}-${filters.to}.csv`}
          />
        }
      />
      {tabs.length > 0 && (
        <Paper variant="outlined">
          <Tabs
            aria-label="Вид звіту"
            value={filters.view}
            onChange={(_, view: string) => setFilters({ ...filters, view })}
            variant="scrollable"
            allowScrollButtonsMobile
          >
            {tabs.map(([id, label]) => (
              <Tab key={id} label={label} value={id} />
            ))}
          </Tabs>
        </Paper>
      )}
      <Paper variant="outlined">
        <FilterBar mobileSheet>
          <Box sx={{ width: '100%' }}>{filterFields}</Box>
        </FilterBar>
      </Paper>
      {!validPeriod(filters) ? (
        <Alert severity="warning">Вкажіть коректний період: дата «від» не пізніше дати «до».</Alert>
      ) : query.isPending ? (
        <ListLoading label="Завантаження звіту" />
      ) : query.isError ? (
        <RequestErrorState
          error={query.error}
          retry={() => {
            void query.refetch()
          }}
        />
      ) : (
        data && (
          <>
            <Metrics metrics={data.metrics} currencies={lookups.data?.currencyIds ?? []} />
            <ReportDataTable data={data} currencies={lookups.data?.currencyIds} />
            {kind === 'profitability' && (
              <Stack spacing={2}>
                <Typography variant="h2">Продажі нижче собівартості</Typography>
                <ReportDataTable
                  data={{
                    ...data,
                    title: 'Продажі нижче собівартості',
                    rows: data.rows.filter((r) => r.belowCost === true),
                  }}
                  currencies={lookups.data?.currencyIds}
                  emptyTitle="Продажів нижче собівартості немає"
                />
              </Stack>
            )}
            <ReportNotes notes={data.notes} />
          </>
        )
      )}
      {lookups.isError && (
        <RequestErrorState
          error={lookups.error}
          retry={() => {
            void lookups.refetch()
          }}
        />
      )}
    </Stack>
  )
}
