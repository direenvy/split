<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef } from 'vue'
import AppButton from '@/components/AppButton.vue'
import MoneyAmount from '@/components/MoneyAmount.vue'
import StateNote from '@/components/StateNote.vue'
import { useGroupRoute } from '@/composables/useGroupRoute'
import { useMoney } from '@/composables/useMoney'

/*
  The group's home: live balances at the top, the expense log underneath.

  One line of setup does all the data loading. `useGroupRoute` watches the :slug in the URL
  and keeps the store pointed at the right group — see that composable for how `watch`
  compares with `useEffect`.
*/

const store = useGroupRoute()

const { format } = useMoney(() => store.group?.currency)

const copied = ref(false)
const confirmingDelete = ref<string | null>(null)

// --- adding somebody to the group ---------------------------------------------------------

const addingPerson = ref(false)
const newPersonName = ref('')

/*
  A template ref: the Vue equivalent of React's useRef on a DOM node.

  In React you write `const input = useRef(null)` and `<input ref={input} />`, then reach for
  `input.current`. Here `useTemplateRef('new-person')` pairs with `ref="new-person"` in the
  template, and the node arrives as `.value`. Vue 3.5 added this named form; before it, you
  declared a plain `ref(null)` whose variable name had to match the attribute, which was easy
  to break by renaming one and not the other.
*/
const newPersonInput = useTemplateRef<HTMLInputElement>('new-person')

async function startAddingPerson() {
  store.clearError()
  addingPerson.value = true

  // The input does not exist yet: v-if has not re-rendered. `nextTick` waits for the DOM to
  // catch up with the state change, which is the job React does with a layout effect.
  await nextTick()
  newPersonInput.value?.focus()
}

function cancelAddingPerson() {
  addingPerson.value = false
  newPersonName.value = ''
  store.clearError()
}

async function confirmAddPerson() {
  const name = newPersonName.value.trim()
  if (name.length === 0) return

  // The server is the one that decides whether this name is already taken — it owns the
  // roster, and checking here as well would be a second rule to keep in step. A rejection
  // comes back through store.error and is shown under the field.
  const added = await store.addMember(name)

  if (added) {
    newPersonName.value = ''
    addingPerson.value = false
  }
}

const shareUrl = computed(() =>
  store.group ? `${window.location.origin}/g/${store.group.slug}` : '',
)

const dateFormatter = new Intl.DateTimeFormat('en-MY', { day: 'numeric', month: 'short' })

function formatDate(iso: string): string {
  return dateFormatter.format(new Date(iso))
}

function nameOf(id: string): string {
  return store.nameById.get(id) ?? 'Unknown'
}

/** A readable summary of who an expense was split between. */
function splitSummary(shareCount: number): string {
  if (shareCount === store.members.length) return 'everyone'
  return `${shareCount} ${shareCount === 1 ? 'person' : 'people'}`
}

async function copyLink() {
  try {
    await navigator.clipboard.writeText(shareUrl.value)
    copied.value = true
    window.setTimeout(() => (copied.value = false), 2000)
  } catch {
    // Clipboard access can be refused; the URL is in the address bar anyway.
    copied.value = false
  }
}

async function removeExpense(id: string) {
  // Two taps rather than a modal: deleting an expense changes everyone's balance, so it
  // deserves a confirmation, but not a whole dialog.
  if (confirmingDelete.value !== id) {
    confirmingDelete.value = id
    return
  }

  confirmingDelete.value = null
  await store.deleteExpense(id)
}
</script>

