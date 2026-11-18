<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

/*
  The top bar. Two interesting bits:

  - It is rendered on every page, so it has to be honest about three auth states, not two:
    signed in, signed out, and "sign-in is not configured on this deployment at all". The
    third is why `auth.available` exists.

  - `useRoute()` is reactive. Reading `route.path` inside a `computed` means the bar updates
    on navigation without anyone telling it to.
*/

const auth = useAuthStore()
const route = useRoute()

const menuOpen = ref(false)

/** Come back to wherever the user was once Google is done with them. */
const returnUrl = computed(() => route.fullPath)

function signIn() {
  auth.signIn(returnUrl.value)
}

async function signOut() {
  menuOpen.value = false
  await auth.signOut()
}
</script>

<template>
  <header class="bar">
    <nav class="bar__inner">
      <RouterLink :to="{ name: 'home' }" class="brand" aria-label="Split, home">
        <span class="brand__mark" aria-hidden="true"></span>
        <span class="brand__word">Split</span>
      </RouterLink>

      <div class="links">
        <!--
          RouterLink sets an `active` class automatically on the link whose route is current,
          which is React Router's NavLink behaviour without needing the render-prop form.
        -->
        <RouterLink :to="{ name: 'new-group' }" class="links__item">New group</RouterLink>

        <RouterLink v-if="auth.signedIn" :to="{ name: 'my-groups' }" class="links__item">
          My groups
        </RouterLink>
      </div>

      <div class="account">
        <!-- Nothing at all until the first /me call lands, so the bar cannot flicker
             between "Sign in" and an avatar on every page load. -->
        <template v-if="auth.checked">
          <div v-if="auth.signedIn" class="account__menu">
            <button
              type="button"
              class="avatar"
              :aria-expanded="menuOpen"
              aria-haspopup="menu"
              :title="auth.user?.email"
              @click="menuOpen = !menuOpen"
            >
              <img
                v-if="auth.user?.avatarUrl"
                :src="auth.user.avatarUrl"
                :alt="auth.user.displayName"
                class="avatar__img"
                referrerpolicy="no-referrer"
              />
              <span v-else class="avatar__initials">{{ auth.initials }}</span>
            </button>

            <div v-if="menuOpen" class="menu" role="menu">
              <p class="menu__who">
                <span class="menu__name">{{ auth.user?.displayName }}</span>
                <span class="menu__email">{{ auth.user?.email }}</span>
              </p>
              <button type="button" class="menu__item" role="menuitem" @click="signOut">
                Sign out
              </button>
            </div>
          </div>

          <button v-else-if="auth.available" type="button" class="signin" @click="signIn">
            Sign in
          </button>
        </template>
      </div>
    </nav>
  </header>
</template>

<style scoped>
.bar {
  position: sticky;
  top: 0;
  z-index: 10;

  /* Blurred rather than solid, so the void still shows through as the page moves under it. */
  background: rgba(0, 0, 0, 0.72);
  backdrop-filter: blur(12px);
  border-bottom: 1px solid var(--hairline);
}

.bar__inner {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: var(--space-2) var(--space-4);

  display: flex;
  align-items: center;
  gap: var(--space-4);
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  flex-shrink: 0;
}

.brand__mark {
  width: 22px;
  height: 22px;
  border-radius: var(--radius-full);
  background: linear-gradient(135deg, var(--electric-iris), var(--deep-verdant-tint));
}

.brand__word {
  font-size: var(--text-sm);
  font-weight: var(--weight-semibold);
  letter-spacing: var(--tracking-nav);
  text-transform: uppercase;
}

.links {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex: 1;
}

.links__item {
  font-size: var(--text-xs);
  font-weight: var(--weight-semibold);
  letter-spacing: var(--tracking-label);
  text-transform: uppercase;
  color: var(--ash-gray);
  padding: var(--space-1) 0;
  transition: color 120ms ease;
}

.links__item:hover {
  color: var(--bone-white);
}

/* vue-router adds this class to the link matching the current route. */
.links__item.router-link-active {
  color: var(--bone-white);
}

.account {
  display: flex;
  align-items: center;
  min-height: 36px;
  flex-shrink: 0;
}

.signin {
  font-family: inherit;
  font-size: var(--text-xs);
  font-weight: var(--weight-semibold);
  letter-spacing: var(--tracking-label);
  text-transform: uppercase;

  background: var(--surface-raised-2);
  color: var(--bone-white);
  border: none;
  border-radius: var(--radius-full);
  padding: var(--space-1) var(--space-3);
  min-height: 36px;
  cursor: pointer;
  transition: background-color 120ms ease;
}

.signin:hover {
  background: var(--surface-hover);
}

.account__menu {
  position: relative;
}

.avatar {
  width: 36px;
  height: 36px;
  border-radius: var(--radius-full);
  border: 1px solid var(--hairline);
  background: var(--surface-raised-2);
  padding: 0;
  overflow: hidden;
  cursor: pointer;

  display: grid;
  place-items: center;
}

.avatar__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.avatar__initials {
  font-size: var(--text-xs);
  font-weight: var(--weight-semibold);
  color: var(--bone-white);
}

.menu {
  position: absolute;
  right: 0;
  top: calc(100% + var(--space-1));
  min-width: 200px;

  background: var(--surface-raised-2);
  border: 1px solid var(--hairline);
  border-radius: var(--radius-md);
  padding: var(--space-2);
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.menu__who {
  display: flex;
  flex-direction: column;
  padding: var(--space-1) var(--space-2) var(--space-2);
  border-bottom: 1px solid var(--hairline);
}

.menu__name {
  font-size: var(--text-base);
}

.menu__email {
  font-size: var(--text-xs);
  color: var(--ash-gray);
  overflow-wrap: anywhere;
}

.menu__item {
  font-family: inherit;
  font-size: var(--text-base);
  text-align: left;
  background: none;
  border: none;
  color: var(--bone-white);
  padding: var(--space-2);
  border-radius: var(--radius-sm);
  cursor: pointer;
}

.menu__item:hover {
  background: var(--surface-hover);
}

@media (max-width: 480px) {
  .bar__inner {
    gap: var(--space-2);
  }

  .links {
    gap: var(--space-2);
  }
}
</style>
