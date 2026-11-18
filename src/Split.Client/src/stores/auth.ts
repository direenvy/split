import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { api } from '@/api'
import type { CurrentUser } from '@/types'

/*
  Who is signed in.

  Kept separate from the group store because it has a completely different lifetime: the
  group store is swapped out every time you open a different group, while this is loaded once
  when the app starts and then barely changes. Two stores, two concerns — Pinia makes that
  cheap, where a single Redux root reducer tends to push everything into one tree.
*/

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(null)

  /**
   * Whether this deployment can sign anyone in at all.
   *
   * Google OAuth needs credentials that are not in the repository, so a fresh clone cannot
   * offer sign-in. Rather than show a button that would fail, the server tells us and the
   * nav bar leaves it out. Starts false so nothing flashes on screen before we know.
   */
  const available = ref(false)

  /** Null until the first /me call lands, so the nav can avoid flickering between states. */
  const checked = ref(false)

  const signedIn = computed(() => user.value !== null)

  /** Initials for the avatar fallback, when Google gives us no picture. */
  const initials = computed(() => {
    if (!user.value) return ''

    return user.value.displayName
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('')
  })

  async function check() {
    try {
      const me = await api.me()
      user.value = me.user
      available.value = me.signInAvailable
    } catch {
      // A failed /me is not worth surfacing: the app works signed out, so treat it as such.
      user.value = null
      available.value = false
    } finally {
      checked.value = true
    }
  }

  /**
   * Leaves the single-page app entirely.
   *
   * This cannot be a fetch. OAuth is a sequence of real browser navigations — the user has to
   * land on Google's own page, on Google's own origin, and type their password there. An XHR
   * would be blocked, and should be: the whole point is that the password is never typed into
   * a page this application controls.
   */
  function signIn(returnUrl: string) {
    window.location.href = `/api/auth/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`
  }

  async function signOut() {
    try {
      await api.signOut()
    } finally {
      user.value = null
    }
  }

  return { user, available, checked, signedIn, initials, check, signIn, signOut }
})
