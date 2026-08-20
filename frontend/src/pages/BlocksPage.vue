<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { apiFetch, apiMutation } from '@/api/client'
import { formatDate } from '@/utils/format'
import type { UserBlock } from '@/types/api'

const blocks = ref<UserBlock[]>([])
const requesterId = ref('')
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const notice = ref('')

/** 封鎖清單只接受 API 成功回應更新，失敗時保留輸入的 Discord UID。 */
async function loadBlocks(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    blocks.value = await apiFetch<UserBlock[]>('/api/blocks')
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '無法載入封鎖名單。'
  } finally {
    loading.value = false
  }
}

async function addBlock(): Promise<void> {
  if (!requesterId.value.trim()) {
    error.value = '請輸入要封鎖的請求方 Discord UID。'
    return
  }
  saving.value = true
  error.value = ''
  notice.value = ''
  try {
    await apiMutation<void>(`/api/blocks/${encodeURIComponent(requesterId.value.trim())}`, {
      method: 'POST',
    })
    requesterId.value = ''
    notice.value = '已更新封鎖名單。'
    await loadBlocks()
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '封鎖失敗。'
  } finally {
    saving.value = false
  }
}

async function removeBlock(block: UserBlock): Promise<void> {
  if (!window.confirm(`確定解除封鎖「${block.requesterDisplayName}」？`)) return
  saving.value = true
  error.value = ''
  try {
    await apiMutation<void>(`/api/blocks/${encodeURIComponent(block.requesterDiscordUserId)}`, {
      method: 'DELETE',
    })
    notice.value = '已解除封鎖。'
    await loadBlocks()
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : '解除封鎖失敗。'
  } finally {
    saving.value = false
  }
}

onMounted(loadBlocks)
</script>

<template>
  <section aria-labelledby="blocks-title">
    <div class="page-heading">
      <div>
        <p class="eyebrow">代購方設定</p>
        <h1 id="blocks-title">封鎖名單</h1>
      </div>
      <button class="secondary-button" type="button" :disabled="loading" @click="loadBlocks">
        <i class="fa-solid fa-rotate-right" aria-hidden="true"></i>
        重新整理
      </button>
    </div>
    <p>封鎖只會拒絕對方建立未來訂單，不會改變既有訂單或款項。</p>
    <form class="inline-form" @submit.prevent="addBlock">
      <label for="requester-id">請求方 Discord UID</label>
      <input
        id="requester-id"
        v-model="requesterId"
        inputmode="numeric"
        autocomplete="off"
        required
      />
      <button class="primary-button" type="submit" :disabled="saving">封鎖</button>
    </form>
    <p v-if="notice" class="notice success" role="status">{{ notice }}</p>
    <p v-if="error" class="notice error" role="alert">{{ error }}</p>
    <p v-if="loading" class="notice" aria-live="polite">正在載入封鎖名單。</p>
    <ul class="plain-list" aria-label="已封鎖請求方">
      <li v-for="block in blocks" :key="block.requesterDiscordUserId">
        <div>
          <strong>{{ block.requesterDisplayName }}</strong>
          <small>UID: {{ block.requesterDiscordUserId }} - {{ formatDate(block.createdAt) }}</small>
        </div>
        <button class="danger-button" type="button" :disabled="saving" @click="removeBlock(block)">
          解除封鎖
        </button>
      </li>
    </ul>
  </section>
</template>
