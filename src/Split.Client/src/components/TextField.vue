<script setup lang="ts">
import { useId } from 'vue'

/*
  `defineModel` has no React equivalent, and it is the clearest example of where Vue differs.

  In React, a controlled input means the parent holds the state and passes `value` and
  `onChange` down. Here the parent writes:

      <TextField v-model="name" label="Your name" />

  and this component declares `const model = defineModel<string>()`. Writing `model.value`
  updates the parent's ref. Under the hood it is still a prop plus an event — `v-model` is
  sugar for `:modelValue` and `@update:modelValue` — but neither side has to spell it out.

  `useId` (Vue 3.5+) generates an id that is stable across server and client, which is what
  connects the <label> to the <input> for screen readers.
*/

const model = defineModel<string>({ required: true })

withDefaults(
  defineProps<{
    label: string
    placeholder?: string
    type?: 'text' | 'number' | 'email'
    inputmode?: 'text' | 'decimal' | 'numeric'
    autocomplete?: string
    hint?: string
    error?: string | null
    maxlength?: number
  }>(),
  {
    placeholder: '',
    type: 'text',
    inputmode: 'text',
    autocomplete: 'off',
    hint: '',
    error: null,
    maxlength: 200,
  },
)

const id = useId()
</script>

<template>
  <div class="field">
    <label class="label" :for="id">{{ label }}</label>

    <input
      :id="id"
      v-model="model"
      class="field__input numeric"
      :class="{ 'field__input--invalid': error }"
      :type="type"
      :inputmode="inputmode"
      :placeholder="placeholder"
      :autocomplete="autocomplete"
      :maxlength="maxlength"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="error || hint ? `${id}-note` : undefined"
    />

    <!--
      v-if removes the element from the DOM entirely, where v-show would only hide it.
      For a message that is usually absent, v-if is the right one.
    -->
    <p v-if="error" :id="`${id}-note`" class="field__note field__note--error">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-note`" class="field__note">{{ hint }}</p>
  </div>
</template>

<style scoped>
.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.field__input {
  font-family: inherit;
  font-size: var(--text-base);
  color: var(--bone-white);

  background: var(--surface-raised-2);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
  min-height: 48px;

  transition: border-color 120ms ease, background-color 120ms ease;
}

.field__input::placeholder {
  color: var(--ash-gray);
}

.field__input:hover {
  background: var(--surface-hover);
}

.field__input:focus {
  outline: none;
  border-color: var(--electric-iris);
}

.field__input--invalid {
  border-color: var(--saffron-spark);
}

.field__note {
  font-size: var(--text-xs);
  color: var(--ash-gray);
}

.field__note--error {
  color: var(--saffron-spark);
}
</style>
