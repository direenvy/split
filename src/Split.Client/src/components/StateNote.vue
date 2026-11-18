<script setup lang="ts">
/*
  The loading / error / empty message, in one place so the three views cannot each invent
  their own wording and spacing.
*/

withDefaults(
  defineProps<{
    tone?: 'quiet' | 'error'
  }>(),
  { tone: 'quiet' },
)
</script>

<template>
  <p class="note" :class="`note--${tone}`" :role="tone === 'error' ? 'alert' : undefined">
    <slot />
  </p>
</template>

<style scoped>
.note {
  font-size: var(--text-body);
  font-weight: var(--weight-thin);
  line-height: 1.5;
  padding: var(--space-4) 0;
}

.note--quiet {
  color: var(--ash-gray);
}

.note--error {
  color: var(--saffron-spark);
}
</style>
