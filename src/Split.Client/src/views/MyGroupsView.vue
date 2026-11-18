<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import AppButton from '@/components/AppButton.vue'
import StateNote from '@/components/StateNote.vue'
import { api, ApiError } from '@/api'
import { useAuthStore } from '@/stores/auth'
import type { GroupSummary } from '@/types'

/*
  The one screen that needs an account: the groups you have made or opened.

  Everything else in Split works signed out, so this view has to handle three states rather
  than the usual two — signed in, signed out, and "this deployment has no sign-in at all"
  because nobody configured Google credentials for it.
*/

const auth = useAuthStore()
const route = useRoute()

const groups = ref<GroupSummary[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

const money = new Intl.NumberFormat('en-MY', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })

function lastSeen(iso: string): string {
  const days = Math.round((Date.now() - new Date(iso).getTime()) / 86_400_000)
  if (days < 1) return 'today'
  return relative.format(-days, 'day')
}

const hasGroups = computed(() => groups.value.length > 0)

async function load() {
  if (!auth.signedIn) {
    groups.value = []
    return
  }

  loading.value = true
  error.value = null

  try {
    groups.value = await api.myGroups()
  } catch (caught) {
    error.value = caught instanceof ApiError ? caught.message : 'Could not load your groups.'
  } finally {
    loading.value = false
  }
}

// The nav lands here before /api/auth/me has necessarily answered, so wait for the auth
// store rather than firing a request that would just 401. Watching a getter over another
// store's state is ordinary - reactivity crosses store boundaries without ceremony.
watch(() => auth.signedIn, load)
onMounted(load)
</script>

<template>
  <div class="page">
    <h1 class="title">My groups</h1>

    <!-- Not signed in, but could be. -->
    <template v-if="auth.checked && !auth.signedIn && auth.available">
      <p class="prose intro">
        Sign in and Split remembers every group you make or open, so you do not have to keep
        the links yourself. It changes nothing else — groups stay reachable by their link
        either way.
      </p>
      <AppButton variant="primary" @click="auth.signIn(route.fullPath)">
        Sign in with Google
      </AppButton>
    </template>

    <!-- Nobody configured Google credentials on this deployment. Say so plainly rather than
         showing a button that cannot work. -->
    <StateNote v-else-if="auth.checked && !auth.available">
      Sign-in is not set up on this deployment, so there is no history to show. Everything
      else works without an account — your group links are the only thing you need to keep.
    </StateNote>

    <StateNote v-else-if="loading">Loading your groups...</StateNote>
    <StateNote v-else-if="error" tone="error">{{ error }}</StateNote>

    <StateNote v-else-if="auth.signedIn && !hasGroups">
      Nothing here yet. Make a group, or open one someone shared with you, and it will show
      up in this list.
    </StateNote>

    <ul v-else-if="hasGroups" class="list">
      <li v-for="group in groups" :key="group.slug">
        <RouterLink :to="{ name: 'group', params: { slug: group.slug } }" class="card">
          <div class="card__main">
            <p class="card__name">{{ group.name }}</p>
            <p class="card__meta muted">
              {{ group.people }} people &middot; {{ group.currency }}
              {{ money.format(group.totalSpend) }} &middot; opened {{ lastSeen(group.lastSeenAt) }}
            </p>
          </div>

          <span v-if="group.isCreator" class="card__badge label">Yours</span>
        </RouterLink>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.title {
  font-size: var(--text-h1);
  padding-top: var(--space-4);
}

.intro {
  margin: var(--space-3) 0 var(--space-5);
  max-width: 54ch;
}

.list {
  list-style: none;
  margin: var(--space-5) 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);

  background: var(--surface-raised);
  border-radius: var(--radius-lg);
  padding: var(--space-4);
  transition: background-color 120ms ease;
}

.card:hover {
  background: var(--surface-raised-2);
}

.card__name {
  font-size: var(--text-body);
  font-weight: var(--weight-thin);
}

.card__meta {
  font-size: var(--text-xs);
  margin-top: 2px;
}

.card__badge {
  color: var(--electric-iris);
  flex-shrink: 0;
}
</style>