<template>
  <div class="page">
    <StateNote v-if="store.loading && !store.group">Loading...</StateNote>

    <StateNote v-else-if="store.error && !store.group" tone="error">
      {{ store.error }}
    </StateNote>

    <!--
      `v-else-if` / `v-else` chain rather than a ternary in JSX. The template reads top to
      bottom as the three states this screen can be in.
    -->
    <template v-else-if="store.group">
      <header class="head">
        <div>
          <h1 class="head__name">{{ store.group.name }}</h1>
          <p class="head__meta muted">
            {{ store.members.length }} people &middot; {{ format(store.group.totalSpend) }} spent
          </p>
        </div>

        <AppButton variant="ghost" @click="copyLink">
          {{ copied ? 'Copied' : 'Share' }}
        </AppButton>
      </header>

      <!-- Errors from anything other than the add-person field, which shows its own below. -->
      <StateNote v-if="store.error && !addingPerson" tone="error">{{ store.error }}</StateNote>

      <!-- Balances ------------------------------------------------------------------- -->
      <section class="panel">
        <div class="row-between section-head">
          <h2>Balances</h2>
          <span v-if="store.isSettled" class="label settled">All square</span>
        </div>

        <ul class="balances">
          <li v-for="person in store.balances" :key="person.participantId" class="balances__row">
            <span class="balances__name">{{ person.name }}</span>

            <span class="balances__value">
              <span class="balances__verb muted">
                {{ person.net > 0 ? 'is owed' : person.net < 0 ? 'owes' : 'settled' }}
              </span>
              <MoneyAmount
                :amount="person.net"
                :currency="store.group.currency"
                :absolute="true"
                signed
              />
            </span>
          </li>
        </ul>

        <!--
          Adding someone is a secondary action: this view already spends its one filled
          violet button on "Add expense", and a design system with a single primary colour
          only works if that stays true.
        -->
        <div class="addperson">
          <AppButton v-if="!addingPerson" variant="ghost" @click="startAddingPerson">
            + Add someone
          </AppButton>

          <form v-else @submit.prevent="confirmAddPerson">
            <div class="addperson__form">
              <input
                ref="new-person"
                v-model="newPersonName"
                class="addperson__input"
                type="text"
                maxlength="80"
                placeholder="Their name"
                aria-label="Name of the person to add"
                :aria-invalid="store.error ? 'true' : undefined"
                :disabled="store.loading"
                @keyup.esc="cancelAddingPerson"
              />

              <AppButton variant="secondary" type="submit" :busy="store.loading">Add</AppButton>
              <AppButton variant="ghost" :disabled="store.loading" @click="cancelAddingPerson">
                Cancel
              </AppButton>
            </div>

            <!--
              Shown here rather than in the page-level error slot above. "Aisyah is already in
              this group" is about the field the user is looking at, and a message two hundred
              pixels up the page reads like it is about something else.
            -->
            <p v-if="store.error" class="addperson__error" role="alert">{{ store.error }}</p>
          </form>
        </div>
      </section>

      <div class="actions">
        <AppButton
          variant="primary"
          full
          @click="$router.push({ name: 'add-expense', params: { slug: store.group.slug } })"
        >
          Add expense
        </AppButton>

        <AppButton
          full
          :disabled="store.isSettled"
          @click="$router.push({ name: 'settle', params: { slug: store.group.slug } })"
        >
          Settle up
        </AppButton>
      </div>

      <!-- Expenses ------------------------------------------------------------------- -->
      <section class="expenses">
        <h2 class="section-head">Expenses</h2>

        <StateNote v-if="store.expenses.length === 0">
          Nothing logged yet. Add the first expense and the balances will follow.
        </StateNote>

        <ul v-else class="expenses__list">
          <li v-for="expense in store.expenses" :key="expense.id" class="expense">
            <div class="expense__main">
              <p class="expense__what">{{ expense.description }}</p>
              <p class="expense__who muted">
                {{ nameOf(expense.paidBy) }} paid &middot; split
                {{ splitSummary(expense.shares.length) }} &middot;
                {{ formatDate(expense.createdAt) }}
              </p>
            </div>

            <div class="expense__side">
              <MoneyAmount :amount="expense.amount" :currency="store.group.currency" />

              <AppButton variant="danger" :busy="store.loading" @click="removeExpense(expense.id)">
                {{ confirmingDelete === expense.id ? 'Sure?' : 'Remove' }}
              </AppButton>
            </div>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<style scoped>
.head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--space-3);
  padding-bottom: var(--space-6);
}

.head__name {
  font-size: var(--text-h1);
}

.head__meta {
  font-size: var(--text-base);
  margin-top: var(--space-1);
}

.section-head {
  margin-bottom: var(--space-3);
}

.settled {
  color: var(--positive);
}

.balances {
  list-style: none;
  margin: 0;
  padding: 0;
}

/*
  Rows get a hairline rather than a card each. The source system forbids borders outright;
  a list of balances needs *some* separation, and a 7%-opacity rule is the lightest thing
  that does the job without turning the page into a table.
*/
.balances__row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-2) 0;
  border-bottom: 1px solid var(--hairline);
}

.balances__row:last-child {
  border-bottom: none;
  padding-bottom: 0;
}

.balances__name {
  font-size: var(--text-body);
  font-weight: var(--weight-thin);
}

.balances__value {
  display: inline-flex;
  align-items: baseline;
  gap: var(--space-1);
}

.balances__verb {
  font-size: var(--text-xs);
}

.addperson {
  margin-top: var(--space-2);
  padding-top: var(--space-2);
  border-top: 1px solid var(--hairline);
}

.addperson__form {
  display: flex;
  align-items: center;
  gap: var(--space-1);
}

.addperson__input {
  flex: 1;
  min-width: 0;
  font-family: inherit;
  font-size: var(--text-base);
  color: var(--bone-white);

  background: var(--surface-raised-2);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  padding: var(--space-2) var(--space-3);
  min-height: 44px;
}

.addperson__error {
  font-size: var(--text-xs);
  color: var(--saffron-spark);
  margin-top: var(--space-1);
}

.addperson__input::placeholder {
  color: var(--ash-gray);
}

.addperson__input:focus {
  outline: none;
  border-color: var(--electric-iris);
}

.actions {
  display: flex;
  gap: var(--space-2);
  margin: var(--space-4) 0 var(--space-10);
}

.expenses__list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.expense {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  padding: var(--space-3) 0;
  border-bottom: 1px solid var(--hairline);
}

.expense:last-child {
  border-bottom: none;
}

.expense__what {
  font-size: var(--text-body);
  font-weight: var(--weight-thin);
}

.expense__who {
  font-size: var(--text-xs);
  margin-top: 2px;
}

.expense__side {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-shrink: 0;
}

@media (max-width: 540px) {
  .actions {
    flex-direction: column;
  }

  /* At phone width the description, the payer line and the amount cannot share a row without
     the meta text wrapping around the price. Stack them instead, and keep the amount and the
     remove button together on their own line. */
  .expense {
    flex-direction: column;
    align-items: stretch;
    gap: var(--space-1);
  }

  .expense__side {
    justify-content: space-between;
  }
}
</style>
