# React to Vue, through this codebase

A map from what you already know onto what is actually in this repository. Every section
points at a real file, so you can read the explanation and the code side by side.

The single most important thing, first:

> **In `<script>` you write `count.value`. In `<template>` you write `count`.**
>
> A `ref` is a box. Script code handles the box; templates are compiled, and the compiler
> unwraps it for you. Nearly every "why is this undefined" moment in your first week is this.

---

## The cheat sheet

| React | Vue | Where to see it here |
|---|---|---|
| `useState` | `ref` | [`stores/group.ts`](../src/Split.Client/src/stores/group.ts) |
| `useMemo` | `computed` | [`views/HomeView.vue`](../src/Split.Client/src/views/HomeView.vue) |
| `useEffect` | `watch` / `watchEffect` | [`composables/useGroupRoute.ts`](../src/Split.Client/src/composables/useGroupRoute.ts) |
| `useRef` (DOM) | `ref` + `ref="name"` attribute | not used here |
| Custom hook | Composable | [`composables/useMoney.ts`](../src/Split.Client/src/composables/useMoney.ts) |
| Redux / Zustand | Pinia | [`stores/group.ts`](../src/Split.Client/src/stores/group.ts) |
| React Router | Vue Router | [`router.ts`](../src/Split.Client/src/router.ts) |
| `<Outlet />` | `<RouterView />` | [`App.vue`](../src/Split.Client/src/App.vue) |
| `props.children` | `<slot />` | [`components/AppButton.vue`](../src/Split.Client/src/components/AppButton.vue) |
| `onClick` prop | `defineEmits` + `@click` | [`components/AppButton.vue`](../src/Split.Client/src/components/AppButton.vue) |
| Controlled input | `v-model` / `defineModel` | [`components/TextField.vue`](../src/Split.Client/src/components/TextField.vue) |
| `{cond && <X/>}` | `v-if` | [`views/GroupView.vue`](../src/Split.Client/src/views/GroupView.vue) |
| `list.map(...)` | `v-for` | [`views/GroupView.vue`](../src/Split.Client/src/views/GroupView.vue) |
| CSS modules | `<style scoped>` | every `.vue` file |
| `React.lazy` | `() => import(...)` in the route | [`router.ts`](../src/Split.Client/src/router.ts) |

---

## 1. State: `useState` becomes `ref`

```ts
// React
const [loading, setLoading] = useState(false)
setLoading(true)
```

```ts
// Vue — src/Split.Client/src/stores/group.ts
const loading = ref(false)
loading.value = true
```

There is no setter. You assign to `.value`, and anything that read it updates.

The deeper difference: React re-runs your whole component function on every state change,
and you work around that with `useCallback`, `useMemo` and dependency arrays. Vue runs your
`setup` **once** and builds a dependency graph. Nothing below re-executes when `loading`
changes except the specific things that read `loading`.

That is why there are no dependency arrays anywhere in this project.

---

## 2. Derived state: `useMemo` becomes `computed`

```ts
// React
const nameById = useMemo(() => {
  const m = new Map()
  for (const member of members) m.set(member.id, member.name)
  return m
}, [members])          // <- get this array wrong and you ship a stale-data bug
```

```ts
// Vue — src/Split.Client/src/stores/group.ts
const nameById = computed(() => {
  const names = new Map<string, string>()
  for (const member of members.value) names.set(member.id, member.name)
  return names
})
```

Vue records which refs were *actually read* while the function ran. The dependency list is
discovered, not declared, so it cannot drift away from the body. A `computed` also caches:
read it ten times in a template and the function runs once.

Note `members.value` inside the function, because this is script. In a template it would be
plain `nameById`.

---

## 3. Effects: `useEffect` becomes `watch` — with two traps

See [`composables/useGroupRoute.ts`](../src/Split.Client/src/composables/useGroupRoute.ts).

```ts
// React
useEffect(() => { load(slug) }, [slug])
```

```ts
// Vue
watch(() => route.params.slug, (slug) => { load(slug) }, { immediate: true })
```

**Trap 1 — the source is a getter, not a value.** `watch(route.params.slug, ...)` would read
the string once and watch nothing. You pass `() => route.params.slug` so Vue can re-read it.

