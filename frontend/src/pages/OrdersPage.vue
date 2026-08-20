<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ApiError, apiFetch } from '@/api/client'
import { formatDate, formatTwd } from '@/utils/format'
import type { OrdersResponse } from '@/types/api'

const route = useRoute()
const data = ref<OrdersResponse | null>(null)
const loading = ref(false)
const error = ref('')

const view = computed(() => {
  if (route.name === 'requested')
    return { role: 'requester', archived: false, title: '我的委託', balance: '應付' }
  if (route.name === 'archived')
    return { role: 'buyer', archived: true, title: '已封存', balance: '差額' }
  return { role: 'buyer', archived: false, title: '我要代購', balance: '應收' }
})

/** 只讀取後端統計值；此頁不依訂單列自行加總任何金額。 */
async function loadOrders(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    data.value = await apiFetch<OrdersResponse>(
      `/api/orders?role=${view.value.role}&archived=${view.value.archived}`,
    )
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '無法載入訂單。'
    if (reason instanceof ApiError && reason.status === 401) data.value = null
  } finally {
    loading.value = false
  }
}

watch(() => route.name, loadOrders, { immediate: true })
</script>

<template>
  <section aria-labelledby="orders-title">
    <div class="page-heading">
      <div>
        <p class="eyebrow">訂單總表</p>
        <h1 id="orders-title">{{ view.title }}</h1>
      </div>
      <button class="secondary-button" type="button" :disabled="loading" @click="loadOrders">
        <i class="fa-solid fa-rotate-right" aria-hidden="true"></i>
        重新整理
      </button>
    </div>

    <div v-if="data" class="summary receipt-divider" aria-label="後端計算的訂單摘要">
      <dl>
        <div>
          <dt>全部總額</dt>
          <dd class="money">{{ formatTwd(data.summary.allOrderTotal) }}</dd>
        </div>
        <div>
          <dt>未購買</dt>
          <dd class="money">{{ formatTwd(data.summary.unpurchasedOrderTotal) }}</dd>
        </div>
        <div>
          <dt>已購買</dt>
          <dd class="money">{{ formatTwd(data.summary.purchasedOrderTotal) }}</dd>
        </div>
        <div>
          <dt>已收款</dt>
          <dd class="money">{{ formatTwd(data.summary.receivedTotal) }}</dd>
        </div>
        <div>
          <dt>{{ view.balance }}</dt>
          <dd class="money">{{ formatTwd(data.summary.balanceTotal) }}</dd>
        </div>
      </dl>
    </div>

    <p v-if="loading" class="notice" aria-live="polite">正在載入訂單。</p>
    <p v-if="error" class="notice error" role="alert">{{ error }}</p>
    <p v-if="data && !loading && data.orders.length === 0" class="empty-state">目前沒有訂單。</p>

    <ol v-if="data" class="order-list" aria-label="訂單清單">
      <li v-for="order in data.orders" :key="order.id">
        <RouterLink class="order-row" :to="`/orders/${order.id}`">
          <div class="order-title">
            <strong>{{ order.itemName }}</strong>
            <span>{{
              view.role === 'buyer' ? order.requesterDisplayName : order.buyerDisplayName
            }}</span>
            <small
              >{{ order.sourceGuildName
              }}<template v-if="order.stall"> - {{ order.stall }}</template></small
            >
          </div>
          <dl class="order-values">
            <div>
              <dt>總額</dt>
              <dd class="money">{{ formatTwd(order.orderTotal) }}</dd>
            </div>
            <div>
              <dt>已收款</dt>
              <dd class="money">{{ formatTwd(order.receivedTotal) }}</dd>
            </div>
            <div>
              <dt>差額</dt>
              <dd class="money">{{ formatTwd(order.balance) }}</dd>
            </div>
            <div>
              <dt>狀態</dt>
              <dd>
                {{ order.isPurchased ? '已購買' : '待購買' }} /
                {{ order.isSettlementComplete ? '已完成' : '未完成' }}
              </dd>
            </div>
          </dl>
          <time :datetime="order.updatedAt">更新 {{ formatDate(order.updatedAt) }}</time>
        </RouterLink>
      </li>
    </ol>
  </section>
</template>
