<script setup lang="ts">
import { hideToast, toast } from '@/composables/toast'
</script>

<template>
  <Transition name="toast">
    <div
      v-if="toast.message"
      class="toast"
      :class="`toast-${toast.kind}`"
      :role="toast.kind === 'error' ? 'alert' : 'status'"
      :aria-live="toast.kind === 'error' ? 'assertive' : 'polite'"
    >
      <i
        class="toast-icon fa-solid"
        :class="{
          'fa-circle-check': toast.kind === 'success',
          'fa-circle-exclamation': toast.kind === 'error',
          'fa-circle-info': toast.kind === 'info',
        }"
        aria-hidden="true"
      ></i>
      <span>{{ toast.message }}</span>
      <button class="toast-close" type="button" aria-label="關閉通知" @click="hideToast">
        <i class="fa-solid fa-xmark" aria-hidden="true"></i>
      </button>
    </div>
  </Transition>
</template>
