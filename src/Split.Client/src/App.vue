<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterView } from 'vue-router'
import NavBar from '@/components/NavBar.vue'
import { useAuthStore } from '@/stores/auth'

/*
  The shell: a nav bar and whatever route is current.

  `onMounted` is the one lifecycle hook this app uses, and it is the closest thing to
  `useEffect(() => {...}, [])`. It runs once, after the component is in the DOM. Finding out
  who is signed in belongs here rather than in main.ts, because it should not delay the first
  paint — the page renders signed-out and the nav fills in when the answer arrives.
*/

const auth = useAuthStore()

onMounted(() => {
  void auth.check()
})
</script>

<template>
  <div class="shell">
    <NavBar />

    <main>
      <!-- React Router's <Outlet>. -->
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  background: var(--void);
}
</style>
