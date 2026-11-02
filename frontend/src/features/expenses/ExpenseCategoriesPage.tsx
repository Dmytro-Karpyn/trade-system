import { useEffect, useState } from 'react'
import {
  Alert,
  Button,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { EditorSurface } from '../../components/EditorSurface'
import { EmptyState } from '../../components/EmptyState'
import { FilterBar } from '../../components/FilterBar'
import { Icon } from '../../components/Icon'
import { ListLoading } from '../../components/ListLoading'
import { PageHeader } from '../../components/PageHeader'
import { RequestErrorState } from '../../components/RequestState'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { useDetailSelection } from '../../components/useDetailSelection'
import { DirectoryEntries } from '../directories/DirectoryEntries'
import { categoryFormSchema, categoryTree, excludedParents, expenseError } from './contracts'
import type { CategoryValues, ExpenseCategory } from './contracts'
import { useExpenseNotice } from './notices'
import { useExpenseCategories, useSaveCategory } from './queries'

function CategoryEditor({
  category,
  parentId,
  categories,
  close,
  embedded,
  onDirtyChange,
}: {
  category?: ExpenseCategory
  parentId?: string
  categories: ExpenseCategory[]
  close: () => void
  embedded: boolean
  onDirtyChange: (dirty: boolean) => void
}) {
  const mutation = useSaveCategory(),
    notify = useExpenseNotice(),
    [error, setError] = useState<unknown>(null)
  const {
    control,
    register,
    handleSubmit,
    formState: { errors, isDirty },
  } = useForm<CategoryValues>({
    resolver: zodResolver(categoryFormSchema),
    defaultValues: {
      name: category?.name ?? '',
      parentId: category?.parentId ?? parentId ?? '',
      active: category?.active ?? true,
    },
  })
  useEffect(() => onDirtyChange(isDirty), [isDirty, onDirtyChange])
  const excluded = excludedParents(categories, category?.id)
  const submit = handleSubmit(async (input) => {
    try {
      await mutation.mutateAsync({ id: category?.id, input })
      notify({ severity: 'success', message: 'Категорію збережено.' })
      close()
    } catch (caught) {
      setError(caught)
      notify({ severity: 'error', message: expenseError(caught) })
    }
  })
  return (
    <EditorSurface
      embedded={embedded}
      busy={mutation.isPending}
      close={close}
      title={category ? 'Редагувати категорію' : 'Нова категорія витрат'}
      actions={
        <>
          <Button disabled={mutation.isPending} onClick={close}>
            Скасувати
          </Button>
          <Button
            form="expense-category-form"
            type="submit"
            variant="contained"
            disabled={mutation.isPending}
          >
            {mutation.isPending ? 'Зберігаємо…' : 'Зберегти категорію'}
          </Button>
        </>
      }
    >
      <Stack
        component="form"
        noValidate
        id="expense-category-form"
        spacing={2}
        sx={{ pt: 1 }}
        onSubmit={(event) => {
          void submit(event)
        }}
      >
        {error != null && <Alert severity="error">{expenseError(error)}</Alert>}
        <TextField
          label="Назва категорії"
          autoFocus
          required
          disabled={mutation.isPending}
          {...register('name')}
          error={Boolean(errors.name)}
          helperText={errors.name?.message}
        />
        <Controller
          name="parentId"
          control={control}
          render={({ field }) => (
            <TextField
              {...field}
              select
              label="Батьківська категорія"
              disabled={mutation.isPending}
              error={Boolean(errors.parentId)}
              helperText={errors.parentId?.message}
            >
              <MenuItem value="">Без батьківської категорії</MenuItem>
              {categoryTree(categories)
                .filter((row) => !excluded.has(row.category.id))
                .map((row) => (
                  <MenuItem key={row.category.id} value={row.category.id} title={row.path}>
                    {row.path}
                    {row.category.active ? '' : ' (неактивна)'}
                  </MenuItem>
                ))}
            </TextField>
          )}
        />
        <Controller
          name="active"
          control={control}
          render={({ field }) => (
            <FormControlLabel
              label="Активна"
              control={
                <Switch
                  checked={field.value}
                  onChange={field.onChange}
                  disabled={mutation.isPending}
                />
              }
            />
          )}
        />
      </Stack>
    </EditorSurface>
  )
}
export function ExpenseCategoriesPage() {
  const query = useExpenseCategories(),
    user = useCurrentUser().data,
    canWrite = Boolean(user && hasAnyPermission(user, ['expense.create', 'settings.manage'])),
    desktop = useMediaQuery(useTheme().breakpoints.up('lg')),
    selection = useDetailSelection()
  const [search, setSearch] = useState(''),
    [active, setActive] = useState('')
  const rows = categoryTree(query.data ?? [])
    .filter(
      (row) =>
        row.path.toLocaleLowerCase('uk').includes(search.trim().toLocaleLowerCase('uk')) &&
        (!active || row.category.active === (active === 'true')),
    )
    .map((row) => ({ node: row.category, depth: row.depth, path: row.path }))
  const actions = (category: ExpenseCategory) =>
    canWrite && (
      <Stack direction="row" spacing={0.5}>
        <IconButton
          size="small"
          title="Додати підкатегорію"
          aria-label={`Додати підкатегорію ${category.name}`}
          onClick={() => selection.select('new:' + category.id)}
        >
          <Icon name="plus" fontSize="small" />
        </IconButton>
        <IconButton
          title="Редагувати"
          size="small"
          aria-label={`Редагувати категорію ${category.name}`}
          onClick={() => selection.select(category.id)}
        >
          <Icon name="edit" fontSize="small" />
        </IconButton>
      </Stack>
    )
  const editor =
    selection.id && canWrite ? (
      <CategoryEditor
        key={selection.id}
        category={query.data?.find((row) => row.id === selection.id)}
        parentId={selection.id.startsWith('new:') ? selection.id.slice(4) : undefined}
        categories={query.data ?? []}
        embedded={desktop}
        close={selection.deleted}
        onDirtyChange={selection.dirtyChanged}
      />
    ) : null
  const content = (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2}>
        <Button component={Link} to="/directories" sx={{ p: 0 }}>
          До довідників
        </Button>
        <Button component={Link} to="/expenses" startIcon={<Icon name="back" />}>
          До витрат
        </Button>
      </Stack>
      <PageHeader
        title="Категорії витрат"
        actions={
          canWrite && (
            <Button
              variant="contained"
              startIcon={<Icon name="plus" />}
              onClick={() => selection.select('new')}
            >
              Додати категорію
            </Button>
          )
        }
      />
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
            sx={{ minWidth: 150 }}
          >
            <MenuItem value="">Усі</MenuItem>
            <MenuItem value="true">Активні</MenuItem>
            <MenuItem value="false">Неактивні</MenuItem>
          </TextField>
        </FilterBar>
      </Paper>
      <Paper variant="outlined">
        {query.isPending ? (
          <ListLoading label="Завантаження категорій витрат" />
        ) : query.isError ? (
          <RequestErrorState
            error={query.error}
            retry={() => {
              void query.refetch()
            }}
          />
        ) : !rows.length ? (
          <EmptyState
            title="Категорій витрат ще немає"
            description="Створіть категорію або змініть фільтри."
          />
        ) : (
          <DirectoryEntries
            label="Дерево категорій витрат"
            rows={rows}
            actions={canWrite ? actions : undefined}
            info={(category) =>
              query.data?.find((row) => row.id === category.parentId)?.name || 'Коренева категорія'
            }
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
        detailLabel="Редактор категорії витрат"
        detail={
          editor ?? (
            <Paper variant="outlined">
              <EmptyState icon="folder" title="Оберіть категорію" />
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
        Відкинути незбережені зміни та відкрити іншу категорію?
      </ConfirmDialog>
    </>
  )
}
