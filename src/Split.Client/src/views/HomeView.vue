<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import AppButton from '@/components/AppButton.vue'
import StateNote from '@/components/StateNote.vue'
import TextField from '@/components/TextField.vue'
import { useGroupStore } from '@/stores/group'

/*
  Create a group, or open an existing one by its code.

  Everything in this file is `ref` and `computed`. If you are reading it with React in mind:
  every `.value` here would be a `useState` setter call or a `useMemo` result there.
*/

const router = useRouter()
const store = useGroupStore()

const name = ref('')
const currency = ref('MYR')

// The people, as editable text fields. Starting at two because a group of one cannot split
// anything — the API enforces the same rule.
const memberNames = ref<string[]>(['', ''])

const joinCode = ref('')
const submitted = ref(false)

const cleanedMembers = computed(() =>
  memberNames.value.map((person) => person.trim()).filter((person) => person.length > 0),
)

const duplicateName = computed(() => {
  const seen = new Set<string>()
  for (const person of cleanedMembers.value) {
    const key = person.toLowerCase()
    if (seen.has(key)) return person
    seen.add(key)
  }
  return null
})

// Validation runs as a computed, so the message updates while the user types rather than
// only on submit. `submitted` gates whether it is actually shown.
const validationError = computed(() => {
  if (name.value.trim().length === 0) return 'Give the group a name.'
  if (cleanedMembers.value.length < 2) return 'Add at least two people.'
  if (duplicateName.value) return `${duplicateName.value} is in the list twice.`
  return null
})

const visibleError = computed(() => (submitted.value ? validationError.value : null))

function addPersonField() {
  memberNames.value.push('')
}

function removePersonField(index: number) {
  memberNames.value.splice(index, 1)
}

async function createGroup() {
  submitted.value = true
  if (validationError.value) return

  const created = await store.create(name.value.trim(), currency.value.trim(), cleanedMembers.value)

  if (created && store.group) {
    router.push({ name: 'group', params: { slug: store.group.slug } })
  }
}

function openByCode() {
  const code = joinCode.value.trim()
  if (code.length === 0) return

  // Accept a pasted URL as well as a bare code, because that is what people actually have on
  // their clipboard when a friend sends them a group.
  const slug = code.split('/').filter(Boolean).pop() ?? code
  router.push({ name: 'group', params: { slug } })
}
</script>

<template>
  <div class="page">
    <section class="hero">
      <h1>Split the bill,<br />settle in the fewest payments.</h1>
      <p class="prose hero__sub">
        Log what everyone paid. Split sorts out who owes whom, and works out the shortest list
        of payments that clears the group.
      </p>
    </section>

    <!--
      `.prevent` is an event modifier: it calls preventDefault() for you. Small thing, but it
      is the kind of sugar Vue has and React does not, and it keeps the handler free of
      plumbing.
    -->
    <form class="panel stack" novalidate @submit.prevent="createGroup">
      <h2>New group</h2>

      <TextField v-model="name" label="What is it for" placeholder="Langkawi trip" :maxlength="120" />

      <TextField
        v-model="currency"
        label="Currency"
        placeholder="MYR"
        :maxlength="3"
        hint="Three-letter code. Used for display only - nothing is converted."
      />

      <div class="people">
        <span class="label">Who is in</span>

        <div v-for="(_, index) in memberNames" :key="index" class="people__row">
          <!--
            v-model on an array element. `memberNames[index]` is a plain string, and Vue's
            reactivity tracks the array itself, so assigning through the index is picked up.
          -->
          <input
            v-model="memberNames[index]"
            class="people__input"
            type="text"
            maxlength="80"
            :placeholder="`Person ${index + 1}`"
            :aria-label="`Person ${index + 1}`"
          />

          <button
            v-if="memberNames.length > 2"
            type="button"
            class="people__remove"
            :aria-label="`Remove person ${index + 1}`"
            @click="removePersonField(index)"
          >
            &times;
          </button>
        </div>

        <AppButton variant="ghost" @click="addPersonField">+ Add someone</AppButton>
      </div>

      <StateNote v-if="visibleError" tone="error">{{ visibleError }}</StateNote>
      <StateNote v-else-if="store.error" tone="error">{{ store.error }}</StateNote>

      <!-- The one primary action on this view. -->
      <AppButton variant="primary" type="submit" full :busy="store.loading">
        {{ store.loading ? 'Creating...' : 'Create group' }}
      </AppButton>
    </form>

    <form class="join" @submit.prevent="openByCode">
      <TextField
        v-model="joinCode"
        label="Already have a group"
        placeholder="bright-tapir-4821"
        hint="Paste the code or the whole link."
      />
      <AppButton type="submit">Open</AppButton>
    </form>
  </div>
</template>

<style scoped>
.hero {
  padding: var(--space-10) 0 var(--space-6);
}

.hero__sub {
  max-width: 46ch;
  margin-top: var(--space-4);
}

.people {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.people__row {
  display: flex;
  align-items: center;
  gap: var(--space-1);
}

.people__input {
  flex: 1;
  font-family: inherit;
  font-size: var(--text-base);
  color: var(--bone-white);

  background: var(--surface-raised-2);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
  min-height: 48px;
}

.people__input::placeholder {
  color: var(--ash-gray);
}

.people__input:focus {
  outline: none;
  border-color: var(--electric-iris);
}

.people__remove {
  background: var(--surface-raised-2);
  border: none;
  border-radius: var(--radius-full);
  color: var(--ash-gray);
  cursor: pointer;
  font-size: 20px;
  line-height: 1;
  width: 44px;
  height: 44px;
  flex-shrink: 0;
}

.people__remove:hover {
  color: var(--saffron-spark);
}

.join {
  display: flex;
  align-items: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-10);
}

.join > :first-child {
  flex: 1;
}
</style>
