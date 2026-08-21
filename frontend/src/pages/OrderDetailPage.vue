<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { apiFetch, apiMutation } from '@/api/client'
import { showToast } from '@/composables/toast'
import { formatDate, formatTwd } from '@/utils/format'
import type {
  OrderDetailResponse,
  PaymentEntry,
  PaymentPayload,
  PurchaseStatusPayload,
  UpdateOrderPayload,
} from '@/types/api'

interface OrderDraft {
  itemName: string
  unitPrice: string
  quantity: number
  note: string
  stall: string
}

const route = useRoute()
const router = useRouter()
const detail = ref<OrderDetailResponse | null>(null)
const loading = ref(false)
const saving = ref(false)
const editingPaymentId = ref<string | null>(null)
const draft = reactive<OrderDraft>({
  itemName: '',
  unitPrice: '',
  quantity: 1,
  note: '',
  stall: '',
})
const paymentDraft = reactive<PaymentPayload>({ amount: '', reason: '' })
const paymentReasonOptions = ['商品款', '運費', '退款', '折抵', '其他']
const orderId = computed(() => String(route.params.id))
const editing = computed(() => route.name === 'order-edit')
const order = computed(() => detail.value?.order ?? null)

function copyOrderToDraft(response: OrderDetailResponse): void {
  draft.itemName = response.order.itemName
  draft.unitPrice = response.order.unitPrice
  draft.quantity = response.order.quantity
  draft.note = response.order.note
  draft.stall = response.order.stall ?? ''
}

/** API 載入失敗不清除既有明細或草稿，讓使用者在暫時斷線後仍可保留內容。 */
async function loadOrder(): Promise<boolean> {
  loading.value = true
  try {
    const response = await apiFetch<OrderDetailResponse>(
      `/api/orders/${encodeURIComponent(orderId.value)}`,
    )
    detail.value = response
    copyOrderToDraft(response)
    return true
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '無法載入訂單。', 'error')
    return false
  } finally {
    loading.value = false
  }
}

function validateOrderDraft(): string | null {
  if (!draft.itemName.trim() || !draft.note.trim()) return '物品名稱與備註為必填。'
  if (!/^[1-9]\d*$/.test(draft.unitPrice)) return '單價必須是大於零的新台幣整數。'
  if (!Number.isSafeInteger(draft.quantity) || draft.quantity < 1) return '數量必須是大於零的整數。'
  return null
}

async function saveOrder(): Promise<void> {
  const validationError = validateOrderDraft()
  if (validationError) {
    showToast(validationError, 'error')
    return
  }
  saving.value = true
  try {
    const payload: UpdateOrderPayload = {
      itemName: draft.itemName.trim(),
      unitPrice: draft.unitPrice,
      quantity: draft.quantity,
      note: draft.note.trim(),
      stall: draft.stall.trim() || null,
    }
    await apiMutation<void>(`/api/orders/${encodeURIComponent(orderId.value)}`, {
      method: 'PATCH',
      body: JSON.stringify(payload),
    })
    if (!(await loadOrder())) return
    showToast('訂單內容已儲存。')
    await router.replace({ name: 'order-detail', params: { id: orderId.value } })
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '儲存訂單失敗。', 'error')
  } finally {
    saving.value = false
  }
}

async function updatePurchaseStatus(): Promise<void> {
  if (!order.value) return
  const payload: PurchaseStatusPayload = { isPurchased: !order.value.isPurchased }
  await updateBuyerAction('/purchase-status', payload, '購買狀態已更新。')
}

async function toggleSettlementStatus(): Promise<void> {
  if (!order.value) return
  const settlementMode = order.value.isSettlementComplete ? 'force_pending' : 'force_completed'
  await updateBuyerAction(
    '/settlement-mode',
    { settlementMode },
    order.value.isSettlementComplete ? '已標記為未完成收款。' : '已標記為完成收款。',
  )
}

