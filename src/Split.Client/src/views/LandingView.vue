<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import AppButton from '@/components/AppButton.vue'
import MoneyAmount from '@/components/MoneyAmount.vue'
import { api, ApiError } from '@/api'
import type { PreviewBalance, SettlementPreview } from '@/types'

/*
  The landing page, and a working demo of the one thing this project is about.

  The demo calls /api/settlement-preview, which runs the same C# that settles real groups.
  Doing the sums in TypeScript here would have been easier and would have meant two
  implementations of the hard part, with the one on the front page being the untested one.
*/

interface Scenario {
  id: string
  label: string
  blurb: string
  balances: PreviewBalance[]
}

const scenarios: Scenario[] = [
  {
    id: 'pairs',
    label: 'Two pairs',
    blurb:
      'Chong owes exactly what Aisyah is owed, and Dinesh owes exactly what Bala is owed. ' +
      'Matching the biggest debtor to the biggest creditor would take three payments. ' +
      'Spotting that the group falls into two independent pairs takes two.',
    balances: [
      { name: 'Aisyah', net: 30 },
      { name: 'Bala', net: 20 },
      { name: 'Chong', net: -30 },
      { name: 'Dinesh', net: -20 },
    ],
  },
  {
    id: 'rounding',
    label: 'Awkward rounding',
    blurb:
      'Ten split three ways is 3.3333... per person. The shares come out 3.34 / 3.33 / 3.33, ' +
      'so they add back up to exactly ten and the group can actually reach zero.',
    balances: [
      { name: 'Aisyah', net: 6.66 },
      { name: 'Bala', net: -3.33 },
      { name: 'Chong', net: -3.33 },
    ],
  },
  {
    id: 'one-payer',
    label: 'One person paid',
    blurb:
      'Aisyah covered a 400 dinner for five. Nobody else owes anybody but her, so there is ' +
      'no shortcut here - four payments really is the minimum.',
    balances: [
      { name: 'Aisyah', net: 320 },
      { name: 'Bala', net: -80 },
      { name: 'Chong', net: -80 },
      { name: 'Dinesh', net: -80 },
      { name: 'Elena', net: -80 },
    ],
  },
]

const selectedId = ref(scenarios[0]!.id)
const result = ref<SettlementPreview | null>(null)
const pending = ref(false)
const failed = ref<string | null>(null)

const scenario = computed(() => scenarios.find((s) => s.id === selectedId.value) ?? scenarios[0]!)

/** Sorted so the people owed money read first, matching the real balances screen. */
const sortedBalances = computed(() => [...scenario.value.balances].sort((a, b) => b.net - a.net))

const saved = computed(() => {
  if (!result.value) return 0
  return result.value.naivePaymentCount - result.value.payments.length
})

async function loadPreview() {
  pending.value = true
  failed.value = null

  try {
    result.value = await api.settlementPreview(scenario.value.balances)
  } catch (caught) {
    result.value = null
    failed.value = caught instanceof ApiError ? caught.message : 'Could not reach the server.'
  } finally {
    pending.value = false
  }
}

// Re-run whenever the scenario changes. `watch` on a getter, and no `immediate` here because
// onMounted below does the first run - see docs/REACT-TO-VUE.md on why that default differs
// from useEffect.
watch(selectedId, loadPreview)
onMounted(loadPreview)
</script>

