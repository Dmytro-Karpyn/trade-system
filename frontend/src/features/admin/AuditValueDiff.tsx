import { Box, Paper, Stack, Typography, useMediaQuery, useTheme } from '@mui/material'
import { DataTable } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { tokens } from '../../theme/tokens'

const fieldNames: Record<string, string> = {
  Name: 'Назва',
  FullName: 'Повне ім’я',
  Username: 'Логін',
  UserName: 'Логін',
  Active: 'Активний',
  Description: 'Опис',
  SortOrder: 'Порядок сортування',
  DefaultMarkupPercent: 'Націнка за замовчуванням, %',
  UnitPrice: 'Ціна',
  PriceUah: 'Ціна UAH',
  Comment: 'Коментар',
  InternalNote: 'Внутрішня примітка',
  Status: 'Статус',
  Permissions: 'Права',
  PermissionCodes: 'Права',
  ItemType: 'Тип номенклатури',
}
function flatten(value: unknown, path = '', result = new Map<string, unknown>()) {
  if (
    value !== null &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length
  )
    for (const [key, child] of Object.entries(value))
      flatten(child, path ? path + '.' + key : key, result)
  else if (path || value != null) result.set(path || 'Значення', value)
  return result
}
const text = (value: unknown) =>
  value === undefined
    ? 'Відсутні'
    : value === null
      ? 'Не задано'
      : typeof value === 'boolean'
        ? value
          ? 'Так'
          : 'Ні'
        : typeof value === 'object'
          ? JSON.stringify(value, null, 2)
          : String(value)
export function AuditValueDiff({
  oldValues,
  newValues,
}: {
  oldValues: unknown
  newValues: unknown
}) {
  const old = flatten(oldValues),
    next = flatten(newValues),
    mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  const rows = [...new Set([...old.keys(), ...next.keys()])].map((key) => ({
    key,
    old: old.get(key),
    next: next.get(key),
    changed: JSON.stringify(old.get(key)) !== JSON.stringify(next.get(key)),
  }))
  const label = (key: string) =>
    key
      .split('.')
      .map((part) => fieldNames[part] ?? part)
      .join(' / ')
  const value = (row: (typeof rows)[number], side: 'old' | 'next') => (
    <Box
      component="span"
      sx={{
        display: 'block',
        p: 1,
        borderRadius: 0.5,
        whiteSpace: 'pre-wrap',
        overflowWrap: 'anywhere',
        ...(row.changed && {
          bgcolor:
            side === 'old' ? tokens.statuses.error.background : tokens.statuses.success.background,
          color: 'text.primary',
        }),
      }}
    >
      {text(row[side])}
    </Box>
  )
  return (
    <Paper variant="outlined">
      {!rows.length ? (
        <EmptyState title="Змін полів немає" />
      ) : mobile ? (
        <Stack component="ul" aria-label="Зміни полів" sx={{ listStyle: 'none', m: 0, p: 0 }}>
          {rows.map((row) => (
            <Box
              component="li"
              key={row.key}
              sx={{ p: 1.5, borderBottom: '1px solid', borderColor: 'divider' }}
            >
              <Typography variant="subtitle1" sx={{ mb: 1 }}>
                {label(row.key)}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                Попередні значення
              </Typography>
              {value(row, 'old')}
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                Нові значення
              </Typography>
              {value(row, 'next')}
            </Box>
          ))}
        </Stack>
      ) : (
        <DataTable
          label="Зміни полів"
          rows={rows}
          rowId={(row) => row.key}
          columns={[
            { key: 'field', label: 'Поле', render: (row) => label(row.key) },
            { key: 'old', label: 'Попередні значення', render: (row) => value(row, 'old') },
            { key: 'next', label: 'Нові значення', render: (row) => value(row, 'next') },
          ]}
        />
      )}
    </Paper>
  )
}