/** 代購方專屬操作均在 API 成功後再重新載入，避免本機推測財務或狀態結果。 */
async function updateBuyerAction(
  path: string,
  body: object,
  successMessage: string,
): Promise<void> {
  saving.value = true
  try {
    await apiMutation<void>(`/api/orders/${encodeURIComponent(orderId.value)}${path}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    })
    if (!(await loadOrder())) return
    showToast(successMessage)
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '更新失敗。', 'error')
  } finally {
    saving.value = false
  }
}

async function archiveOrRestore(): Promise<void> {
  if (!order.value) return
  const restoring = Boolean(order.value.archivedAt)
  saving.value = true
  try {
    await apiMutation<void>(
      `/api/orders/${encodeURIComponent(orderId.value)}/${restoring ? 'restore' : 'archive'}`,
      {
        method: 'POST',
      },
    )
    showToast(restoring ? '訂單已復原。' : '訂單已封存。')
    await router.push({ name: restoring ? 'buying' : 'archived' })
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '訂單狀態更新失敗。', 'error')
  } finally {
    saving.value = false
  }
}

function beginPaymentEdit(payment: PaymentEntry): void {
  editingPaymentId.value = payment.id
  paymentDraft.amount = payment.amount
  paymentDraft.reason = payment.reason
}

function cancelPaymentEdit(): void {
  editingPaymentId.value = null
  paymentDraft.amount = ''
  paymentDraft.reason = ''
}

function validatePayment(): string | null {
  if (!/^-?[1-9]\d*$/.test(paymentDraft.amount)) return '金額必須是非零的新台幣整數，可為負數。'
  if (!paymentDraft.reason.trim()) return '請填寫款項事由。'
  return null
}

async function savePayment(): Promise<void> {
  const validationError = validatePayment()
  if (validationError) {
    showToast(validationError, 'error')
    return
  }
  saving.value = true
  try {
    const body = JSON.stringify({ amount: paymentDraft.amount, reason: paymentDraft.reason.trim() })
    if (editingPaymentId.value) {
      await apiMutation<void>(
        `/api/payment-entries/${encodeURIComponent(editingPaymentId.value)}`,
        {
          method: 'PATCH',
          body,
        },
      )
    } else {
      await apiMutation<void>(`/api/orders/${encodeURIComponent(orderId.value)}/payment-entries`, {
        method: 'POST',
        body,
      })
    }
    cancelPaymentEdit()
    if (!(await loadOrder())) return
    showToast('款項紀錄已儲存。')
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '款項紀錄儲存失敗。', 'error')
  } finally {
    saving.value = false
  }
}

async function deletePayment(payment: PaymentEntry): Promise<void> {
  if (!window.confirm(`確定永久刪除「${payment.reason}」這筆款項紀錄？`)) return
  saving.value = true
  try {
    await apiMutation<void>(`/api/payment-entries/${encodeURIComponent(payment.id)}`, {
      method: 'DELETE',
    })
    if (!(await loadOrder())) return
    showToast('款項紀錄已永久刪除。')
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '款項紀錄刪除失敗。', 'error')
  } finally {
    saving.value = false
  }
}

async function blockRequester(): Promise<void> {
  if (!order.value) return
  if (
    !window.confirm(`確定封鎖「${order.value.requesterDisplayName}」嗎？封鎖後將無法建立新的訂單。`)
  )
    return

  saving.value = true
  try {
    await apiMutation<void>(
      `/api/blocks/${encodeURIComponent(order.value.requesterDiscordUserId)}`,
      {
        method: 'POST',
      },
    )
    showToast(`已封鎖 ${order.value.requesterDisplayName} 的未來訂單請求。`)
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '封鎖失敗。', 'error')
  } finally {
    saving.value = false
  }
}

watch(orderId, loadOrder, { immediate: true })
</script>

<template>
  <section aria-labelledby="order-title">
    <p v-if="loading" class="notice" aria-live="polite">正在載入訂單。</p>

    <template v-if="order">
      <div class="page-heading">
        <div>
          <p class="eyebrow">訂單 #{{ order.id }}</p>
          <h1 id="order-title">{{ editing ? '編輯訂單' : order.itemName }}</h1>
        </div>
        <RouterLink
          v-if="!editing && order.permissions.canEdit && !order.archivedAt"
          class="secondary-button"
          :to="`/orders/${order.id}/edit`"
        >
          <i class="fa-solid fa-pen" aria-hidden="true"></i>
          編輯一般欄位
        </RouterLink>
      </div>

      <form v-if="editing" class="form-grid" @submit.prevent="saveOrder">
        <label>物品名稱<input v-model="draft.itemName" required /></label>
        <label
          >單價<input
            v-model="draft.unitPrice"
            inputmode="numeric"
            pattern="[1-9][0-9]*"
            title="請輸入大於零的整數"
            required
        /></label>
        <label
          >數量<input v-model.number="draft.quantity" type="number" min="1" step="1" required
        /></label>
        <label>攤位<input v-model="draft.stall" /></label>
        <label class="full-width"
          >備註<textarea v-model="draft.note" rows="4" required></textarea>
        </label>
        <div class="form-actions full-width">
          <button class="primary-button" type="submit" :disabled="saving">儲存訂單</button>
          <RouterLink class="secondary-button" :to="`/orders/${order.id}`">取消</RouterLink>
        </div>
      </form>

      <template v-else>
        <dl class="detail-list">
          <div>
            <dt>請求方</dt>
            <dd>{{ order.requesterDisplayName }}</dd>
          </div>
          <div>
            <dt>代購方</dt>
            <dd>{{ order.buyerDisplayName }}</dd>
          </div>
          <div>
            <dt>來源伺服器</dt>
            <dd>{{ order.sourceGuildName }}</dd>
          </div>
          <div>
            <dt>攤位</dt>
            <dd>{{ order.stall || '未填寫' }}</dd>
          </div>
          <div>
            <dt>單價</dt>
            <dd class="money">{{ formatTwd(order.unitPrice) }}</dd>
          </div>
          <div>
            <dt>數量</dt>
            <dd>{{ order.quantity }}</dd>
          </div>
          <div>
            <dt>訂單總額</dt>
            <dd class="money">{{ formatTwd(order.orderTotal) }}</dd>
          </div>
          <div>
            <dt>已付金額</dt>
            <dd class="money">{{ formatTwd(order.receivedTotal) }}</dd>
          </div>
          <div>
            <dt>未付金額</dt>
            <dd class="money">{{ formatTwd(order.balance) }}</dd>
          </div>
          <div>
            <dt>購買狀態</dt>
            <dd>{{ order.isPurchased ? '已購買' : '尚未購買' }}</dd>
          </div>
          <div>
            <dt>收款狀態</dt>
            <dd>{{ order.isSettlementComplete ? '已完成' : '未完成' }}</dd>
          </div>
          <div>
            <dt>建立時間</dt>
            <dd>{{ formatDate(order.createdAt) }}</dd>
          </div>
          <div>
            <dt>更新時間</dt>
            <dd>{{ formatDate(order.updatedAt) }}</dd>
          </div>
          <div class="full-width">
            <dt>備註</dt>
            <dd class="preserve-line">{{ order.note }}</dd>
          </div>
        </dl>

        <div
          v-if="!order.archivedAt && order.permissions.canManageBuyerActions"
          class="action-section"
        >
          <h2>代購方操作</h2>
          <div class="button-row">
            <button
              class="secondary-button"
              type="button"
              :disabled="saving"
              @click="updatePurchaseStatus"
            >
              {{ order.isPurchased ? '標記為未購買' : '標記為已購買' }}
            </button>
            <button
              class="secondary-button"
              type="button"
              :disabled="saving"
              @click="toggleSettlementStatus"
            >
              {{ order.isSettlementComplete ? '標記為未完成收款' : '標記為已完成收款' }}
            </button>
            <button class="danger-button" type="button" :disabled="saving" @click="blockRequester">
              封鎖此請求方
            </button>
          </div>
        </div>

        <div
          v-if="!order.archivedAt && order.permissions.canManageBuyerActions"
          class="action-section"
        >
          <h2>款項紀錄</h2>
          <form class="form-grid payment-form" @submit.prevent="savePayment">
            <label
              >金額<input
                v-model="paymentDraft.amount"
                inputmode="numeric"
                pattern="-?[1-9][0-9]*"
                title="請輸入非零整數，可為負數"
                placeholder="可輸入負數"
                required
            /></label>
            <label
              >事由<input
                v-model="paymentDraft.reason"
                list="payment-reasons"
                placeholder="選擇或輸入事由"
                required
            /></label>
            <datalist id="payment-reasons">
              <option v-for="reason in paymentReasonOptions" :key="reason" :value="reason" />
            </datalist>
            <div class="form-actions">
              <button class="primary-button" type="submit" :disabled="saving">
                {{ editingPaymentId ? '儲存修改' : '新增款項' }}
              </button>
              <button
                v-if="editingPaymentId"
                class="text-button"
                type="button"
                @click="cancelPaymentEdit"
              >
                取消編輯
              </button>
            </div>
          </form>
          <ul class="plain-list" aria-label="款項紀錄">
            <li v-for="payment in detail?.paymentEntries ?? []" :key="payment.id">
              <div>
                <strong class="money">{{ formatTwd(payment.amount) }}</strong
                ><small>{{ payment.reason }} - {{ formatDate(payment.updatedAt) }}</small>
              </div>
              <div class="button-row">
                <button
                  class="text-button"
                  type="button"
                  :disabled="saving"
                  @click="beginPaymentEdit(payment)"
                >
                  編輯</button
                ><button
                  class="danger-button"
                  type="button"
                  :disabled="saving"
                  @click="deletePayment(payment)"
                >
                  刪除
                </button>
              </div>
            </li>
          </ul>
        </div>

        <div class="action-section">
          <h2>訂單操作紀錄</h2>
          <ol v-if="detail?.activities.length" class="activity-list" aria-label="訂單操作紀錄">
            <li v-for="activity in detail.activities" :key="activity.id">
              <div class="activity-meta">
                <strong>{{ activity.actionType }}</strong>
                <small>
                  {{ activity.actorDisplayName }}（{{ activity.actorDiscordUserId }}） ·
                  {{ formatDate(activity.createdAt) }}
                </small>
              </div>
              <p>{{ activity.detail }}</p>
            </li>
          </ol>
          <p v-else>尚無操作紀錄。</p>
        </div>

        <div class="action-section">
          <h2>訂單狀態</h2>
          <p v-if="order.archivedAt">
            此訂單已於
            {{ formatDate(order.archivedAt) }} 封存。封存時不可編輯、管理款項或變更購買狀態。
          </p>
          <button
            v-if="order.permissions.canArchive || order.permissions.canRestore"
            class="danger-button"
            type="button"
            :disabled="saving"
            @click="archiveOrRestore"
          >
            {{ order.archivedAt ? '復原訂單' : '封存訂單' }}
          </button>
        </div>
      </template>
    </template>
  </section>
</template>
