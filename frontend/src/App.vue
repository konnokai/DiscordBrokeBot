<script setup lang="ts">
import { useRouter } from 'vue-router'
import { auth, logout } from '@/composables/auth'
import { showToast } from '@/composables/toast'
import ToastHost from '@/components/ToastHost.vue'

const router = useRouter()

/** 登出失敗時不清除現有畫面，讓使用者可重試且不誤以為 Cookie 已失效。 */
async function handleLogout(): Promise<void> {
  try {
    await logout()
    await router.push({ name: 'login' })
  } catch (error) {
    showToast(error instanceof Error ? error.message : '登出失敗，請稍後再試。', 'error')
  }
}
</script>

<template>
  <header class="site-header">
    <RouterLink class="wordmark" to="/orders/buying" aria-label="吃土小幫手首頁">
      <img class="brand-icon" src="/icon.png" alt="" width="40" height="40" />
      <span>吃土小幫手</span>
    </RouterLink>
    <nav v-if="auth.user" aria-label="主要導覽">
      <RouterLink to="/orders/buying">我代購的</RouterLink>
      <RouterLink to="/orders/requested">我委託的</RouterLink>
      <RouterLink to="/orders/archived">已封存</RouterLink>
      <RouterLink to="/blocks">封鎖名單</RouterLink>
    </nav>
    <div v-if="auth.user" class="account">
      <span>{{ auth.user.displayName }}</span>
      <button class="text-button" type="button" @click="handleLogout">登出</button>
    </div>
  </header>

  <main id="main-content" class="page-shell" tabindex="-1">
    <RouterView />
  </main>

  <ToastHost />

  <footer class="site-footer">
    <RouterLink to="/privacy">隱私政策</RouterLink>
    <RouterLink to="/tos">服務條款</RouterLink>
    <RouterLink to="/invite">邀請 Bot</RouterLink>
    <a
      class="attribution"
      href="https://www.flaticon.com/free-icons/spending"
      title="spending icons"
      >Spending icons created by BZZRINCANTATION - Flaticon</a
    >
  </footer>
</template>
