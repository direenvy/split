import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

/*
  Six routes.

  `createWebHistory` means real URLs with no hash. That only works because the ASP.NET Core
  host falls back to index.html for any path it does not recognise — see the bottom of
  src/Split.Api/Program.cs. Without it, refreshing on /g/some-slug would ask the server for a
  file that does not exist and get a 404.

  The views are lazy-loaded with a dynamic import, the same idea as React.lazy, except there
  is no Suspense boundary to set up: the router waits for the chunk before it navigates.

  Note that no route is guarded. The only screen that needs an account is /groups, and it
  handles being signed out itself by offering to sign in — which is better than bouncing
  somebody to a login page they did not ask for. The server is where access is actually
  decided, and a router guard is a convenience, never a control.
*/

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/LandingView.vue'),
  },
  {
    path: '/new',
    name: 'new-group',
    component: () => import('@/views/NewGroupView.vue'),
  },
  {
    path: '/groups',
    name: 'my-groups',
    component: () => import('@/views/MyGroupsView.vue'),
  },
  {
    path: '/g/:slug',
    name: 'group',
    component: () => import('@/views/GroupView.vue'),
  },
  {
    path: '/g/:slug/add',
    name: 'add-expense',
    component: () => import('@/views/AddExpenseView.vue'),
  },
  {
    path: '/g/:slug/settle',
    name: 'settle',
    component: () => import('@/views/SettleView.vue'),
  },
  {
    // Anything else goes home rather than showing a blank screen.
    path: '/:pathMatch(.*)*',
    redirect: { name: 'home' },
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,

  // Jump to the top on navigation, except when the link points at an anchor on the page.
  scrollBehavior(to) {
    if (to.hash) {
      return { el: to.hash, behavior: 'smooth' }
    }

    return { top: 0 }
  },
})
