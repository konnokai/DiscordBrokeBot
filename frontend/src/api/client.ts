/** 可顯示給使用者的 API 錯誤，保留 HTTP 狀態以利頁面判斷登入狀態。 */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, '')

function apiUrl(path: string): string {
  if (!configuredBaseUrl) {
    throw new ApiError('尚未設定服務連線網址，目前無法連線。')
  }
  return `${configuredBaseUrl}${path}`
}

async function readError(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (
      typeof body === 'object' &&
      body !== null &&
      'message' in body &&
      typeof body.message === 'string'
    ) {
      return body.message
    }
  } catch {
    // 非 JSON 的 proxy 或伺服器錯誤仍需提供可理解訊息。
  }
  return `請求失敗（${response.status}）。`
}

/**
 * 集中 Cookie 與 CSRF 行為，避免個別頁面漏帶 credentials 或安全 header。
 * API 錯誤會拋出，不會用本機資料偽造成功結果。
 */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response: Response
  try {
    response = await fetch(apiUrl(path), {
      ...init,
      credentials: 'include',
      headers: { Accept: 'application/json', ...init.headers },
    })
  } catch {
    throw new ApiError('目前無法連線到服務，請確認服務已啟動，或稍後再試。')
  }

  if (!response.ok) {
    throw new ApiError(await readError(response), response.status)
  }

  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

/** 所有非 GET 請求先取得一次性 CSRF token，再用同一個 Cookie session 寫入資料。 */
export async function apiMutation<T>(path: string, init: RequestInit): Promise<T> {
  const csrf = await apiFetch<{ token: string }>('/api/auth/csrf')
  return apiFetch<T>(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-Token': csrf.token,
      ...init.headers,
    },
  })
}

export async function exchangeQuickLoginToken(token: string): Promise<void> {
  let response: Response
  try {
    response = await fetch(apiUrl('/auth/quick-login'), {
      method: 'POST',
      credentials: 'include',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
    })
  } catch {
    throw new ApiError('目前無法連線到服務，請確認服務已啟動，或稍後再試。')
  }

  if (!response.ok) throw new ApiError(await readError(response), response.status)
}

export function loginUrl(): string | null {
  return configuredBaseUrl ? `${configuredBaseUrl}/auth/login` : null
}