**Trap 2 — `watch` does not run on mount.** This is the exact opposite of `useEffect`, which
always runs after the first render. If you want the first run, pass `{ immediate: true }`.
Forgetting this gives you a screen that only loads data when you navigate *away and back*.

There is also `watchEffect`, which needs no source at all — it runs its body, notes what it
read, and re-runs when any of that changes. It is the closest thing to `useEffect` with no
dependency array, except that it is correct.

**Cleanup**: React returns a cleanup function. Vue passes you one:

```ts
watch(source, (value, oldValue, onCleanup) => {
  const timer = setTimeout(...)
  onCleanup(() => clearTimeout(timer))
})
```

---

## 4. Custom hooks become composables

[`composables/useMoney.ts`](../src/Split.Client/src/composables/useMoney.ts) is a composable:
a plain function named `use*` that calls other Composition API functions and returns reactive
values. Same idea as a hook, with the rules removed:

- It runs **once**, not on every render — no stale closures.
- There are no "rules of hooks". You can call a composable inside an `if`. Nothing breaks,
  because there is no call-order bookkeeping to corrupt.
- Lifecycle hooks registered inside it (`onMounted`, `onUnmounted`) still attach to the
  component that called it, so cleanup works the way you expect.

One idiom worth recognising:

```ts
export function useMoney(currency: MaybeRefOrGetter<string | undefined>) {
  const code = toValue(currency)   // accepts a value, a ref, OR a getter
}
```

`MaybeRefOrGetter` plus `toValue` lets callers hand you whatever they have. In
[`MoneyAmount.vue`](../src/Split.Client/src/components/MoneyAmount.vue) it is called as
`useMoney(() => props.currency)` — a getter, so the formatter tracks prop changes. Passing
`props.currency` directly would read it once and freeze.

---

## 5. Props and events

```tsx
// React
interface Props { variant?: 'primary' | 'secondary'; onClick?: () => void }
function AppButton({ variant = 'secondary', onClick, children }: Props) { ... }
```

```vue
<!-- Vue — src/Split.Client/src/components/AppButton.vue -->
<script setup lang="ts">
const props = withDefaults(defineProps<{ variant?: 'primary' | 'secondary' }>(), {
  variant: 'secondary',
})
const emit = defineEmits<{ click: [] }>()
</script>

<template>
  <button @click="emit('click')"><slot /></button>
</template>
```

Things to notice:

- `defineProps` and `defineEmits` are **compiler macros**. You do not import them, and they
  only work inside `<script setup>`.
- Props are **read-only**, enforced at runtime in development. One-way data flow is a rule
  here, not a convention.
- `<slot />` is `children`. Named slots (`<slot name="footer" />`) are the equivalent of
  passing elements as named props.
- Events are declared, not passed down as function props. The parent writes `@click="..."`.

**The destructuring trap.** This used to silently break reactivity:

```ts
const { variant } = defineProps<{ variant: string }>()   // a one-off copy in Vue < 3.5
```

Vue 3.5 made destructured props reactive, so it is now fine. This project still writes
`props.variant` everywhere, because it reads unambiguously and does not depend on the reader
knowing which Vue version introduced what.

---

## 6. `v-model`: the thing with no React equivalent

In React, a controlled input means the parent owns the state and passes `value` and
`onChange` down, and every wrapper component has to forward both.

```vue
<!-- the parent -->
<TextField v-model="description" label="What was it" />
```

```ts
// the child — src/Split.Client/src/components/TextField.vue
const model = defineModel<string>({ required: true })
// writing model.value updates the parent's ref
```

It is still just a prop and an event underneath (`:modelValue` and `@update:modelValue`), but
neither side writes that out. It works for arrays too — see
[`PersonChips.vue`](../src/Split.Client/src/components/PersonChips.vue), which takes
`v-model="splitAmong"` where `splitAmong` is a `string[]`.

---

## 7. Templates instead of JSX

```tsx
// React
{loading ? <Spinner/> : error ? <Error msg={error}/> : <List items={items}/>}
{items.map(item => <Row key={item.id} item={item} />)}
```

```vue
<!-- Vue — src/Split.Client/src/views/GroupView.vue -->
<StateNote v-if="loading">Loading...</StateNote>
<StateNote v-else-if="error" tone="error">{{ error }}</StateNote>
<ul v-else>
  <li v-for="item in items" :key="item.id">{{ item.description }}</li>
</ul>
```

