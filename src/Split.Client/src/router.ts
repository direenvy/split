import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

/*
  Four routes, which is the whole app.

  `createWebHistory` means real URLs with no hash. That only works because the ASP.NET Core
  host falls back to index.html for any path it does not recognise — see the bottom of
  src/Split.Api/Program.cs. Without that, refreshing the page on /g/some-slug would ask the
  server for a file that does not exist and get a 404.

  The views are lazy-loaded with a dynamic import, the same idea as React.lazy, except there
  is no Suspense boundary to set up: the router waits for the chunk before it navigates.
*/

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/HomeView.vue'),
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
  scrollBehavior: () => ({ top: 0 }),
})
