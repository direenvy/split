import { computed, toValue, type MaybeRefOrGetter } from 'vue'

/*
  A composable: a plain function whose name starts with `use`, which calls other Composition
  API functions and returns reactive values. It is exactly the idea behind a React hook, with
  two differences worth knowing:

    1. It runs ONCE, when the component sets up — not on every render. So there are no
       dependency arrays, no stale closures, and no rules about calling it conditionally.
       You can put a composable call inside an `if`. Nothing breaks.

    2. It has no hidden link to the component that called it. The reactivity comes from the
       refs themselves, not from the call order.

  `MaybeRefOrGetter` + `toValue` is the idiomatic way to accept "a value, a ref, or a getter"
  so callers are not forced to unwrap things before handing them over. `toValue` resolves
  whichever one it got.
*/

export function useMoney(currency: MaybeRefOrGetter<string | undefined>) {
  // Intl.NumberFormat is not cheap to build, so build it once per currency and rebuild only
  // when the currency actually changes. This is the `useMemo` case, and it is just `computed`.
  const formatter = computed(() => {
    const code = toValue(currency) ?? 'MYR'

    try {
      return new Intl.NumberFormat('en-MY', {
        style: 'currency',
        currency: code,
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })
    } catch {
      // An unknown currency code would make the constructor throw and take the page with it.
      return new Intl.NumberFormat('en-MY', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })
    }
  })

  /** "RM 42.50" — the amount as the user should read it. */
  function format(amount: number): string {
    return formatter.value.format(amount)
  }

  /** The same, without a sign, for places where the direction is already shown some other way. */
  function formatAbsolute(amount: number): string {
    return formatter.value.format(Math.abs(amount))
  }

  return { format, formatAbsolute }
}
