<script setup lang="ts">
const inviteUrl = import.meta.env.VITE_DISCORD_INVITE_URL as string | undefined
</script>

<template>
  <section class="narrow-page">
    <p class="eyebrow">公開安裝</p>
    <h1>邀請吃土小幫手加入伺服器</h1>
    <p>邀請後即可建立訂單，並使用私訊通知功能。</p>
    <a v-if="inviteUrl" class="primary-button" :href="inviteUrl">
      <i class="fa-brands fa-discord" aria-hidden="true"></i>
      前往 Discord 安裝
    </a>
    <p v-else class="notice error" role="alert">目前尚未設定 Discord 安裝連結，暫時無法使用。</p>
  </section>
</template>
