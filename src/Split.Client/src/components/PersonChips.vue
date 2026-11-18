<script setup lang="ts">
import type { Member } from '@/types'

/*
  A multi-select built on `v-model`, showing how to make a custom component work with it when
  the value is an array rather than a string.

  The parent writes `v-model="splitAmong"`. This component never mutates that array in place
  — it assigns a new one. Vue would cope with a mutation, but replacing makes the change
  obvious at the call site and matches the habit a React developer already has.
*/

const selected = defineModel<string[]>({ required: true })

const props = defineProps<{
  members: Member[]
  /** Shown when the selection is empty and the user has already tried to submit. */
  error?: string | null
}>()

function isSelected(id: string): boolean {
  return selected.value.includes(id)
}

function toggle(id: string) {
  selected.value = isSelected(id)
    ? selected.value.filter((memberId) => memberId !== id)
    : [...selected.value, id]
}

function selectAll() {
  selected.value = props.members.map((member) => member.id)
}

function selectNone() {
  selected.value = []
}
</script>

<template>
  <div class="chips">
    <div class="chips__head">
      <span class="label">Split between</span>

      <div class="chips__bulk">
        <button type="button" class="chips__link" @click="selectAll">Everyone</button>
        <span class="chips__sep" aria-hidden="true">/</span>
        <button type="button" class="chips__link" @click="selectNone">None</button>
      </div>
    </div>

    <div class="chips__list" role="group" aria-label="Split between">
      <!--
        `:key` is React's `key`, same reason. `v-for` can iterate arrays, objects, numbers
        and anything iterable.

        `aria-pressed` makes these read as toggle buttons rather than as plain buttons,
        which is what they actually are.
      -->
      <button
        v-for="member in members"
        :key="member.id"
        type="button"
        class="chip"
        :class="{ 'chip--on': isSelected(member.id) }"
        :aria-pressed="isSelected(member.id)"
        @click="toggle(member.id)"
      >
        {{ member.name }}
      </button>
    </div>

    <p v-if="error" class="chips__error">{{ error }}</p>
  </div>
</template>

<style scoped>
.chips {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.chips__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-2);
}

.chips__bulk {
  display: flex;
  align-items: center;
  gap: var(--space-1);
}

.chips__link {
  background: none;
  border: none;
  padding: 0;
  cursor: pointer;

  font-family: inherit;
  font-size: var(--text-xs);
  font-weight: var(--weight-semibold);
  letter-spacing: var(--tracking-label);
  text-transform: uppercase;
  color: var(--ash-gray);
}

.chips__link:hover {
  color: var(--bone-white);
}

.chips__sep {
  color: var(--ash-gray);
  font-size: var(--text-xs);
}

.chips__list {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1);
}

.chip {
  font-family: inherit;
  font-size: var(--text-base);
  color: var(--ash-gray);

  background: var(--surface-raised-2);
  border: 1px solid transparent;
  border-radius: var(--radius-full);
  padding: var(--space-1) var(--space-3);
  min-height: 40px;
  cursor: pointer;

  transition: background-color 120ms ease, color 120ms ease, border-color 120ms ease;
}

.chip:hover {
  background: var(--surface-hover);
  color: var(--bone-white);
}

/* Selected chips use the violet as an outline rather than a fill, so they do not compete
   with the one filled primary button on the screen. */
.chip--on {
  color: var(--bone-white);
  border-color: var(--electric-iris);
  background: rgba(128, 82, 255, 0.14);
}

.chips__error {
  font-size: var(--text-xs);
  color: var(--saffron-spark);
}
</style>
