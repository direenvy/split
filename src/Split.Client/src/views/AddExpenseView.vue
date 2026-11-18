<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import AppButton from '@/components/AppButton.vue'
import PersonChips from '@/components/PersonChips.vue'
import StateNote from '@/components/StateNote.vue'
import TextField from '@/components/TextField.vue'
import { useGroupRoute } from '@/composables/useGroupRoute'

const router = useRouter()
const store = useGroupRoute()

const description = ref('')
const amountText = ref('')
const paidBy = ref<string>('')
const splitAmong = ref<string[]>([])
const submitted = ref(false)

/*
  The amount is held as a STRING, not a number.

  Binding a number input straight to a numeric ref means the browser hands you a JavaScript
  double, and a double cannot hold 0.1 exactly. Keeping the raw text means the user's "12.30"
  reaches the server as "12.30" and is parsed into a C# decimal there. The regex below is the
  client-side half of the same rule the API enforces, so the user gets told immediately
  instead of after a round trip.
*/
const amountPattern = /^\d{1,12}([.,]\d{1,2})?$/

const amount = computed(() => {
  const text = amountText.value.trim().replace(',', '.')
  if (!amountPattern.test(text)) return null

  const value = Number(text)
  return Number.isFinite(value) && value > 0 ? value : null
})

const validationError = computed(() => {
  if (description.value.trim().length === 0) return 'What was it for?'
  if (amountText.value.trim().length === 0) return 'How much was it?'
  if (amount.value === null) return 'Enter an amount like 42.50 - at most two decimal places.'
  if (!paidBy.value) return 'Who paid?'
  if (splitAmong.value.length === 0) return 'Pick at least one person to split it between.'
  return null
})

const visibleError = computed(() => (submitted.value ? validationError.value : null))

/** What each person's share will be, shown live so nobody is surprised by the rounding. */
const perPersonPreview = computed(() => {
  if (amount.value === null || splitAmong.value.length === 0) return null

  // Display only. The authoritative split is done in C# with decimal arithmetic, including
  // which person absorbs the leftover cent; this is just the rough figure.
  return amount.value / splitAmong.value.length
})

/*
  Default the form to the group once it has loaded.

  `watch` with a getter, like useEffect with a dependency — but note there is no
  `immediate: true` here. The group may already be in the store from a previous view, so the
  getter is also read once below. Writing it as `watch(..., { immediate: true })` would be
  equivalent; this spells out that the first run is the "already loaded" case.
*/
function applyDefaults() {
  const group = store.group
  if (!group) return

  if (!paidBy.value && group.members.length > 0) {
    paidBy.value = group.members[0].id
  }

  if (splitAmong.value.length === 0) {
    splitAmong.value = group.members.map((member) => member.id)
  }
}

watch(() => store.group, applyDefaults)
applyDefaults()

async function submit() {
  submitted.value = true
  if (validationError.value || amount.value === null) return

  const saved = await store.addExpense({
    description: description.value.trim(),
    amount: amount.value,
    paidBy: paidBy.value,
    splitAmong: splitAmong.value,
  })

  if (saved && store.group) {
    router.push({ name: 'group', params: { slug: store.group.slug } })
  }
}

function cancel() {
  router.back()
}
</script>

<template>
  <div class="page">
    <StateNote v-if="store.loading && !store.group">Loading...</StateNote>

    <StateNote v-else-if="!store.group" tone="error">
      {{ store.error ?? 'That group could not be loaded.' }}
    </StateNote>

    <template v-else>
      <h1 class="title">Add an expense</h1>
      <p class="prose title__sub">to {{ store.group.name }}</p>

      <form class="panel stack" novalidate @submit.prevent="submit">
        <TextField
          v-model="description"
          label="What was it"
          placeholder="Dinner at the night market"
        />

        <TextField
          v-model="amountText"
          label="How much"
          placeholder="42.50"
          inputmode="decimal"
          :maxlength="15"
          :hint="`In ${store.group.currency}. Two decimal places at most.`"
        />

        <div class="payer">
          <span class="label">Who paid</span>

          <!--
            A native <select> rather than a custom dropdown: it is keyboard accessible and
            correct on mobile for free, and this screen already has one custom control.
          -->
          <select v-model="paidBy" class="payer__select" aria-label="Who paid">
            <option v-for="member in store.members" :key="member.id" :value="member.id">
              {{ member.name }}
            </option>
          </select>
        </div>

        <PersonChips v-model="splitAmong" :members="store.members" />

        <p v-if="perPersonPreview !== null" class="preview muted">
          About {{ perPersonPreview.toFixed(2) }} each, across
          {{ splitAmong.length }}
          {{ splitAmong.length === 1 ? 'person' : 'people' }}. The server works out the exact
          split, including who absorbs the odd cent.
        </p>

        <StateNote v-if="visibleError" tone="error">{{ visibleError }}</StateNote>
        <StateNote v-else-if="store.error" tone="error">{{ store.error }}</StateNote>

        <div class="buttons">
          <AppButton variant="ghost" @click="cancel">Cancel</AppButton>
          <AppButton variant="primary" type="submit" :busy="store.loading">
            {{ store.loading ? 'Saving...' : 'Add expense' }}
          </AppButton>
        </div>
      </form>
    </template>
  </div>
</template>

<style scoped>
.title {
  font-size: var(--text-h1);
}

.title__sub {
  margin: var(--space-1) 0 var(--space-6);
}

.payer {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.payer__select {
  font-family: inherit;
  font-size: var(--text-base);
  color: var(--bone-white);

  background: var(--surface-raised-2);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
  min-height: 48px;
  cursor: pointer;
  appearance: none;
}

.payer__select:focus {
  outline: none;
  border-color: var(--electric-iris);
}

.preview {
  font-size: var(--text-xs);
}

.buttons {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-2);
}
</style>
