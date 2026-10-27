import { useState } from 'react'
import { zodResolver } from '@hookform/resolvers/zod'
import { Alert, Box, Button, IconButton, InputAdornment, Stack, TextField, Typography } from '@mui/material'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { loginSchema } from '../auth/contracts'
import type { LoginInput } from '../auth/contracts'
import { useLogin } from '../auth/queries'
import { Brand } from '../components/Brand'
import { Icon } from '../components/Icon'
import { tokens } from '../theme/tokens'

export function LoginPage() {
  const navigate = useNavigate()
  const login = useLogin()
  const [showPassword, setShowPassword] = useState(false)
  const { register, handleSubmit, setError, formState: { errors } } = useForm<LoginInput>({
    resolver: zodResolver(loginSchema), defaultValues: { username: '', password: '' },
  })
  const submit = handleSubmit(async input => {
    try {
      await login.mutateAsync(input)
      navigate('/', { replace: true })
    } catch (error) {
      if (error instanceof ApiError) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          const key = field.toLowerCase()
          if (key === 'username' || key === 'password') setError(key, { message: messages.join(' ') })
        }
      }
    }
  })
  const errorMessage = login.error instanceof ApiError && login.error.status === 401
    ? 'Не вдалося увійти. Перевірте логін і пароль. Якщо проблема повторюється, зверніться до адміністратора.'
    : login.error?.message
  return (
    <Box sx={{ minHeight: '100dvh', display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(380px, 0.95fr) 1.05fr' }, bgcolor: '#fff' }}>
      <Stack sx={{ display: { xs: 'none', md: 'flex' }, bgcolor: tokens.colors.sidebar, color: '#fff', p: { md: 5, xl: 7 }, position: 'relative', overflow: 'hidden', justifyContent: 'space-between', minHeight: '100dvh', '&::after': { content: '""', pointerEvents: 'none', position: 'absolute', inset: 0, background: 'radial-gradient(ellipse at 0% 85%, #224c8d55, transparent 60%)' } }}>
        <Brand light />
        <Box sx={{ position: 'relative', zIndex: 1, maxWidth: 490, py: 6 }}>
          <Typography variant="overline" sx={{ color: '#89adf4' }}>ВАШ РОБОЧИЙ ПРОСТІР</Typography>
          <Typography component="p" sx={{ mt: 2, mb: 3, fontSize: { md: 42, xl: 52 }, fontWeight: 650, letterSpacing: '-0.045em', lineHeight: 1.12 }}>Єдиний простір<br />для вашого бізнесу.</Typography>
          <Typography sx={{ color: '#aebed5', maxWidth: 350, fontSize: 14, lineHeight: 1.8 }}>Керуйте торговими та складськими процесами в одному робочому просторі.</Typography>
          <Box aria-hidden="true" sx={{ mt: 5, display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 1.5 }}>
            {([{ icon: 'box', label: 'Товари' }, { icon: 'stock', label: 'Склад' }, { icon: 'chart', label: 'Результат' }] as const).map(item => <Box key={item.label} sx={{ border: '1px solid #34496b', bgcolor: '#1b2d4966', borderRadius: 1.5, p: 2 }}><Icon name={item.icon} sx={{ color: '#8baded', fontSize: 25, mb: 1.5 }} /><Typography sx={{ color: '#c7d4e9', fontSize: 12 }}>{item.label}</Typography></Box>)}
          </Box>
        </Box>
        <Typography variant="caption" sx={{ color: '#7185a5', position: 'relative', zIndex: 1 }}>Торгово-складська система</Typography>
      </Stack>
      <Stack sx={{ alignItems: 'center', justifyContent: 'center', p: { xs: 3, sm: 5 }, minHeight: '100dvh' }}>
        <Box sx={{ width: '100%', maxWidth: 380 }}>
          <Box sx={{ display: { xs: 'block', md: 'none' }, mb: 7 }}><Brand /></Box>
          <Typography variant="overline" color="primary">ВХІД ДО СИСТЕМИ</Typography>
          <Typography variant="h1" component="h1" sx={{ mt: 1, mb: 1.5 }}>З поверненням</Typography>
          <Typography color="text.secondary" sx={{ mb: 4, fontSize: 13 }}>Введіть облікові дані для входу в робочий простір.</Typography>
          <Box component="form" noValidate onSubmit={event => { void submit(event) }}>
            <Stack spacing={2.5}>
              {login.isError && <Alert severity="error">{errorMessage}</Alert>}
              <TextField label="Логін" autoComplete="username" fullWidth autoFocus disabled={login.isPending} {...register('username')} error={Boolean(errors.username)} helperText={errors.username?.message} slotProps={{ inputLabel: { shrink: true }, input: { sx: { height: 46 } } }} />
              <TextField label="Пароль" type={showPassword ? 'text' : 'password'} autoComplete="current-password" fullWidth disabled={login.isPending} {...register('password')} error={Boolean(errors.password)} helperText={errors.password?.message}
                slotProps={{ inputLabel: { shrink: true }, input: { sx: { height: 46 }, endAdornment: <InputAdornment position="end"><IconButton aria-label={showPassword ? 'Приховати пароль' : 'Показати пароль'} onClick={() => setShowPassword(value => !value)} onMouseDown={event => event.preventDefault()} edge="end"><Icon name={showPassword ? 'eyeOff' : 'eye'} sx={{ fontSize: 20 }} /></IconButton></InputAdornment> } }} />
              <Button type="submit" variant="contained" fullWidth disabled={login.isPending} endIcon={login.isPending ? undefined : <Icon name="arrow" />} sx={{ height: 46 }}>{login.isPending ? 'Входимо…' : 'Увійти'}</Button>
            </Stack>
          </Box>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', textAlign: 'center', mt: 3.5 }}>Немає доступу? Зверніться до адміністратора.</Typography>
        </Box>
      </Stack>
    </Box>
  )
}
