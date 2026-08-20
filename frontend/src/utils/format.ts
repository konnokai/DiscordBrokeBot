/** 格式化 API 已計算的整數金額，不進行任何訂單或財務運算。 */
export function formatTwd(amount: string): string {
  try {
    return `NT$ ${new Intl.NumberFormat('zh-TW').format(BigInt(amount))}`
  } catch {
    return `NT$ ${amount}`
  }
}

export function formatDate(value: string | null): string {
  if (!value) return '未設定'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}
