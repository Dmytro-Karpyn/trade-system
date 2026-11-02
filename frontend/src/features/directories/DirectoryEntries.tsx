import type { ReactNode } from 'react'
import { Box, Stack, Typography, useMediaQuery, useTheme } from '@mui/material'
import { ActiveBadge } from '../../components/ActiveBadge'
import { DataTable } from '../../components/DataTable'
import { Icon } from '../../components/Icon'
import { MobileEntityCard } from '../../components/MobileEntityCard'

export function DirectoryEntries<Row extends { id: string; name: string; active: boolean }>({
  label,
  rows,
  actions,
  info,
  selectedId,
  select,
}: {
  label: string
  rows: { node: Row; depth: number; path: string }[]
  actions?: (row: Row) => ReactNode
  info: (row: Row) => ReactNode
  selectedId?: string | null
  select?: (row: Row) => void
}) {
  const mobile = useMediaQuery(useTheme().breakpoints.down('md'))
  const name = (row: (typeof rows)[number]) => (
    <Typography
      component="span"
      variant="body2"
      title={row.path}
      sx={{
        display: 'block',
        pl: Math.min(row.depth, mobile ? 4 : 6) * 1.5,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
      }}
    >
      {row.depth > 0 && (
        <Icon
          name="arrow"
          sx={{ fontSize: 14, mr: 0.5, verticalAlign: 'middle', color: 'text.secondary' }}
        />
      )}
      {row.node.name}
    </Typography>
  )
  return mobile ? (
    <Box component="ul" aria-label={label} sx={{ p: 0, m: 0, listStyle: 'none' }}>
      {rows.map((row) => (
        <MobileEntityCard
          key={row.node.id}
          title={name(row)}
          status={<ActiveBadge active={row.node.active} />}
        >
          <Stack spacing={1}>
            {row.depth > 0 && (
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ overflowWrap: 'anywhere' }}
              >
                {row.path}
              </Typography>
            )}
            <Typography variant="body2" color="text.secondary">
              {info(row.node)}
            </Typography>
            {actions?.(row.node)}
          </Stack>
        </MobileEntityCard>
      ))}
    </Box>
  ) : (
    <DataTable
      label={label}
      compact
      rows={rows}
      rowId={(row) => row.node.id}
      selectedId={selectedId}
      select={select ? (row) => select(row.node) : undefined}
      columns={[
        {
          key: 'name',
          label: 'Назва',
          render: (row) => (
            <Box sx={{ minWidth: 0 }}>
              {name(row)}
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{
                  display: 'block',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                }}
              >
                {info(row.node)}
              </Typography>
            </Box>
          ),
        },
        {
          key: 'active',
          label: 'Активний',
          width: 105,
          render: (row) => <ActiveBadge active={row.node.active} />,
        },
        ...(actions
          ? [
              {
                key: 'actions',
                label: 'Дії',
                width: 100,
                render: (row: (typeof rows)[number]) => actions(row.node),
              },
            ]
          : []),
      ]}
    />
  )
}