- `v-if` removes the element from the DOM; `v-show` only sets `display: none`. Use `v-if` for
  things that are usually absent, `v-show` for things toggled constantly.
- `:key` is React's `key`, for the same reason.
- `:prop="value"` is shorthand for `v-bind:prop`, `@event` for `v-on:event`.
- Event modifiers have no React equivalent and are genuinely useful: `@submit.prevent` calls
  `preventDefault()` for you, so handlers stay free of plumbing. Also `.stop`, `.once`,
  `.self`, and key modifiers like `@keyup.enter`.
- `<template>` as a wrapper is React's `<>...</>` — it groups without emitting an element.

---

## 8. Pinia instead of Redux

[`stores/group.ts`](../src/Split.Client/src/stores/group.ts) is a **setup store**: the body
is ordinary Composition API code, and what it returns is the store.

```ts
export const useGroupStore = defineStore('group', () => {
  const group = ref<Group | null>(null)        // state
  const members = computed(() => group.value?.members ?? [])   // getter
  async function load(slug: string) { ... }    // action
  return { group, members, load }
})
```

In a component:

```ts
const store = useGroupStore()
store.load('bright-tapir-4821')
// store.group and store.members are already reactive — no useSelector
```

No reducers, no action types, no dispatch, no immutable update helpers. You mutate and
everything that read the value updates.

**The one real gotcha**: destructuring a store breaks reactivity, exactly like destructuring
props used to.

```ts
const { group } = useGroupStore()           // dead — a snapshot
const { group } = storeToRefs(useGroupStore())  // alive — refs
const store = useGroupStore()                   // alive — what this project does
```

This codebase keeps `store.group` everywhere, which sidesteps the question entirely and makes
it obvious where a value comes from.

---

## 9. Scoped styles instead of CSS-in-JS

```vue
<style scoped>
.btn { background: var(--electric-iris); }
</style>
```

`scoped` adds a generated attribute to this component's elements and to these selectors, so
the rules cannot leak. No naming convention, no build-time CSS extraction library, no runtime
style injection — plain CSS, in the same file as the component that uses it.

Global styles still exist for tokens and resets:
[`styles/theme.css`](../src/Split.Client/src/styles/theme.css) and
[`styles/base.css`](../src/Split.Client/src/styles/base.css), imported once in `main.ts`.

To reach inside a child component deliberately, there is `:deep(.selector)`.

---

## 10. Mounting the app

```tsx
// React
createRoot(el).render(
  <Provider store={store}><BrowserRouter><App/></BrowserRouter></Provider>
)
```

```ts
// Vue — src/Split.Client/src/main.ts
createApp(App).use(createPinia()).use(router).mount('#app')
```

Plugins are installed on the app instance rather than wrapped around the component tree.
Fewer nested providers, and no "which provider am I inside" questions.

---

## Questions worth being able to answer about this code

If this project comes up in an interview, these are the things to have thought about:

1. **Why `computed` and not a function?** Because `computed` caches on its dependencies. A
   plain function re-runs on every read. `balances` in the store is read several times per
   render.

2. **Why does `useMoney` take `() => props.currency` rather than `props.currency`?** Because
   a getter can be re-read when the prop changes; a plain value is a one-off snapshot.

3. **Why is there no money arithmetic in the client?** Because JavaScript numbers are IEEE
   doubles and cannot represent 0.1 exactly. Every figure that matters is computed in C# as
   `decimal`, over integer cents. The one piece of client-side arithmetic — the "about X
   each" preview in `AddExpenseView.vue` — is labelled as approximate on screen.

4. **Why does every endpoint return the whole group?** So the store never patches its own
   copy of the state to match the server. There is one source of truth and no reconciliation
   code to get wrong. It costs a slightly larger payload on a small object.

5. **Why a setup store rather than an options store?** The setup form is plain Composition
   API, so the store reads like the components do, and anything not returned stays private.

6. **What would you change if this grew?** Generate `types.ts` from the OpenAPI document;
   split the single store per group into a cache keyed by slug; add optimistic updates for
   adding an expense; move settlement onto its own endpoint if the group payload gets heavy.
