<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { auth, logout } from '@/composables/auth'

const router = useRouter()
const appError = ref('')

/** 登出失敗時不清除現有畫面，讓使用者可重試且不誤以為 Cookie 已失效。 */
async function handleLogout(): Promise<void> {
  appError.value = ''
  try {
    await logout()
    await router.push({ name: 'login' })
  } catch (error) {
    appError.value = error instanceof Error ? error.message : '登出失敗，請稍後再試。'
  }
}
</script>

<template>
  <header class="site-header">
    <RouterLink class="wordmark" to="/orders/buying" aria-label="吃土小幫手首頁">
      <i class="fa-solid fa-receipt" aria-hidden="true"></i>
      <span>吃土小幫手</span>
    </RouterLink>
    <nav v-if="auth.user" aria-label="主要導覽">
      <RouterLink to="/orders/buying">我要代購</RouterLink>
      <RouterLink to="/orders/requested">我的委託</RouterLink>
      <RouterLink to="/orders/archived">已封存</RouterLink>
      <RouterLink to="/blocks">封鎖名單</RouterLink>
    </nav>
    <div v-if="auth.user" class="account">
      <span>{{ auth.user.displayName }}</span>
      <button class="text-button" type="button" @click="handleLogout">登出</button>
    </div>
  </header>

  <main id="main-content" class="page-shell" tabindex="-1">
    <p v-if="appError" class="notice error" role="alert">{{ appError }}</p>
    <RouterView />
  </main>

  <footer class="site-footer">
    <RouterLink to="/privacy">隱私政策</RouterLink>
    <RouterLink to="/tos">服務條款</RouterLink>
    <RouterLink to="/invite">邀請 Bot</RouterLink>
  </footer>
</template>
