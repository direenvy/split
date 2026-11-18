import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api'
import { ApiError } from '@/api'
import type { AddExpenseRequest, Group } from '@/types'

/*
  ------------------------------------------------------------------------------------------
  Coming from Redux or Zustand? Read this first.
  ------------------------------------------------------------------------------------------

  This is a Pinia "setup store". The body of the function below is ordinary Composition API
  code — the same refs and computeds you would write inside a component — and whatever it
  returns becomes the store. There are no actions, reducers, dispatch, or immutable updates.
  You mutate the ref and every component using it re-renders.

      Redux                        Pinia
      ------------------------     ---------------------------------
      createSlice / reducer        this function body
      state.group                  const group = ref<Group|null>(null)
      useSelector(s => s.group)    store.group   (already reactive)
      createSelector / reselect    computed(() => ...)
      dispatch(thunk())            just call the async function
      immutable update             group.value = next

  One Vue-specific thing that trips React developers constantly: inside this file you must
  write `group.value`, because a ref is a box. In a <template> the box is unwrapped for you,
  so there you write just `group`. Both forms appear throughout this project; the rule is
  simply script versus template.
*/

export const useGroupStore = defineStore('group', () => {
  // ---------------------------------------------------------------------------------------
  // State — `ref` is `useState`, minus the setter. Assign to `.value` to change it.
  // ---------------------------------------------------------------------------------------

  const group = ref<Group | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ---------------------------------------------------------------------------------------
  // Derived state — `computed` is `useMemo` with no dependency array. Vue tracks which refs
  // were read while the function ran and re-runs it only when one of those changes. Getting
  // the dependencies wrong is not a thing that can happen.
  // ---------------------------------------------------------------------------------------

  const slug = computed(() => group.value?.slug ?? null)

  const members = computed(() => group.value?.members ?? [])

  /** Member id to name, so rows can label a payment without searching the array each time. */
  const nameById = computed(() => {
    const names = new Map<string, string>()
    for (const member of members.value) {
      names.set(member.id, member.name)
    }
    return names
  })

  /** Balances with the person's name attached, biggest creditor first. */
  const balances = computed(() => {
    const rows = (group.value?.balances ?? []).map((balance) => ({
      ...balance,
      name: nameById.value.get(balance.participantId) ?? 'Unknown',
    }))

    return rows.sort((a, b) => b.net - a.net)
  })

  const settlement = computed(() => group.value?.settlement ?? [])

  /** True when nobody owes anybody anything. */
  const isSettled = computed(() => group.value !== null && settlement.value.length === 0)

  const expenses = computed(() => group.value?.expenses ?? [])

  // ---------------------------------------------------------------------------------------
  // Actions — plain async functions. No thunk middleware, no dispatch.
  // ---------------------------------------------------------------------------------------

  /**
   * Runs an API call with the shared loading and error state around it, and stores whatever
   * group comes back. Every endpoint returns the whole group, so one helper covers all of
   * them and the UI never has to patch its own copy of the state to match the server.
   */
  async function run(call: () => Promise<Group>): Promise<boolean> {
    loading.value = true
    error.value = null

    try {
      group.value = await call()
      return true
    } catch (caught) {
      error.value = caught instanceof ApiError ? caught.message : 'Something went wrong.'
      return false
    } finally {
      loading.value = false
    }
  }

  function load(forSlug: string) {
    // Drop a previously loaded group immediately, so a slow fetch never shows the old group's
    // numbers under the new group's name.
    if (group.value?.slug !== forSlug) {
      group.value = null
    }

    return run(() => api.getGroup(forSlug))
  }

  function create(name: string, currency: string, memberNames: string[]) {
    return run(() => api.createGroup({ name, currency, members: memberNames }))
  }

  function addMember(name: string) {
    const current = group.value
    if (!current) return Promise.resolve(false)

    return run(() => api.addMember(current.slug, name))
  }

  function addExpense(expense: AddExpenseRequest) {
    const current = group.value
    if (!current) return Promise.resolve(false)

    return run(() => api.addExpense(current.slug, expense))
  }

  function deleteExpense(expenseId: string) {
    const current = group.value
    if (!current) return Promise.resolve(false)

    return run(() => api.deleteExpense(current.slug, expenseId))
  }

  function clearError() {
    error.value = null
  }

  // Everything returned here is the store's public surface. Anything declared above and not
  // returned stays private to the store, which is a nice property Redux slices do not have.
  return {
    group,
    loading,
    error,
    slug,
    members,
    nameById,
    balances,
    settlement,
    isSettled,
    expenses,
    load,
    create,
    addMember,
    addExpense,
    deleteExpense,
    clearError,
  }
})