<template>
  <div class="landing">
    <!-- Hero ------------------------------------------------------------------------- -->
    <section class="hero">
      <h1 class="hero__line">
        Split the bill.<br />
        Settle in the <span class="hero__accent">fewest</span> payments.
      </h1>

      <p class="prose hero__sub">
        Log who paid for what. Split keeps everyone's balance live, then works out the
        shortest list of payments that clears the whole group — not everyone paying everyone
        back.
      </p>

      <div class="hero__actions">
        <AppButton variant="primary" @click="$router.push({ name: 'new-group' })">
          Start a group
        </AppButton>
        <AppButton variant="ghost" @click="$router.push({ name: 'new-group', hash: '#join' })">
          I have a code
        </AppButton>
      </div>
    </section>

    <!-- Live demo -------------------------------------------------------------------- -->
    <section class="demo">
      <p class="label demo__eyebrow">Try it — this is the real algorithm</p>

      <div class="demo__tabs" role="tablist" aria-label="Example groups">
        <button
          v-for="option in scenarios"
          :key="option.id"
          type="button"
          role="tab"
          class="tab"
          :class="{ 'tab--on': option.id === selectedId }"
          :aria-selected="option.id === selectedId"
          @click="selectedId = option.id"
        >
          {{ option.label }}
        </button>
      </div>

      <p class="prose demo__blurb">{{ scenario.blurb }}</p>

      <div class="demo__grid">
        <div class="demo__panel">
          <p class="label">Where they stand</p>

          <ul class="rows">
            <li v-for="person in sortedBalances" :key="person.name" class="rows__row">
              <span class="rows__name">{{ person.name }}</span>
              <span class="rows__value">
                <span class="rows__verb muted">{{ person.net > 0 ? 'is owed' : 'owes' }}</span>
                <MoneyAmount :amount="person.net" absolute signed />
              </span>
            </li>
          </ul>
        </div>

        <div class="demo__panel demo__panel--result">
          <p class="label">Settle up</p>

          <p v-if="pending" class="muted demo__state">Working it out...</p>
          <p v-else-if="failed" class="demo__state demo__error">{{ failed }}</p>

          <template v-else-if="result">
            <ul class="rows">
              <li v-for="(payment, i) in result.payments" :key="i" class="rows__row">
                <span class="rows__name">
                  {{ payment.from }}
                  <span class="rows__arrow" aria-hidden="true">&rarr;</span>
                  {{ payment.to }}
                </span>
                <MoneyAmount :amount="payment.amount" />
              </li>
            </ul>

            <p class="verdict">
              <strong class="verdict__count numeric">{{ result.payments.length }}</strong>
              {{ result.payments.length === 1 ? 'payment' : 'payments' }}
              <template v-if="saved > 0">
                <span class="accent">
                  instead of {{ result.naivePaymentCount }}.
                </span>
              </template>
              <template v-else>
                <span class="muted">— already the minimum.</span>
              </template>
            </p>
          </template>
        </div>
      </div>
    </section>

    <!-- How it works ----------------------------------------------------------------- -->
    <section class="steps">
      <h2>How it works</h2>

      <ol class="steps__list">
        <li class="step">
          <span class="step__n numeric">01</span>
          <div>
            <h3 class="step__title">Make a group</h3>
            <p class="prose step__body">
              Name it, add the people in it. No account needed — you get a link you can paste
              into the group chat.
            </p>
          </div>
        </li>

        <li class="step">
          <span class="step__n numeric">02</span>
          <div>
            <h3 class="step__title">Log what people paid</h3>
            <p class="prose step__body">
              Who paid, how much, and who it was split between. Balances update as you go, to
              the cent — every amount is worked out in whole cents on the server, never in
              floating point.
            </p>
          </div>
        </li>

        <li class="step">
          <span class="step__n numeric">03</span>
          <div>
            <h3 class="step__title">Settle up</h3>
            <p class="prose step__body">
              Split finds the fewest payments that clear everybody. Finding that minimum is
              an NP-hard problem, so for groups up to eighteen it searches every way of
              splitting the group into subsets that already cancel out.
            </p>
          </div>
        </li>
      </ol>
    </section>

    <section class="closing">
      <h2 class="closing__line">Stop doing this in your notes app.</h2>
      <AppButton variant="primary" @click="$router.push({ name: 'new-group' })">
        Start a group
      </AppButton>
    </section>
  </div>
</template>

<style scoped>
.landing {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: 0 var(--space-4) var(--space-20);
}

