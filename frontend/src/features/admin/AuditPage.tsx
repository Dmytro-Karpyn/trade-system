import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Drawer,
  MenuItem,
  Paper,
  Stack,
  TablePagination,
  TextField,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { Link as MuiLink } from '@mui/material'
import { Link } from 'react-router-dom'
import { useCurrentUser } from '../../auth/queries'
import { DataTable } from '../../components/DataTable'
import { DocumentLink } from '../../components/DocumentLink'
import { EmptyState } from '../../components/EmptyState'
import { FilterBar } from '../../components/FilterBar'
import { ListLoading } from '../../components/ListLoading'
import { MobileEntityCard } from '../../components/MobileEntityCard'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { useDebouncedValue } from '../../components/useDebouncedValue'
import { auditEventNames, entityNames, timestamp } from './contracts'
import { useAudit, useAuditDetail, useAuditLookups } from './queries'
import type { AuditFilters } from './queries'
import { AuditValueDiff } from './AuditValueDiff'

function EntityLink({ type, id, label }: { type: string; id: string; label: string }) {
  const user = useCurrentUser().data
  const path =
    type === 'User' && user?.permissions.includes('users.manage')
      ? '/admin/users/'
      : type === 'Role' && user?.permissions.includes('roles.manage')
        ? '/admin/roles/'
        : type === 'Product' && user?.permissions.includes('product.view')
          ? '/products/'
          : null
  return path ? (
    <MuiLink component={Link} to={path + id}>
      {label}
    </MuiLink>
  ) : (
    <>{label}</>
  )
}
function AuditDetails({ id, close }: { id: string; close: () => void }) {
  const query = useAuditDetail(id)
  return (
    <Drawer
      anchor="right"
      open
      onClose={close}
      slotProps={{
        paper: {
          role: 'dialog',
          'aria-label': 'Деталі події',
          sx: { width: { xs: '100vw', md: 760 }, maxWidth: '100vw' },
        },
      }}
    >
      <Stack spacing={2} sx={{ p: 2 }}>
        <PageHeader
          title="Деталі події"
          secondary
          actions={<Button onClick={close}>Закрити деталі</Button>}
        />
        {query.isPending ? (
          <ListLoading label="Завантаження деталей аудиту" />
        ) : query.isError ? (
          <RequestErrorState
            error={query.error}
            retry={() => {
              void query.refetch()
            }}
          />
        ) : (
          <>
            <Typography variant="body2" color="text.secondary">
              {timestamp(query.data.entry.occurredAt)} ·{' '}
              {query.data.entry.username || 'Користувач без імені'}
            </Typography>
            <Typography variant="subtitle1">
              {auditEventNames[query.data.entry.eventType] ?? query.data.entry.eventType} ·{' '}
              {entityNames[query.data.entry.entityType] ?? query.data.entry.entityType}
            </Typography>
            <Typography>
              <EntityLink
                type={query.data.entry.entityType}
                id={query.data.entry.entityId}
                label={query.data.entry.summary}
              />
            </Typography>
            {query.data.entry.documentId && query.data.entry.documentType && (
              <Typography>
                Документ:{' '}
                <DocumentLink
                  id={query.data.entry.documentId}
                  type={query.data.entry.documentType}
                  number={query.data.entry.documentNumber}
                />
              </Typography>
            )}
            <AuditValueDiff oldValues={query.data.oldValues} newValues={query.data.newValues} />
          </>
        )}
      </Stack>
    </Drawer>
  )
}
export function AuditPage() {
  const user = useCurrentUser().data
  const [filters, setFilters] = useState<AuditFilters>({
      from: '',
      to: '',
      userId: '',
      eventType: '',
      entityType: '',
      documentId: '',
      search: '',
      skip: 0,
    }),
    search = useDebouncedValue(filters.search, 300)
  const query = useAudit({ ...filters, search }),
    lookups = useAuditLookups(),
    [selected, setSelected] = useState<string | null>(null),
    mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  const change = (key: keyof AuditFilters, value: string) =>
    setFilters({ ...filters, [key]: value, skip: 0 })
  const selectors = [
    ['userId', 'Користувач', lookups.data?.users ?? []],
    [
      'eventType',
      'Подія',
      lookups.data?.eventTypes.map((id) => ({ id, name: auditEventNames[id] ?? id })) ?? [],
    ],
    [
      'entityType',
      'Тип сутності',
      lookups.data?.entityTypes.map((id) => ({ id, name: entityNames[id] ?? id })) ?? [],
    ],
    ['documentId', 'Документ', lookups.data?.documents ?? []],
  ] as const
  const rows = query.data?.items ?? []
  return (
    <Stack spacing={2}>
      <PageHeader
        title="Журнал аудиту"
        description="Історія змін. Записи доступні лише для перегляду."
      />
      <Paper variant="outlined">
        <FilterBar mobileSheet>
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: 'minmax(0,1fr)',
                sm: 'repeat(2,minmax(0,1fr))',
                lg: 'repeat(4,minmax(0,1fr))',
              },
              gap: 1.5,
              width: '100%',
            }}
          >
            {(['from', 'to'] as const).map((key) => (
              <TextField
                key={key}
                label={key === 'from' ? 'Період від' : 'Період до'}
                type="date"
                value={filters[key]}
                onChange={(event) => change(key, event.target.value)}
                slotProps={{ inputLabel: { shrink: true } }}
              />
            ))}
            {selectors.map(([key, label, options]) => (
              <TextField
                key={key}
                label={label}
                select
                value={filters[key]}
                onChange={(event) => change(key, event.target.value)}
              >
                <MenuItem value="">Усі</MenuItem>
                {options.map((option) => (
                  <MenuItem key={option.id} value={option.id}>
                    {option.name}
                  </MenuItem>
                ))}
              </TextField>
            ))}
            <TextField
              label="Пошук / ID сутності"
              value={filters.search}
              onChange={(event) => change('search', event.target.value)}
            />
          </Box>
        </FilterBar>
      </Paper>
      {filters.from && filters.to && filters.from > filters.to && (
        <Alert severity="warning">Дата «від» не може бути пізніше дати «до».</Alert>
      )}
      <Paper variant="outlined">
        {query.isPending ? (
          <ListLoading label="Завантаження аудиту" />
        ) : query.isError ? (
          <RequestErrorState
            error={query.error}
            retry={() => {
              void query.refetch()
            }}
          />
        ) : !rows.length ? (
          <EmptyState
            title="Подій аудиту ще немає"
            description="Змініть фільтри або виконайте зміну даних."
          />
        ) : mobile ? (
          <Stack component="ul" aria-label="Події аудиту" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {rows.map((row) => (
              <MobileEntityCard
                key={row.id}
                title={
                  <Typography variant="subtitle1">
                    {auditEventNames[row.eventType] ?? row.eventType}
                  </Typography>
                }
                status={
                  <Typography variant="caption">
                    {entityNames[row.entityType] ?? row.entityType}
                  </Typography>
                }
              >
                <Typography variant="body2" color="text.secondary">
                  {timestamp(row.occurredAt)} · {row.username || 'Користувач без імені'}
                </Typography>
                <Typography variant="body2" sx={{ my: 0.75 }}>
                  {row.summary}
                </Typography>
                {row.documentId && row.documentType && (
                  <DocumentLink
                    id={row.documentId}
                    type={row.documentType}
                    number={row.documentNumber}
                  />
                )}
                <Button onClick={() => setSelected(row.id)} sx={{ display: 'block', px: 0 }}>
                  Деталі
                </Button>
              </MobileEntityCard>
            ))}
          </Stack>
        ) : (
          <DataTable
            label="Події аудиту"
            rows={rows}
            rowId={(row) => row.id}
            selectedId={selected}
            select={(row) => setSelected(row.id)}
            minWidth={950}
            columns={[
              {
                key: 'date',
                label: 'Дата / час',
                render: (row) => (
                  <Typography variant="caption" sx={{ whiteSpace: 'nowrap' }}>
                    {timestamp(row.occurredAt)}
                  </Typography>
                ),
              },
              {
                key: 'user',
                label: 'Користувач',
                render: (row) =>
                  row.actorId && user?.permissions.includes('users.manage') ? (
                    <MuiLink component={Link} to={'/admin/users/' + row.actorId}>
                      {row.username || 'Користувач без імені'}
                    </MuiLink>
                  ) : (
                    row.username || 'Користувач без імені'
                  ),
              },
              {
                key: 'event',
                label: 'Подія',
                render: (row) => auditEventNames[row.eventType] ?? row.eventType,
              },
              {
                key: 'entity',
                label: 'Сутність',
                render: (row) => entityNames[row.entityType] ?? row.entityType,
              },
              {
                key: 'document',
                label: 'Документ',
                render: (row) =>
                  row.documentId && row.documentType ? (
                    <DocumentLink
                      id={row.documentId}
                      type={row.documentType}
                      number={row.documentNumber}
                    />
                  ) : (
                    '—'
                  ),
              },
              {
                key: 'summary',
                label: 'Зміст',
                render: (row) => (
                  <Box>
                    <EntityLink type={row.entityType} id={row.entityId} label={row.summary} />
                    <Button onClick={() => setSelected(row.id)} size="small" sx={{ ml: 1 }}>
                      Деталі
                    </Button>
                  </Box>
                ),
              },
            ]}
          />
        )}
        <TablePagination
          component="div"
          count={query.data?.total ?? 0}
          page={Math.floor(filters.skip / 50)}
          rowsPerPage={50}
          rowsPerPageOptions={[50]}
          onPageChange={(_, page) => setFilters({ ...filters, skip: page * 50 })}
          labelDisplayedRows={({ from, to, count }) => `${from}–${to} з ${count}`}
          labelRowsPerPage="На сторінці:"
          sx={{ '.MuiTablePagination-toolbar': { flexWrap: 'wrap' } }}
        />
      </Paper>
      {lookups.isError && (
        <RequestErrorState
          error={lookups.error}
          retry={() => {
            void lookups.refetch()
          }}
        />
      )}
      {selected && <AuditDetails id={selected} close={() => setSelected(null)} />}
    </Stack>
  )
}
