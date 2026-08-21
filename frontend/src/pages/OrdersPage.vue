<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ApiError, apiFetch } from '@/api/client'
import { showToast } from '@/composables/toast'
import { formatDate, formatTwd } from '@/utils/format'
import type { OrdersResponse } from '@/types/api'

const route = useRoute()
const data = ref<OrdersResponse | null>(null)
const loading = ref(false)

const view = computed(() => {
  if (route.name === 'requested')
    return {
      role: 'requester',
      archived: false,
      title: '我委託的訂單',
      balance: '應付',
      counterpartyLabel: '代購方',
    }
  if (route.name === 'archived')
    return {
      role: 'buyer',
      archived: true,
      title: '已封存',
      balance: '未付金額',
      counterpartyLabel: '委託方',
    }
  return {
    role: 'buyer',
    archived: false,
    title: '我代購的訂單',
    balance: '應收',
    counterpartyLabel: '委託方',
  }
})

const selectedCounterparty = ref('')

const counterpartyOptions = computed(() => {
  const options = new Map<string, string>()
  for (const order of data.value?.orders ?? []) {
    const id = view.value.role === 'buyer' ? order.requesterDiscordUserId : order.buyerDiscordUserId
    const name = view.value.role === 'buyer' ? order.requesterDisplayName : order.buyerDisplayName
    options.set(id, name)
  }
  return [...options.entries()]
    .map(([id, name]) => ({ id, name }))
    .sort((left, right) => left.name.localeCompare(right.name, 'zh-Hant'))
})

const filteredOrders = computed(() => {
  const orders = data.value?.orders ?? []
  if (!selectedCounterparty.value) return orders

  return orders.filter((order) => {
    const id = view.value.role === 'buyer' ? order.requesterDiscordUserId : order.buyerDiscordUserId
    return id === selectedCounterparty.value
  })
})

/** 只讀取後端統計值；此頁不依訂單列自行加總任何金額。 */
async function loadOrders(): Promise<void> {
  loading.value = true
  try {
    data.value = await apiFetch<OrdersResponse>(
      `/api/orders?role=${view.value.role}&archived=${view.value.archived}`,
    )
  } catch (reason) {
    showToast(reason instanceof Error ? reason.message : '無法載入訂單。', 'error')
    if (reason instanceof ApiError && reason.status === 401) data.value = null
  } finally {
    loading.value = false
  }
}

watch(
  () => route.name,
  () => {
    selectedCounterparty.value = ''
    void loadOrders()
  },
  { immediate: true },
)
</script>

<template>
  <section aria-labelledby="orders-title">
    <div class="page-heading">
      <div>
        <p class="eyebrow">訂單總表</p>
        <h1 id="orders-title">{{ view.title }}</h1>
      </div>
      <div class="page-actions">
        <label v-if="data && !view.archived" class="order-filter">
          <span>篩選{{ view.counterpartyLabel }}</span>
          <select id="counterparty-filter" v-model="selectedCounterparty">
            <option value="">全部{{ view.counterpartyLabel }}</option>
            <option v-for="option in counterpartyOptions" :key="option.id" :value="option.id">
              {{ option.name }}
            </option>
          </select>
        </label>
        <button class="secondary-button" type="button" :disabled="loading" @click="loadOrders">
          <i class="fa-solid fa-rotate-right" aria-hidden="true"></i>
          重新整理
        </button>
      </div>
    </div>

    <div v-if="data" class="summary receipt-divider" aria-label="訂單摘要">
      <dl>
        <div>
          <dt>全部訂單總額</dt>
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
    <p v-if="data && !loading && filteredOrders.length === 0" class="empty-state">
      {{ data.orders.length === 0 ? '目前沒有訂單。' : '沒有符合篩選條件的訂單。' }}
    </p>

    <ol v-if="data && filteredOrders.length > 0" class="order-list" aria-label="訂單清單">
      <li v-for="order in filteredOrders" :key="order.id">
        <RouterLink class="order-row" :to="`/orders/${order.id}`">
          <div class="order-title">
            <strong>{{ order.itemName }}</strong>
            <span>{{
              view.role === 'buyer' ? order.requesterDisplayName : order.buyerDisplayName
            }}</span>
            <small>{{ order.sourceGuildName }}</small>
            <span v-if="order.stall" class="order-stall">攤位：{{ order.stall }}</span>
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
              <dt>未付金額</dt>
              <dd class="money">{{ formatTwd(order.balance) }}</dd>
            </div>
            <div>
              <dt>狀態</dt>
              <dd>
                {{ order.isPurchased ? '已購買' : '尚未購買' }} /
                {{ order.isSettlementComplete ? '已完成' : '未完成' }}
              </dd>
            </div>
          </dl>
          <time :datetime="order.updatedAt">更新時間：{{ formatDate(order.updatedAt) }}</time>
        </RouterLink>
      </li>
    </ol>
  </section>
</template>
