<script setup lang="ts">
/*
  Typed props and emits, the modern way.

  `defineProps` and `defineEmits` are compiler macros: they are not imported, and they only
  work inside <script setup>. Passing a type argument gives you the same checking a React
  component gets from its props interface, plus runtime validation in development.

  `withDefaults` supplies defaults for optional props. Note that props are READ-ONLY — the
  one-way data flow rule is enforced, not just conventional.
*/

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'

const props = withDefaults(
  defineProps<{
    variant?: Variant
    type?: 'button' | 'submit'
    disabled?: boolean
    busy?: boolean
    full?: boolean
  }>(),
  {
    variant: 'secondary',
    type: 'button',
    disabled: false,
    busy: false,
    full: false,
  },
)

// The React equivalent of an `onClick` prop. Declaring it this way means the parent writes
// @click="..." and TypeScript knows the handler takes no arguments.
const emit = defineEmits<{ click: [] }>()

function onClick() {
  // Do not emit while a request is in flight: double-submitting an expense is the obvious
  // way for a user to end up owing twice what they should.
  if (props.disabled || props.busy) return
  emit('click')
}
</script>

<template>
  <button
    :type="type"
    :class="['btn', `btn--${variant}`, { 'btn--full': full, 'btn--busy': busy }]"
    :disabled="disabled || busy"
    @click="onClick"
  >
    <!--
      `props.variant` in script, bare `variant` in template. The template unwraps for you.
      A <slot> is `children` — this one is unnamed, so it takes whatever the parent puts
      between the tags.
    -->
    <span class="btn__label"><slot /></span>
  </button>
</template>

<style scoped>
/*
  `scoped` rewrites these selectors so they only match this component's own elements. It is
  the Vue answer to CSS modules or styled-components, built in, with no naming convention to
  maintain and plain CSS inside.
*/

.btn {
  font-family: inherit;
  font-size: var(--text-sm);
  font-weight: var(--weight-semibold);
  letter-spacing: var(--tracking-nav);
  text-transform: uppercase;

  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-1);

  padding: var(--space-2) var(--space-4);
  min-height: 44px; /* a comfortable touch target */

  border: none;
  border-radius: var(--radius-full);
  cursor: pointer;
  transition: background-color 120ms ease, color 120ms ease, opacity 120ms ease;
}

.btn:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

/* The one saturated violet, reserved for the single primary action on a view. If two buttons
   on a screen are both primary, neither is. */
.btn--primary {
  background: var(--electric-iris);
  color: var(--bone-white);
}

.btn--primary:hover:not(:disabled) {
  background: #8f66ff;
}

.btn--secondary {
  background: var(--surface-raised-2);
  color: var(--bone-white);
}

.btn--secondary:hover:not(:disabled) {
  background: var(--surface-hover);
}

.btn--ghost {
  background: transparent;
  color: var(--ash-gray);
  padding-inline: var(--space-2);
}

.btn--ghost:hover:not(:disabled) {
  color: var(--bone-white);
}

.btn--danger {
  background: transparent;
  color: var(--ash-gray);
}

.btn--danger:hover:not(:disabled) {
  color: var(--saffron-spark);
}

.btn--full {
  width: 100%;
}

.btn--busy .btn__label {
  opacity: 0.6;
}
</style>
