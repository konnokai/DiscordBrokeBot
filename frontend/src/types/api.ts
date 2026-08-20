/** API 的識別碼和金額刻意保留字串，避免 JavaScript 整數精度造成資料失真。 */
export type Id = string
export type Amount = string
export type SettlementMode = 'auto' | 'force_completed' | 'force_pending'

export interface AuthUser {
  discordUserId: Id
  displayName: string
  avatarUrl?: string | null
}

export interface OrderPermissions {
  canEdit: boolean
  canManageBuyerActions: boolean
  canArchive: boolean
  canRestore: boolean
}

export interface OrderFinancials {
  orderTotal: Amount
  receivedTotal: Amount
  balance: Amount
  isSettlementComplete: boolean
}

export interface Order extends OrderFinancials {
  id: Id
  requesterDiscordUserId: Id
  requesterDisplayName: string
  buyerDiscordUserId: Id
  buyerDisplayName: string
  sourceGuildId: Id
  sourceGuildName: string
  itemName: string
  unitPrice: Amount
  quantity: number
  note: string
  stall: string | null
  isPurchased: boolean
  purchasedAt: string | null
  settlementMode: SettlementMode
  createdAt: string
  updatedAt: string
  archivedAt: string | null
  permissions: OrderPermissions
}

export interface OrderSummary {
  allOrderTotal: Amount
  unpurchasedOrderTotal: Amount
  purchasedOrderTotal: Amount
  receivedTotal: Amount
  balanceTotal: Amount
}

export interface OrdersResponse {
  orders: Order[]
  summary: OrderSummary
}

export interface PaymentEntry {
  id: Id
  orderId: Id
  amount: Amount
  reason: string
  createdAt: string
  updatedAt: string
}

export interface OrderDetailResponse {
  order: Order
  paymentEntries: PaymentEntry[]
}

export interface UpdateOrderPayload {
  itemName: string
  unitPrice: Amount
  quantity: number
  note: string
  stall: string | null
}

export interface PaymentPayload {
  amount: Amount
  reason: string
}

export interface PurchaseStatusPayload {
  isPurchased: boolean
}

export interface SettlementModePayload {
  settlementMode: SettlementMode
}

export interface UserBlock {
  requesterDiscordUserId: Id
  requesterDisplayName: string
  createdAt: string
}
