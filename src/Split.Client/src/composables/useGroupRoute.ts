import { watch } from 'vue'
import { useRoute } from 'vue-router'
import { useGroupStore } from '@/stores/group'

/*
  Keeps the store in step with the :slug in the URL.

  This is the `useEffect` case, and it shows the two differences that matter:

    useEffect(() => { load(slug) }, [slug])

  becomes

    watch(() => route.params.slug, load, { immediate: true })

    1. The dependency is a GETTER, not a value in an array. Vue re-runs the getter to decide
       whether anything changed, so the dependency cannot drift out of sync with the body.

    2. `watch` does not run on mount unless you ask it to with `immediate: true`. That is the
       opposite of useEffect's default and the single most common thing to get wrong when
       moving across.

  Both views that need a group call this, so neither has to remember the rule about what
  happens when the user edits the URL or navigates between two groups.
*/

export function useGroupRoute() {
  const route = useRoute()
  const store = useGroupStore()

  watch(
    () => route.params.slug,
    (slug) => {
      if (typeof slug === 'string' && slug.length > 0) {
        void store.load(slug)
      }
    },
    { immediate: true },
  )

  return store
}
