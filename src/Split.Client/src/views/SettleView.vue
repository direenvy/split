<script setup lang="ts">
import { computed } from 'vue'
import AppButton from '@/components/AppButton.vue'
import MoneyAmount from '@/components/MoneyAmount.vue'
import StateNote from '@/components/StateNote.vue'
import { useGroupRoute } from '@/composables/useGroupRoute'

/*
  The payoff screen: the shortest list of payments that clears the group.

  There is no logic here beyond presentation. The list comes from the server, where
  DebtSimplifier works out the minimum — see src/Split.Domain/DebtSimplifier.cs. Recomputing
  it in the browser would mean two implementations of the hard part, and the one in the
  browser would be the untested one.
*/

const store = useGroupRoute()

function nameOf(id: string): string {
  return store.nameById.get(id) ?? 'Unknown'
}

/** How many payments a naive settlement would have needed, for comparison. */
const naivePaymentCount = computed(() => {
  const debtors = store.balances.filter((person) => person.net < 0).length
  const creditors = store.balances.filter((person) => person.net > 0).length
  return debtors * creditors
})

const saved = computed(() => naivePaymentCount.value - store.settlement.length)
</script>

<template>
  <div class="page">
    <StateNote v-if="store.loading && !store.group">Loading...</StateNote>

    <StateNote v-else-if="!store.group" tone="error">
      {{ store.error ?? 'That group could not be loaded.' }}
    </StateNote>

    <template v-else>
      <h1 class="title">Settle up</h1>

      <StateNote v-if="store.isSettled">
        Everyone is square. Nothing to pay.
      </StateNote>

      <template v-else>
        <p class="prose title__sub">
          {{ store.settlement.length }}
          {{ store.settlement.length === 1 ? 'payment clears' : 'payments clear' }} the whole
          group.
          <span v-if="saved > 0" class="accent">
            That is {{ saved }} fewer than paying everyone back individually.
          </span>
        </p>

        <ol class="payments">
          <li v-for="(payment, index) in store.settlement" :key="index" class="payment">
            <div class="payment__people">
              <span class="payment__from">{{ nameOf(payment.from) }}</span>
              <span class="payment__arrow" aria-hidden="true">&rarr;</span>
              <span class="payment__to">{{ nameOf(payment.to) }}</span>
              <span class="visually-hidden">pays</span>
            </div>

            <MoneyAmount
              :amount="payment.amount"
              :currency="store.group.currency"
              size="large"
            />
          </li>
        </ol>
      </template>

      <AppButton
        variant="primary"
        full
        @click="$router.push({ name: 'group', params: { slug: store.group.slug } })"
      >
        Back to group
      </AppButton>
    </template>
  </div>
</template>

<style scoped>
.title {
  font-size: var(--text-h1);
}

.title__sub {
  margin: var(--space-2) 0 var(--space-6);
}

.payments {
  list-style: none;
  margin: 0 0 var(--space-10);
  padding: 0;
  counter-reset: payment;
}

/*
  Each payment gets its own raised surface. This is the one place the source system's
  "one or two elements per viewport" instinct survives intact: a settlement is usually two
  or three lines, and they are the most important numbers in the app, so they get room.
*/
.payment {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);

  background: var(--surface-raised);
  border-radius: var(--radius-lg);
  padding: var(--space-4);
  margin-bottom: var(--space-2);
}

.payment__people {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-body);
  font-weight: var(--weight-thin);
}

.payment__from {
  color: var(--bone-white);
}

.payment__arrow {
  color: var(--electric-iris);
}

.payment__to {
  color: var(--bone-white);
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  clip-path: inset(50%);
  white-space: nowrap;
}

@media (max-width: 480px) {
  .payment {
    flex-direction: column;
    align-items: flex-start;
    gap: var(--space-2);
  }
}
</style>
