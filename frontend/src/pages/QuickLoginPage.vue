<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { exchangeQuickLoginToken } from '@/api/client'
import { auth, refreshAuth } from '@/composables/auth'

const router = useRouter()
const message = ref('正在確認快速登入連結。')

onMounted(async () => {
  const token = new URLSearchParams(window.location.hash.slice(1)).get('token')
  window.history.replaceState(null, '', window.location.pathname)

  if (!token) {
    message.value = '快速登入連結無效或已過期。'
    return
  }

  try {
    await exchangeQuickLoginToken(token)
    await refreshAuth()
    if (auth.user) await router.replace({ name: 'buying' })
    else message.value = auth.error || '快速登入未完成。'
  } catch (error) {
    message.value = error instanceof Error ? error.message : '快速登入未完成。'
  }
})
</script>

<template>
  <section class="narrow-page" aria-live="polite">
    <h1>快速登入</h1>
    <p>{{ message }}</p>
    <RouterLink v-if="!auth.user" class="secondary-button" to="/login">返回登入頁</RouterLink>
  </section>
</template>
