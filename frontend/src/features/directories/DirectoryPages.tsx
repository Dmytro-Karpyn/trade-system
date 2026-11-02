import { useEffect, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { CatalogSections } from '../../components/CatalogSections'
import type { CatalogSection } from '../../components/CatalogSections'
import { EditorSurface } from '../../components/EditorSurface'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { FilterBar } from '../../components/FilterBar'
import { PageHeader } from '../../components/PageHeader'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { useDetailSelection } from '../../components/useDetailSelection'
import { DirectoryEntries } from './DirectoryEntries'
import { Icon } from '../../components/Icon'
import { ActiveBadge } from '../../components/ActiveBadge'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { currencyName, money } from '../purchases/contracts'
import {
  directoryConfig,
  directoryDefaults,
  directoryFormSchema,
  directoryKinds,
  directoryTree,
  excludedCategories,
  managementError,
} from './contracts'
import type { DirectoryKind, DirectoryRow, DirectoryValues } from './contracts'
import { useCurrencies, useDefaultPrices, useDirectory, useDirectorySave } from './queries'
import { useManagementNotice } from './notices'

export function DirectoriesIndexPage() {
  const user = useCurrentUser().data
  const icons = {
    categories: 'folder',
    units: 'box',
    'price-types': 'trend',
    'storage-locations': 'stock',
    'write-off-reasons': 'expense',
    currencies: 'wallet',
  } as const
  const sections: CatalogSection[] = directoryKinds
    .filter((kind) => user && hasAnyPermission(user, directoryConfig[kind].read))
    .map((kind) => ({
      path: '/directories/' + kind,
      title: directoryConfig[kind].title,
      description: directoryConfig[kind].description,
      icon: icons[kind],
    }))
  if (user && hasAnyPermission(user, ['settings.manage', 'expense.view', 'expense.create']))
    sections.push({
      path: '/expense-categories',
      title: 'Категорії витрат',
      description: 'Дерево загальних і закупівельних витрат.',
      icon: 'expense',
    })
  return (
    <Stack spacing={2}>
      <PageHeader
        title="Довідники"
        description="Налаштування номенклатури, цін і місць зберігання."
      />
      <CatalogSections sections={sections} />
    </Stack>
  )
}
function DirectoryEditor({
  kind,
  row,
  parentId,
  rows,
  close,
  embedded,
  onDirtyChange,
}: {
  kind: DirectoryKind
  row?: DirectoryRow
  parentId?: string
  rows: DirectoryRow[]
  close: () => void
  embedded: boolean
  onDirtyChange: (dirty: boolean) => void
}) {
  const save = useDirectorySave(kind)
  const expense = useDirectory('expense-categories', kind === 'write-off-reasons')
  const notify = useManagementNotice()
  const [error, setErrorState] = useState<unknown>(null)
  const {
    register,
    control,
    handleSubmit,
    setError,
    formState: { errors, isDirty },
  } = useForm<DirectoryValues>({
    resolver: zodResolver(directoryFormSchema),
    defaultValues: directoryDefaults(row, parentId),
  })
  useEffect(() => onDirtyChange(isDirty), [isDirty, onDirtyChange])
  const excluded = excludedCategories(rows, row?.id)
  const submit = handleSubmit(async (values) => {
    if (kind === 'write-off-reasons' && !values.expenseCategoryId) {
      setError('expenseCategoryId', { message: 'Виберіть категорію витрати.' })
      return
    }
    try {
      await save.mutateAsync({ id: row?.id, values })
      notify({ severity: 'success', message: 'Запис збережено.' })
      close()
    } catch (caught) {
      setErrorState(caught)
      notify({ severity: 'error', message: managementError(caught) })
    }
  })
  return (
    <EditorSurface
      embedded={embedded}
      busy={save.isPending}
      close={close}
      title={`${row ? 'Редагувати запис' : 'Новий запис'} · ${directoryConfig[kind].title}`}
      actions={
        <>
          <Button disabled={save.isPending} onClick={close}>
            Скасувати
          </Button>
          <Button variant="contained" form="directory-form" type="submit" disabled={save.isPending}>
            Зберегти
          </Button>
        </>
      }
    >
      {
        <Stack
          component="form"
          id="directory-form"
          noValidate
          onSubmit={(e) => {
            void submit(e)
          }}
          spacing={2.5}
          sx={{ pt: 1 }}
        >
          {error != null && <Alert severity="error">{managementError(error)}</Alert>}
          <TextField
            label="Назва"
            required
            autoFocus
            disabled={save.isPending}
            {...register('name')}
            error={Boolean(errors.name)}
            helperText={errors.name?.message}
          />
          {kind === 'categories' && (
            <>
              <Controller
                name="parentId"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    select
                    label="Батьківська категорія"
                    disabled={save.isPending}
                  >
                    <MenuItem value="">Без батьківської категорії</MenuItem>
                    {directoryTree(rows)
                      .filter((n) => !excluded.has(n.node.id))
                      .map((n) => (
                        <MenuItem key={n.node.id} value={n.node.id}>
                          {n.path}
                          {n.node.active ? '' : ' (неактивна)'}
                        </MenuItem>
                      ))}
                  </TextField>
                )}
              />
              <TextField label="Опис" multiline minRows={2} {...register('description')} />
            </>
          )}
          {['categories', 'price-types'].includes(kind) && (
            <Controller
              name="sortOrder"
              control={control}
              render={({ field }) => (
                <TextField
                  label="Порядок сортування"
                  type="number"
                  value={field.value}
                  onChange={(e) => field.onChange(Number(e.target.value))}
                  error={Boolean(errors.sortOrder)}
                  helperText={errors.sortOrder?.message}
                />
              )}
            />
          )}{' '}
          {kind === 'price-types' && (
            <Controller
              name="defaultMarkupPercent"
              control={control}
              render={({ field }) => (
                <TextField
                  label="Націнка за замовчуванням, %"
                  type="number"
                  value={field.value}
                  onChange={(e) => field.onChange(Number(e.target.value))}
                  error={Boolean(errors.defaultMarkupPercent)}
                  helperText={
                    errors.defaultMarkupPercent?.message ??
                    'Націнка використовується для початкової рекомендованої ціни нових партій.'
                  }
                  slotProps={{ htmlInput: { min: 0, step: '0.0001' } }}
                />
              )}
            />
          )}{' '}
          {kind === 'write-off-reasons' && (
            <>
              <Controller
                name="expenseCategoryId"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    select
                    required
                    label="Категорія витрати"
                    error={Boolean(errors.expenseCategoryId)}
                    helperText={errors.expenseCategoryId?.message}
                  >
                    <MenuItem value="">Виберіть категорію</MenuItem>
                    {directoryTree(expense.data ?? []).map((n) => (
                      <MenuItem key={n.node.id} value={n.node.id}>
                        {n.path}
                      </MenuItem>
                    ))}
                  </TextField>
                )}
              />
              {expense.isError && (
                <RequestErrorState
                  error={expense.error}
                  retry={() => {
                    void expense.refetch()
                  }}
                />
              )}
            </>
          )}
          <Controller
            name="active"
            control={control}
            render={({ field }) => (
              <FormControlLabel
                label="Активний запис"
                control={
                  <Switch
                    checked={field.value}
                    onChange={field.onChange}
                    disabled={save.isPending}
                  />
                }
              />
            )}
          />
        </Stack>
      }
    </EditorSurface>
  )
}

