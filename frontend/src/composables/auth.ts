import { reactive } from 'vue'
import { ApiError, apiFetch, apiMutation } from '@/api/client'
import type { AuthUser } from '@/types/api'

export const auth = reactive({
  user: null as AuthUser | null,
  loading: false,
  error: '' as string,
})

/** 讀取 Cookie session；401 是未登入，其餘錯誤要明確顯示，不能假定匿名成功。 */
export async function refreshAuth(): Promise<void> {
  auth.loading = true
  auth.error = ''
  try {
    auth.user = await apiFetch<AuthUser>('/api/auth/me')
  } catch (error) {
    auth.user = null
    if (!(error instanceof ApiError && error.status === 401)) {
      auth.error = error instanceof Error ? error.message : '無法確認登入狀態。'
    }
  } finally {
    auth.loading = false
  }
}

export async function logout(): Promise<void> {
  await apiMutation<void>('/auth/logout', { method: 'POST' })
  auth.user = null
}
