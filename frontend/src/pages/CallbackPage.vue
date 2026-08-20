<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { auth, refreshAuth } from '@/composables/auth'

const router = useRouter()
const message = ref('正在確認 Discord 登入狀態。')

/** OAuth callback 已由 API 寫入 Cookie，這頁只驗證 session 後導向受保護頁面。 */
onMounted(async () => {
  await refreshAuth()
  if (auth.user) {
    await router.replace({ name: 'buying' })
  } else {
    message.value = auth.error || '登入未完成。請返回登入頁重新嘗試。'
  }
})
</script>

<template>
  <section class="narrow-page" aria-live="polite">
    <h1>登入處理中</h1>
    <p>{{ message }}</p>
    <RouterLink v-if="!auth.user && !auth.loading" class="secondary-button" to="/login">
      返回登入頁
    </RouterLink>
  </section>
</template>
