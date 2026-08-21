<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const isPrivacy = computed(() => route.name === 'privacy')
const title = computed(() => String(route.meta.title))
</script>

<template>
  <article class="legal-page">
    <h1>{{ title }}</h1>
    <template v-if="isPrivacy">
      <h2>蒐集資料</h2>
      <p>
        本服務會透過 Discord OAuth 取得登入所需的 Discord
        使用者識別碼和顯示資料，並儲存訂單、款項與封鎖關係。
      </p>
      <h2>使用目的</h2>
      <p>資料只用來顯示、管理和通知代購訂單。前端不會儲存 Discord access token。</p>
      <h2>資料儲存與聯絡方式</h2>
      <p>訂單資料會儲存在本服務使用的資料庫中。如有資料相關問題，請聯絡服務管理者。</p>
    </template>
    <template v-else>
      <h2>服務範圍</h2>
      <p>本服務協助 Discord 使用者記錄代購訂單及款項，不提供付款、履約或爭議仲裁。</p>
      <h2>使用者責任</h2>
      <p>使用者應自行確認訂單內容、收付款項與交易對象。不得利用本服務騷擾他人或進行違法交易。</p>
      <h2>服務調整</h2>
      <p>服務可能因維護或安全需求調整功能。款項紀錄刪除後不保留稽核資料。</p>
    </template>
  </article>
</template>
