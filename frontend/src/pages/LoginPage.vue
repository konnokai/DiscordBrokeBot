<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { auth } from '@/composables/auth'
import { loginUrl } from '@/api/client'

const route = useRoute()
const oauthUrl = computed(() => loginUrl())

/** API 產生 state 與 PKCE；前端只導向登入入口，不持有 Discord token。 */
function startLogin(): void {
  if (oauthUrl.value) window.location.assign(oauthUrl.value)
}
</script>

<template>
  <section class="narrow-page">
    <p class="eyebrow">Discord 代購訂單帳本</p>
    <h1>登入後管理你的訂單</h1>
    <p>使用 Discord 帳號登入。本站不建立密碼，也不在瀏覽器儲存 Discord access token。</p>
    <p v-if="route.query.redirect" class="notice">請先登入後繼續操作。</p>
    <p v-if="auth.error" class="notice error" role="alert">{{ auth.error }}</p>
    <p v-if="!oauthUrl" class="notice error" role="alert">
      尚未設定 VITE_API_BASE_URL，無法開始 Discord 登入。
    </p>
    <button class="primary-button" type="button" :disabled="!oauthUrl" @click="startLogin">
      <i class="fa-brands fa-discord" aria-hidden="true"></i>
      使用 Discord 登入
    </button>
  </section>
</template>
