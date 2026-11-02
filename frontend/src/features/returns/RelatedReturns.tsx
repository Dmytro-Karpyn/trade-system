import { Button, Paper, Stack, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { hasAnyPermission } from '../../auth/contracts'
import { useCurrentUser } from '../../auth/queries'
import { ListLoading } from '../../components/ListLoading'
import { RequestErrorState } from '../../components/RequestState'
import type { ReturnKind } from './contracts'
import { returnConfig } from './contracts'
import { ReturnList } from './ReturnListPage'
import { useReturns } from './queries'

export function RelatedReturns({ kind, sourceId }: { kind: ReturnKind; sourceId: string }) {
  const user = useCurrentUser().data; const config = returnConfig[kind]; const canRead = Boolean(user && hasAnyPermission(user, [`${config.permission}.view`])); const canCreate = Boolean(user && hasAnyPermission(user, [`${config.permission}.create`]))
  const query = useReturns(kind, { sourceId }, canRead)
  if (!canRead && !canCreate) return null
  return <Stack spacing={2}><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' } }}><Typography variant="h2">{kind === 'customer' ? 'Повернення' : 'Повернення постачальнику'}</Typography>{canCreate && <Button component={Link} variant="outlined" to={`${config.root}/new?${config.sourceKey}=${sourceId}`}>+ {config.create}</Button>}</Stack>{canRead && <Paper variant="outlined">{query.isPending ? <ListLoading label="Завантаження пов’язаних повернень" /> : query.isError ? <RequestErrorState error={query.error} retry={() => { void query.refetch() }} /> : <ReturnList kind={kind} rows={query.data} />}</Paper>}</Stack>
}
