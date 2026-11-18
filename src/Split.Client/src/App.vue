<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
</script>

<template>
  <div class="shell">
    <header class="shell__bar">
      <RouterLink :to="{ name: 'home' }" class="brand">
        <span class="brand__mark" aria-hidden="true"></span>
        <span class="brand__word">Split</span>
      </RouterLink>
    </header>

    <main>
      <!--
        <RouterView> is where the matched route's component renders — React Router's
        <Outlet>. The v-slot form gives access to the component so it can be wrapped; the
        plain form is enough here.
      -->
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  background: var(--void);
}

.shell__bar {
  max-width: var(--page-max);
  margin: 0 auto;
  padding: var(--space-4);
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}

/* The logo mark: the brand gradient, violet into the deep verdant that the token file had
   mis-specified as a five-digit hex value. */
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
  color: var(--bone-white);
}
</style>
