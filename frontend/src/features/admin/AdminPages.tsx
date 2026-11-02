import { useEffect, useState } from 'react'
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Checkbox,
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
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useCurrentUser } from '../../auth/queries'
import { hasAnyPermission } from '../../auth/contracts'
import { CatalogSections } from '../../components/CatalogSections'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { DataTable } from '../../components/DataTable'
import { DocumentActions } from '../../components/DocumentActions'
import { FilterBar } from '../../components/FilterBar'
import { Icon } from '../../components/Icon'
import { PageHeader } from '../../components/PageHeader'
import { SplitViewLayout } from '../../components/SplitViewLayout'
import { useDetailSelection } from '../../components/useDetailSelection'
import { ActiveBadge } from '../../components/ActiveBadge'
import { EmptyState } from '../../components/EmptyState'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import { managementError } from '../directories/contracts'
import { useManagementNotice } from '../directories/notices'
import {
  permissionGroup,
  permissionGroups,
  permissionLabel,
  roleFormSchema,
  timestamp,
  userFormSchema,
} from './contracts'
import type { ManagedRole, ManagedUser, RoleValues, UserValues } from './contracts'
import {
  useManagedRole,
  useManagedRoles,
  useManagedUser,
  useManagedUsers,
  usePermissionDefinitions,
  useRoleMutations,
  useUserLookups,
  useUserMutations,
} from './queries'

export function AdminIndexPage() {
  const user = useCurrentUser().data
  const sections = [
    ['users', 'Користувачі', 'Облікові записи, ролі й активність.', 'users.manage'],
    ['roles', 'Ролі та права', 'Набори прав для користувачів організації.', 'roles.manage'],
    ['audit', 'Журнал аудиту', 'Історія змін і подій.', 'audit.view'],
  ]
  return (
    <Stack spacing={2}>
      <PageHeader
        title="Адміністрування"
        description="Користувачі, права доступу та історія змін."
      />
      <CatalogSections
        sections={sections
          .filter((section) => user && hasAnyPermission(user, [section[3]]))
          .map(([path, title, description]) => ({
            path: '/admin/' + path,
            title,
            description,
            icon: path === 'users' ? 'people' : path === 'roles' ? 'shield' : 'calendar',
          }))}
      />
      {user?.permissions.includes('settings.manage') && (
        <Button component={Link} to="/directories" sx={{ alignSelf: 'flex-start' }}>
          Налаштування довідників
        </Button>
      )}
    </Stack>
  )
}

