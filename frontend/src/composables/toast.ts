import { reactive } from 'vue'

export type ToastKind = 'success' | 'error' | 'info'

export const toast = reactive({
  message: '',
  kind: 'info' as ToastKind,
})

let timer: ReturnType<typeof setTimeout> | undefined

export function showToast(message: string, kind: ToastKind = 'success'): void {
  if (timer) clearTimeout(timer)
  toast.message = message
  toast.kind = kind
  timer = setTimeout(() => {
    toast.message = ''
    timer = undefined
  }, 5000)
}

export function hideToast(): void {
  if (timer) clearTimeout(timer)
  timer = undefined
  toast.message = ''
}
