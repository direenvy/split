<script setup lang="ts">
import { computed } from 'vue'
import { useMoney } from '@/composables/useMoney'

/*
  A small presentational component, worth reading for one thing: `computed` inside a
  component, driven by props.

  Props are reactive. Destructuring them (`const { amount } = defineProps(...)`) used to break
  that by taking a one-off copy; Vue 3.5 added reactive destructure to fix it, but this
  project keeps `props.amount` everywhere because it is unambiguous to read and does not
  depend on the reader knowing which Vue version introduced what.
*/

const props = withDefaults(
  defineProps<{
    amount: number
    currency?: string
    /** Colour by direction: green when owed money, amber when owing it. */
    signed?: boolean
    /** Drop the minus sign — for places where an arrow already shows the direction. */
    absolute?: boolean
    size?: 'base' | 'large' | 'display'
  }>(),
  {
    currency: 'MYR',
    signed: false,
    absolute: false,
    size: 'base',
  },
)

const { format, formatAbsolute } = useMoney(() => props.currency)

const text = computed(() => (props.absolute ? formatAbsolute(props.amount) : format(props.amount)))

const tone = computed(() => {
  if (!props.signed) return 'neutral'
  if (props.amount > 0) return 'positive'
  if (props.amount < 0) return 'negative'
  return 'settled'
})
</script>

<template>
  <span class="money numeric" :class="[`money--${tone}`, `money--${size}`]">{{ text }}</span>
</template>

<style scoped>
.money {
  white-space: nowrap;
}

.money--base {
  font-size: var(--text-base);
}

.money--large {
  font-size: var(--text-h3);
}

.money--display {
  font-size: var(--text-display);
  letter-spacing: var(--tracking-display);
  line-height: 1.1;
}

.money--neutral {
  color: var(--bone-white);
}

/* Being owed is the calm state; owing is the one that needs acting on, so it takes the warm
   accent. Colour is never the only signal — the sign and the wording carry it too. */
.money--positive {
  color: var(--positive);
}

.money--negative {
  color: var(--negative);
}

.money--settled {
  color: var(--ash-gray);
}
</style>
