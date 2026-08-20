<script setup lang="ts">
const inviteUrl = import.meta.env.VITE_DISCORD_INVITE_URL as string | undefined
</script>

<template>
  <section class="narrow-page">
    <p class="eyebrow">公開安裝</p>
    <h1>邀請吃土小幫手到伺服器</h1>
    <p>Bot 僅要求安裝與 application commands 所需權限。訂單資料不依目前伺服器成員資格授權。</p>
    <a v-if="inviteUrl" class="primary-button" :href="inviteUrl">
      <i class="fa-brands fa-discord" aria-hidden="true"></i>
      前往 Discord 安裝
    </a>
    <p v-else class="notice error" role="alert">
      尚未設定 VITE_DISCORD_INVITE_URL，暫時無法提供安裝連結。
    </p>
  </section>
</template>