export function DirectoryPage({ kind }: { kind: DirectoryKind }) {
  const query = useDirectory(kind),
    currencies = useCurrencies(kind === 'currencies'),
    expense = useDirectory('expense-categories', kind === 'write-off-reasons'),
    defaults = useDefaultPrices()
  const user = useCurrentUser().data,
    notify = useManagementNotice(),
    desktop = useMediaQuery(useTheme().breakpoints.up('lg')),
    selection = useDetailSelection()
  const [active, setActive] = useState(''),
    [search, setSearch] = useState('')
  const config = directoryConfig[kind],
    canWrite = kind !== 'currencies' && Boolean(user && hasAnyPermission(user, config.write))
  const rows =
    kind === 'categories'
      ? directoryTree(query.data ?? [])
      : (query.data ?? []).map((node) => ({ node, depth: 0, path: node.name }))
  const visible = rows.filter(
    (row) =>
      (!active || row.node.active === (active === 'true')) &&
      row.path.toLocaleLowerCase('uk').includes(search.trim().toLocaleLowerCase('uk')),
  )
  const actions = (row: DirectoryRow) =>
    canWrite && (
      <Stack direction="row" spacing={0.5}>
        {kind === 'categories' && (
          <IconButton
            size="small"
            title="Додати підкатегорію"
            aria-label={'Додати підкатегорію ' + row.name}
            onClick={() => selection.select('new:' + row.id)}
          >
            <Icon name="plus" fontSize="small" />
          </IconButton>
        )}
        <IconButton
          title="Редагувати"
          size="small"
          aria-label={'Редагувати ' + row.name}
          onClick={() => selection.select(row.id)}
        >
          <Icon name="edit" fontSize="small" />
        </IconButton>
      </Stack>
    )
  const info = (row: DirectoryRow) =>
    kind === 'price-types'
      ? `Націнка: ${money(row.defaultMarkupPercent ?? 0)}% · Порядок: ${row.sortOrder ?? 0}`
      : kind === 'categories'
        ? row.description || `Порядок: ${row.sortOrder ?? 0}`
        : kind === 'write-off-reasons'
          ? directoryTree(expense.data ?? []).find((n) => n.node.id === row.expenseCategoryId)
              ?.path || 'Категорія витрати'
          : ' '
  const pending = kind === 'currencies' ? currencies.isPending : query.isPending,
    error = kind === 'currencies' ? currencies.error : query.error
  const selected = query.data?.find((row) => row.id === selection.id)
  const editor =
    selection.id && canWrite ? (
      <DirectoryEditor
        key={selection.id}
        embedded={desktop}
        kind={kind}
        row={selected}
        parentId={selection.id.startsWith('new:') ? selection.id.slice(4) : undefined}
        rows={query.data ?? []}
        close={selection.deleted}
        onDirtyChange={selection.dirtyChanged}
      />
    ) : null
  const content = (
    <Stack spacing={2}>
      <Button component={Link} to="/directories" sx={{ alignSelf: 'flex-start', p: 0 }}>
        До довідників
      </Button>
      <PageHeader
        title={config.title}
        actions={
          <>
            {kind === 'price-types' && canWrite && (
              <Button
                disabled={defaults.isPending}
                onClick={() => {
                  void defaults
                    .mutateAsync()
                    .then(() =>
                      notify({
                        severity: 'success',
                        message: 'Базові типи цін додано. Наявні ціни збережено.',
                      }),
                    )
                    .catch((error) =>
                      notify({ severity: 'error', message: managementError(error) }),
                    )
                }}
              >
                Додати базові типи
              </Button>
            )}
            {canWrite && (
              <Button variant="contained" onClick={() => selection.select('new')}>
                Додати
              </Button>
            )}
          </>
        }
      />
      {kind === 'price-types' && (
        <Alert severity="info">
          Використовується для початкової рекомендованої ціни нових партій. Збережені ціни партій
          автоматично не змінюються.
        </Alert>
      )}
      {kind !== 'currencies' && (
        <Paper variant="outlined">
          <FilterBar mobileSheet>
            <TextField
              label="Пошук"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              sx={{ flex: 1 }}
            />
            <TextField
              label="Статус"
              select
              value={active}
              onChange={(event) => setActive(event.target.value)}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="">Усі</MenuItem>
              <MenuItem value="true">Активні</MenuItem>
              <MenuItem value="false">Неактивні</MenuItem>
            </TextField>
          </FilterBar>
        </Paper>
      )}
      <Paper variant="outlined">
        {pending ? (
          <ListLoading label="Завантаження довідника" />
        ) : error ? (
          <RequestErrorState
            error={error}
            retry={() => {
              if (kind === 'currencies') void currencies.refetch()
              else void query.refetch()
            }}
          />
        ) : kind === 'currencies' ? (
          <Stack component="ul" aria-label="Валюти" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {currencies.data?.map((currency) => (
              <Stack
                component="li"
                key={currency.id}
                direction="row"
                spacing={2}
                sx={{
                  p: 2,
                  borderBottom: '1px solid',
                  borderColor: 'divider',
                  alignItems: 'center',
                }}
              >
                <Box sx={{ flex: 1 }}>
                  <Typography variant="subtitle1">
                    {currency.code ??
                      currencyName(
                        currency.id,
                        currencies.data.map((item) => item.id),
                      )}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {currency.name ?? 'Код і довідникова назва ще не задані.'}
                  </Typography>
                </Box>
                {currency.isBase && <Chip label="Базова" size="small" variant="outlined" />}
                {currency.active !== null && <ActiveBadge active={currency.active} />}
              </Stack>
            ))}
          </Stack>
        ) : !visible.length ? (
          <EmptyState title="Записів ще немає" description="Додайте запис або змініть фільтр." />
        ) : (
          <DirectoryEntries
            label={config.title}
            rows={visible}
            info={info}
            actions={canWrite ? actions : undefined}
            selectedId={selection.id}
            select={canWrite && desktop ? (row) => selection.select(row.id) : undefined}
          />
        )}
      </Paper>
    </Stack>
  )
  return (
    <>
      <SplitViewLayout
        split={desktop && canWrite}
        list={content}
        detailLabel="Редактор довідника"
        detail={
          editor ?? (
            <Paper variant="outlined">
              <EmptyState
                icon="folder"
                title="Оберіть запис"
                description="Виберіть запис для редагування або додайте новий."
              />
            </Paper>
          )
        }
      />
      {!desktop && editor}
      <ConfirmDialog
        open={selection.pending !== null}
        title="Незбережені зміни"
        action="Відкинути зміни"
        danger
        close={selection.cancel}
        confirm={selection.discard}
      >
        Відкинути незбережені зміни та відкрити інший запис?
      </ConfirmDialog>
    </>
  )
}