/* ---------------------------------------------------------------------------------------
   Hero. This is the one screen in the app built the way the source design system intends:
   one thing in a lot of void, at the full display size.
   --------------------------------------------------------------------------------------- */

.hero {
  padding: var(--space-16) 0 var(--space-20);
}

.hero__line {
  font-size: var(--text-hero);
  font-weight: var(--weight-regular);
  line-height: 0.95;
  letter-spacing: var(--tracking-display);
  margin: 0;
}

.hero__accent {
  color: var(--saffron-spark);
}

.hero__sub {
  max-width: 48ch;
  margin-top: var(--space-5);
}

.hero__actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  margin-top: var(--space-6);
  flex-wrap: wrap;
}

/* ---------------------------------------------------------------------------------------
   Demo
   --------------------------------------------------------------------------------------- */

.demo {
  padding-bottom: var(--space-20);
}

.demo__eyebrow {
  margin-bottom: var(--space-3);
}

.demo__tabs {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-1);
}

.tab {
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

.tab:hover {
  color: var(--bone-white);
  background: var(--surface-hover);
}

.tab--on {
  color: var(--bone-white);
  border-color: var(--electric-iris);
  background: rgba(128, 82, 255, 0.14);
}

.demo__blurb {
  margin: var(--space-3) 0 var(--space-4);
  max-width: 62ch;
  font-size: var(--text-base);
}

.demo__grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--space-2);
}

.demo__panel {
  background: var(--surface-raised);
  border-radius: var(--radius-lg);
  padding: var(--space-4);
}

.demo__panel--result {
  display: flex;
  flex-direction: column;
}

.demo__state {
  padding: var(--space-3) 0;
}

.demo__error {
  color: var(--saffron-spark);
}

.rows {
  list-style: none;
  margin: var(--space-2) 0 0;
  padding: 0;
}

.rows__row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-2);
  padding: var(--space-2) 0;
  border-bottom: 1px solid var(--hairline);
}

.rows__row:last-child {
  border-bottom: none;
}

.rows__name {
  font-size: var(--text-base);
}

.rows__arrow {
  color: var(--electric-iris);
  padding: 0 2px;
}

.rows__value {
  display: inline-flex;
  align-items: baseline;
  gap: var(--space-1);
  white-space: nowrap;
}

.rows__verb {
  font-size: var(--text-xs);
}

.verdict {
  margin-top: auto;
  padding-top: var(--space-3);
  font-size: var(--text-base);
  color: var(--silver-mist);
}

.verdict__count {
  font-size: var(--text-h3);
  font-weight: var(--weight-regular);
  color: var(--bone-white);
  margin-right: 2px;
}

/* ---------------------------------------------------------------------------------------
   Steps
   --------------------------------------------------------------------------------------- */

.steps {
  padding-bottom: var(--space-20);
}

.steps__list {
  list-style: none;
  margin: var(--space-6) 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}

.step {
  display: flex;
  gap: var(--space-4);
  align-items: flex-start;
}

.step__n {
  font-size: var(--text-h3);
  color: var(--electric-iris);
  flex-shrink: 0;
  line-height: 1.2;
}

.step__title {
  font-size: var(--text-h3);
}

.step__body {
  margin-top: var(--space-1);
  max-width: 56ch;
}

/* ---------------------------------------------------------------------------------------
   Closing
   --------------------------------------------------------------------------------------- */

.closing {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: var(--space-5);
  padding: var(--space-10) 0;
  border-top: 1px solid var(--hairline);
}

.closing__line {
  font-size: var(--text-display);
  letter-spacing: var(--tracking-display);
  line-height: 1.05;
  max-width: 16ch;
}

@media (max-width: 640px) {
  .demo__grid {
    grid-template-columns: 1fr;
  }

  .hero {
    padding: var(--space-10) 0 var(--space-16);
  }
}
</style>