function RoleBadges({ user }: { user: ManagedUser }) {
  return (
    <Stack direction="row" spacing={0.75} sx={{ flexWrap: 'wrap', rowGap: 0.75 }}>
      {user.roleNames.length ? (
        user.roleNames.map((name) => (
          <Chip
            key={name}
            size="small"
            variant="outlined"
            label={name}
            title={name}
            sx={{ maxWidth: '100%' }}
          />
        ))
      ) : (
        <Typography variant="body2" color="text.secondary">
          Без ролей
        </Typography>
      )}
    </Stack>
  )
}
export function UsersPage() {
  const query = useManagedUsers(),
    mutations = useUserMutations(),
    notify = useManagementNotice(),
    mobile = useMediaQuery(useTheme().breakpoints.down('md')),
    desktop = useMediaQuery(useTheme().breakpoints.up('lg')),
    selection = useDetailSelection()
  const [search, setSearch] = useState('')
  const [target, setTarget] = useState<ManagedUser | null>(null)
  const [error, setError] = useState<unknown>(null)
  const activate = async () => {
    if (!target) return
    try {
      await mutations.active.mutateAsync({ id: target.userId, active: !target.active })
      notify({ severity: 'success', message: 'Активність користувача змінено.' })
      setTarget(null)
    } catch (e) {
      setError(e)
      notify({ severity: 'error', message: managementError(e) })
    }
  }
  const rows = (query.data ?? []).filter((user) =>
    `${user.username} ${user.fullName}`
      .toLocaleLowerCase('uk')
      .includes(search.trim().toLocaleLowerCase('uk')),
  )
  const open = (event: React.MouseEvent, id: string) => {
    if (desktop && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
      event.preventDefault()
      selection.select(id)
    }
  }
  const actions = (user: ManagedUser) => (
    <Stack direction="row" spacing={0.5}>
      <Button
        component={Link}
        to={'/admin/users/' + user.userId}
        onClick={(event) => open(event, user.userId)}
        size="small"
      >
        Редагувати
      </Button>
      <Button
        size="small"
        color={user.active ? 'error' : 'primary'}
        onClick={() => {
          setError(null)
          setTarget(user)
        }}
        aria-label={`${user.active ? 'Деактивувати' : 'Активувати'} ${user.username}`}
      >
        {user.active ? 'Деактивувати' : 'Активувати'}
      </Button>
    </Stack>
  )
  const content = (
    <Stack spacing={2}>
      <PageHeader
        title="Користувачі"
        actions={
          <Button component={Link} to="/admin/users/new" variant="contained">
            Додати користувача
          </Button>
        }
      />
      <Paper variant="outlined">
        <FilterBar mobileSheet>
          <TextField
            label="Пошук користувача"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            fullWidth
          />
        </FilterBar>
      </Paper>
      <Paper variant="outlined">
        {query.isPending ? (
          <ListLoading label="Завантаження користувачів" />
        ) : query.isError ? (
          <RequestErrorState
            error={query.error}
            retry={() => {
              void query.refetch()
            }}
          />
        ) : !rows.length ? (
          <EmptyState title="Користувачів ще немає" />
        ) : mobile ? (
          <Stack component="ul" aria-label="Користувачі" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {rows.map((user) => (
              <Stack
                component="li"
                key={user.userId}
                spacing={1}
                sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider' }}
              >
                <Typography variant="subtitle1">{user.fullName || user.username}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {user.username} · {timestamp(user.lastLoginAt)}
                </Typography>
                <RoleBadges user={user} />
                <Box>
                  <ActiveBadge active={user.active} />
                </Box>
                {actions(user)}
              </Stack>
            ))}
          </Stack>
        ) : (
          <DataTable
            label="Користувачі"
            compact={desktop}
            rows={rows}
            rowId={(user) => user.userId}
            selectedId={selection.id}
            select={desktop ? (user) => selection.select(user.userId) : undefined}
            columns={[
              {
                key: 'name',
                label: 'Ім’я / логін',
                render: (user) => (
                  <Box>
                    <Typography variant="body2" noWrap title={user.fullName}>
                      {user.fullName || '—'}
                    </Typography>
                    <Button
                      component={Link}
                      to={'/admin/users/' + user.userId}
                      onClick={(event) => open(event, user.userId)}
                      size="small"
                      sx={{
                        p: 0,
                        minWidth: 0,
                        maxWidth: '100%',
                        display: 'block',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                        textAlign: 'left',
                      }}
                    >
                      {user.username}
                    </Button>
                  </Box>
                ),
              },
              {
                key: 'roles',
                label: 'Ролі',
                width: desktop ? 105 : undefined,
                render: (user) => <RoleBadges user={user} />,
              },
              {
                key: 'active',
                label: 'Активний',
                width: desktop ? 95 : undefined,
                render: (user) => <ActiveBadge active={user.active} />,
              },
              {
                key: 'login',
                label: 'Останній вхід',
                width: desktop ? 85 : undefined,
                render: (user) => (
                  <Typography variant="caption">{timestamp(user.lastLoginAt)}</Typography>
                ),
              },
              {
                key: 'actions',
                label: 'Дії',
                width: desktop ? 42 : undefined,
                render: (user) =>
                  desktop ? (
                    <IconButton
                      size="small"
                      color={user.active ? 'error' : 'primary'}
                      title={user.active ? 'Деактивувати' : 'Активувати'}
                      aria-label={`${user.active ? 'Деактивувати' : 'Активувати'} ${user.username}`}
                      onClick={() => {
                        setError(null)
                        setTarget(user)
                      }}
                    >
                      <Icon name={user.active ? 'eyeOff' : 'refresh'} fontSize="small" />
                    </IconButton>
                  ) : (
                    actions(user)
                  ),
              },
            ]}
          />
        )}
      </Paper>
    </Stack>
  )
  return (
    <>
      <SplitViewLayout
        split={desktop}
        list={content}
        detailLabel="Вибраний користувач"
        detail={
          selection.id ? (
            <UserDetail
              key={selection.id}
              id={selection.id}
              embedded
              onDirtyChange={selection.dirtyChanged}
            />
          ) : (
            <Paper variant="outlined">
              <EmptyState icon="people" title="Оберіть користувача" />
            </Paper>
          )
        }
      />
      <ConfirmDialog
        open={Boolean(target)}
        title={`${target?.active ? 'Деактивувати' : 'Активувати'} користувача?`}
        action={target?.active ? 'Деактивувати' : 'Активувати'}
        busy={mutations.active.isPending}
        danger={Boolean(target?.active)}
        close={() => setTarget(null)}
        confirm={() => {
          void activate()
        }}
      >
        {target?.username}.{' '}
        {target?.active
          ? 'Поточні сеанси користувача будуть відкликані.'
          : 'Користувач знову зможе входити до системи.'}
        {error != null && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {managementError(error)}
          </Alert>
        )}
      </ConfirmDialog>

      <ConfirmDialog
        open={selection.pending !== null}
        title="Незбережені зміни"
        action="Відкинути зміни"
        danger
        close={selection.cancel}
        confirm={selection.discard}
      >
        Відкинути незбережені зміни та відкрити іншого користувача?
      </ConfirmDialog>
    </>
  )
}
function UserForm({
  user,
  embedded = false,
  onDirtyChange,
}: {
  user?: ManagedUser
  embedded?: boolean
  onDirtyChange?: (dirty: boolean) => void
}) {
  const me = useCurrentUser().data
  const lookups = useUserLookups()
  const mutations = useUserMutations()
  const navigate = useNavigate()
  const notify = useManagementNotice()
  const [error, setErrorState] = useState<unknown>(null)
  const schema = userFormSchema.superRefine((v, ctx) => {
    if (user) return
    if (
      v.password.length < 12 ||
      !/[a-z]/.test(v.password) ||
      !/[A-Z]/.test(v.password) ||
      !/[0-9]/.test(v.password) ||
      !/[\W_]/.test(v.password)
    )
      ctx.addIssue({
        code: 'custom',
        path: ['password'],
        message:
          'Щонайменше 12 символів: велика й мала латинські літери, цифра та спеціальний символ.',
      })
    if (v.password !== v.confirmPassword)
      ctx.addIssue({ code: 'custom', path: ['confirmPassword'], message: 'Паролі не збігаються.' })
  })
  const {
    register,
    control,
    handleSubmit,
    setError,
    reset,
    formState: { errors, isDirty },
  } = useForm<UserValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      username: user?.username ?? '',
      fullName: user?.fullName ?? '',
      counterpartyId: user?.counterpartyId ?? '',
      roleIds: user?.roleIds ?? [],
      active: user?.active ?? true,
      password: '',
      confirmPassword: '',
    },
  })
  useEffect(() => onDirtyChange?.(isDirty), [isDirty, onDirtyChange])
  const submit = handleSubmit(async (values) => {
    try {
      const saved = await mutations.save.mutateAsync({ id: user?.userId, values })
      notify({ severity: 'success', message: 'Користувача збережено.' })
      reset({ ...values, password: '', confirmPassword: '' })
      if (!embedded) void navigate('/admin/users/' + saved.userId, { replace: !user })
    } catch (e) {
      setErrorState(e)
      notify({ severity: 'error', message: managementError(e) })
      if (e instanceof Error && /password/i.test('detail' in e ? String(e.detail) : e.message))
        setError('password', { message: 'Пароль не відповідає вимогам сервера.' })
    }
  })
  return (
    <Paper
      component="form"
      noValidate
      variant="outlined"
      onSubmit={(e) => {
        void submit(e)
      }}
      sx={{ p: 2 }}
    >
      <Stack spacing={2}>
        {error != null && <Alert severity="error">{managementError(error)}</Alert>}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}>
          <TextField
            label="Логін"
            required
            {...register('username')}
            disabled={mutations.save.isPending}
            error={Boolean(errors.username)}
            helperText={errors.username?.message}
          />
          <TextField
            label="Повне ім’я"
            {...register('fullName')}
            disabled={mutations.save.isPending}
          />
          <TextField
            label="Організація"
            value={me?.organizationId ?? ''}
            slotProps={{ input: { readOnly: true } }}
            helperText="Поточна організація облікового запису."
          />
          <Controller
            name="counterpartyId"
            control={control}
            render={({ field }) => (
              <TextField
                {...field}
                select
                label="Контрагент / працівник"
                disabled={mutations.save.isPending || lookups.isPending}
              >
                <MenuItem value="">Не задано</MenuItem>
                {field.value && !lookups.data?.counterparties.some((c) => c.id === field.value) && (
                  <MenuItem value={field.value} disabled>
                    Поточний контрагент
                  </MenuItem>
                )}
                {lookups.data?.counterparties.map((c) => (
                  <MenuItem key={c.id} value={c.id}>
                    {c.name}
                    {c.isEmployee ? ' (працівник)' : ''}
                  </MenuItem>
                ))}
              </TextField>
            )}
          />
          {!user && (
            <>
              <TextField
                label="Пароль"
                type="password"
                required
                autoComplete="new-password"
                {...register('password')}
                error={Boolean(errors.password)}
                helperText={
                  errors.password?.message ??
                  'Мінімум 12 символів, велика й мала літери, цифра та спеціальний символ.'
                }
              />
              <TextField
                label="Підтвердження пароля"
                type="password"
                required
                autoComplete="new-password"
                {...register('confirmPassword')}
                error={Boolean(errors.confirmPassword)}
                helperText={errors.confirmPassword?.message}
              />
            </>
          )}
        </Box>
        <Box>
          <Typography variant="h2" sx={{ mb: 1 }}>
            Ролі
          </Typography>
          <Controller
            name="roleIds"
            control={control}
            render={({ field }) => (
              <Stack spacing={0.5}>
                {lookups.data?.roles.map((role) => (
                  <FormControlLabel
                    key={role.id}
                    label={role.name + (role.active ? '' : ' (неактивна)')}
                    control={
                      <Checkbox
                        checked={field.value.includes(role.id)}
                        disabled={
                          mutations.save.isPending ||
                          (!role.active && !field.value.includes(role.id))
                        }
                        onChange={(e) =>
                          field.onChange(
                            e.target.checked
                              ? [...field.value, role.id]
                              : field.value.filter((id) => id !== role.id),
                          )
                        }
                      />
                    }
                  />
                ))}
              </Stack>
            )}
          />
        </Box>
        <Controller
          name="active"
          control={control}
          render={({ field }) => (
            <FormControlLabel
              label="Активний користувач"
              control={
                <Switch
                  checked={field.value}
                  onChange={field.onChange}
                  disabled={mutations.save.isPending}
                />
              }
            />
          )}
        />
        {lookups.isError && (
          <RequestErrorState
            error={lookups.error}
            retry={() => {
              void lookups.refetch()
            }}
          />
        )}
        {user?.userId === me?.userId && (
          <Alert severity="info">
            Зміна власного облікового запису завершує його поточні сеанси. Після збереження увійдіть
            знову.
          </Alert>
        )}
        <DocumentActions>
          <Button component={Link} to="/admin/users">
            До списку
          </Button>
          <Button
            variant="contained"
            type="submit"
            disabled={mutations.save.isPending || lookups.isPending || lookups.isError}
          >
            Зберегти користувача
          </Button>
        </DocumentActions>
      </Stack>
    </Paper>
  )
}
export function UserPage({ newUser = false }: { newUser?: boolean }) {
  const { id = '' } = useParams()
  return <UserDetail id={newUser ? '' : id} newUser={newUser} />
}
function UserDetail({
  id,
  newUser = false,
  embedded = false,
  onDirtyChange,
}: {
  id: string
  newUser?: boolean
  embedded?: boolean
  onDirtyChange?: (dirty: boolean) => void
}) {
  const query = useManagedUser(newUser ? '' : id)
  if (!newUser && query.isPending) return <ListLoading label="Завантаження користувача" />
  if (!newUser && query.isError)
    return (
      <RequestErrorState
        error={query.error}
        retry={() => {
          void query.refetch()
        }}
      />
    )
  return (
    <Stack spacing={2}>
      {embedded && (
        <Button
          component={Link}
          to={'/admin/users/' + id}
          sx={{ alignSelf: 'flex-start' }}
          startIcon={<Icon name="upRight" />}
        >
          Відкрити окремо
        </Button>
      )}
      <PageHeader
        title={
          newUser
            ? 'Новий користувач'
            : query.data?.fullName || query.data?.username || 'Користувач'
        }
        secondary={embedded}
        description={query.data && <ActiveBadge active={query.data.active} />}
      />
      <UserForm
        key={newUser ? 'new' : id}
        user={newUser ? undefined : query.data}
        embedded={embedded}
        onDirtyChange={onDirtyChange}
      />
    </Stack>
  )
}
export function RolesPage() {
  const query = useManagedRoles(),
    mobile = useMediaQuery(useTheme().breakpoints.down('md')),
    desktop = useMediaQuery(useTheme().breakpoints.up('lg')),
    selection = useDetailSelection()
  const [search, setSearch] = useState('')
  const rows = (query.data ?? []).filter((role) =>
    role.name.toLocaleLowerCase('uk').includes(search.trim().toLocaleLowerCase('uk')),
  )
  const open = (event: React.MouseEvent, id: string) => {
    if (desktop && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
      event.preventDefault()
      selection.select(id)
    }
  }
  const content = (
    <Stack spacing={2}>
      <PageHeader
        title="Ролі та права"
        actions={
          <Button component={Link} to="/admin/roles/new" variant="contained">
            Додати роль
          </Button>
        }
      />
      <Paper variant="outlined">
        <FilterBar mobileSheet>
          <TextField
            label="Пошук ролі"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            fullWidth
          />
        </FilterBar>
      </Paper>
      <Paper variant="outlined">
        {query.isPending ? (
          <ListLoading label="Завантаження ролей" />
        ) : query.isError ? (
          <RequestErrorState
            error={query.error}
            retry={() => {
              void query.refetch()
            }}
          />
        ) : !rows.length ? (
          <EmptyState title="Ролей ще немає" />
        ) : mobile ? (
          <Stack component="ul" aria-label="Ролі" sx={{ listStyle: 'none', p: 0, m: 0 }}>
            {rows.map((role) => (
              <Stack
                component="li"
                key={role.id}
                spacing={1}
                sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider' }}
              >
                <Typography variant="subtitle1">{role.name}</Typography>
                <Typography variant="body2">
                  Користувачів: {role.userCount} · Прав: {role.permissions.length}
                </Typography>
                <Box>
                  <ActiveBadge active={role.active} />
                </Box>
                <Button
                  component={Link}
                  to={'/admin/roles/' + role.id}
                  sx={{ alignSelf: 'flex-start' }}
                >
                  Редагувати
                </Button>
              </Stack>
            ))}
          </Stack>
        ) : (
          <DataTable
            label="Ролі"
            compact
            rows={rows}
            rowId={(role) => role.id}
            selectedId={selection.id}
            select={desktop ? (role) => selection.select(role.id) : undefined}
            columns={[
              {
                key: 'name',
                label: 'Назва',
                render: (role) => (
                  <Button
                    component={Link}
                    to={'/admin/roles/' + role.id}
                    onClick={(event) => open(event, role.id)}
                    size="small"
                    title={role.name}
                    sx={{
                      p: 0,
                      maxWidth: '100%',
                      display: 'block',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      textAlign: 'left',
                    }}
                  >
                    {role.name}
                  </Button>
                ),
              },
              {
                key: 'active',
                label: 'Активна',
                width: 105,
                render: (role) => <ActiveBadge active={role.active} />,
              },
              { key: 'users', label: 'Користувачів', width: 95, render: (role) => role.userCount },
              {
                key: 'permissions',
                label: 'Прав',
                width: 55,
                render: (role) => role.permissions.length,
              },
              {
                key: 'open',
                label: 'Дії',
                width: 42,
                render: (role) => (
                  <IconButton
                    component={Link}
                    to={'/admin/roles/' + role.id}
                    onClick={(event) => open(event, role.id)}
                    size="small"
                    title="Редагувати"
                    aria-label={'Редагувати ' + role.name}
                  >
                    <Icon name="edit" fontSize="small" />
                  </IconButton>
                ),
              },
            ]}
          />
        )}
      </Paper>
    </Stack>
  )
  return (
    <>
      <SplitViewLayout
        split={desktop}
        list={content}
        detailLabel="Вибрана роль"
        detail={
          selection.id ? (
            <RoleDetail
              key={selection.id}
              id={selection.id}
              embedded
              onDirtyChange={selection.dirtyChanged}
            />
          ) : (
            <Paper variant="outlined">
              <EmptyState icon="shield" title="Оберіть роль" />
            </Paper>
          )
        }
      />
      <ConfirmDialog
        open={selection.pending !== null}
        title="Незбережені зміни"
        action="Відкинути зміни"
        danger
        close={selection.cancel}
        confirm={selection.discard}
      >
        Відкинути незбережені зміни та відкрити іншу роль?
      </ConfirmDialog>
    </>
  )
}
function RoleForm({
  role,
  onDirtyChange,
}: {
  role?: ManagedRole
  onDirtyChange?: (dirty: boolean) => void
}) {
  const [baseline, setBaseline] = useState(role)
  const [selected, setSelected] = useState(role?.permissions ?? [])
  const [error, setErrorState] = useState<unknown>(null)
  const definitions = usePermissionDefinitions()
  const mutations = useRoleMutations()
  const notify = useManagementNotice()
  const navigate = useNavigate()
  const {
    register,
    control,
    handleSubmit,
    reset,
    formState: { errors, isDirty },
  } = useForm<RoleValues>({
    resolver: zodResolver(roleFormSchema),
    defaultValues: { name: role?.name ?? '', active: role?.active ?? true },
  })
  const permissionDirty =
    JSON.stringify([...selected].sort()) !==
    JSON.stringify([...(baseline?.permissions ?? [])].sort())
  useEffect(
    () => onDirtyChange?.(isDirty || permissionDirty),
    [isDirty, permissionDirty, onDirtyChange],
  )
  const active = useWatch({ control, name: 'active' })
  const busy = mutations.save.isPending || mutations.permissions.isPending
  const submit = handleSubmit(async (values) => {
    let saved = baseline
    try {
      if (!saved || isDirty) {
        saved = await mutations.save.mutateAsync({ id: saved?.id, values })
        setBaseline(saved)
        reset({ name: saved.name, active: saved.active })
        if (!saved.active) setSelected(saved.permissions)
      }
      if (
        saved.active &&
        JSON.stringify([...selected].sort()) !== JSON.stringify([...saved.permissions].sort())
      ) {
        saved = await mutations.permissions.mutateAsync({ id: saved.id, codes: selected })
        setBaseline(saved)
      }
      notify({ severity: 'success', message: 'Роль і права збережено.' })
      if (!role) void navigate('/admin/roles/' + saved.id, { replace: true })
    } catch (e) {
      setErrorState(e)
      notify({ severity: 'error', message: managementError(e) })
    }
  })
  const groups = permissionGroups.map(([key, label]) => ({
    key,
    label,
    permissions: definitions.data?.filter((p) => permissionGroup(p.code) === key) ?? [],
  }))
  const options = (group: (typeof groups)[number]) => (
    <Stack>
      {group.permissions.map((p) => (
        <FormControlLabel
          key={p.code}
          label={
            <Box>
              <Typography variant="body2">{permissionLabel(p.code)}</Typography>
              <Typography variant="caption" color="text.secondary">
                {p.code}
              </Typography>
            </Box>
          }
          control={
            <Checkbox
              checked={selected.includes(p.code)}
              disabled={busy || !active || baseline?.active === false}
              onChange={(e) =>
                setSelected(
                  e.target.checked
                    ? [...selected, p.code]
                    : selected.filter((code) => code !== p.code),
                )
              }
            />
          }
        />
      ))}
    </Stack>
  )
  return (
    <Stack
      component="form"
      noValidate
      spacing={2}
      onSubmit={(e) => {
        void submit(e)
      }}
    >
      {error != null && <Alert severity="error">{managementError(error)}</Alert>}
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack spacing={2}>
          <TextField
            label="Назва ролі"
            required
            {...register('name')}
            disabled={busy}
            error={Boolean(errors.name)}
            helperText={errors.name?.message}
          />
          <Controller
            name="active"
            control={control}
            render={({ field }) => (
              <FormControlLabel
                label="Активна роль"
                control={<Switch checked={field.value} onChange={field.onChange} disabled={busy} />}
              />
            )}
          />
        </Stack>
      </Paper>
      {baseline?.active === false && (
        <Alert severity="info">
          Неактивна роль не надає прав. Щоб змінити її права, спочатку активуйте та збережіть роль.
        </Alert>
      )}
      {definitions.isPending ? (
        <ListLoading label="Завантаження прав" />
      ) : definitions.isError ? (
        <RequestErrorState
          error={definitions.error}
          retry={() => {
            void definitions.refetch()
          }}
        />
      ) : (
        <Box>
          {groups.map((group) => (
            <Accordion
              key={group.key}
              defaultExpanded={group.key === 'product'}
              disableGutters
              elevation={0}
              sx={{ border: '1px solid', borderColor: 'divider', '&:before': { display: 'none' } }}
            >
              <AccordionSummary aria-label={group.label} expandIcon={<Icon name="down" />}>
                <Typography component="span" variant="subtitle1">
                  {group.label}
                </Typography>
              </AccordionSummary>
              <AccordionDetails sx={{ pt: 0 }}>{options(group)}</AccordionDetails>
            </Accordion>
          ))}
        </Box>
      )}
      <DocumentActions>
        <Button component={Link} to="/admin/roles">
          До списку
        </Button>
        <Button
          variant="contained"
          type="submit"
          disabled={busy || definitions.isPending || definitions.isError}
        >
          Зберегти роль
        </Button>
      </DocumentActions>
    </Stack>
  )
}
export function RolePage({ newRole = false }: { newRole?: boolean }) {
  const { id = '' } = useParams()
  return <RoleDetail id={newRole ? '' : id} newRole={newRole} />
}
function RoleDetail({
  id,
  newRole = false,
  embedded = false,
  onDirtyChange,
}: {
  id: string
  newRole?: boolean
  embedded?: boolean
  onDirtyChange?: (dirty: boolean) => void
}) {
  const query = useManagedRole(newRole ? '' : id)
  if (!newRole && query.isPending) return <ListLoading label="Завантаження ролі" />
  if (!newRole && query.isError)
    return (
      <RequestErrorState
        error={query.error}
        retry={() => {
          void query.refetch()
        }}
      />
    )
  return (
    <Stack spacing={2}>
      {embedded && (
        <Button
          component={Link}
          to={'/admin/roles/' + id}
          startIcon={<Icon name="upRight" />}
          sx={{ alignSelf: 'flex-start' }}
        >
          Відкрити окремо
        </Button>
      )}
      <PageHeader
        title={newRole ? 'Нова роль' : query.data?.name || 'Роль'}
        secondary={embedded}
        description={query.data && <ActiveBadge active={query.data.active} />}
      />
      <RoleForm
        key={newRole ? 'new' : id}
        role={newRole ? undefined : query.data}
        onDirtyChange={onDirtyChange}
      />
    </Stack>
  )
}
