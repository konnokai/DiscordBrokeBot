import { createRouter, createWebHistory } from 'vue-router'
import { auth, refreshAuth } from '@/composables/auth'
import BlocksPage from '@/pages/BlocksPage.vue'
import CallbackPage from '@/pages/CallbackPage.vue'
import InvitePage from '@/pages/InvitePage.vue'
import LegalPage from '@/pages/LegalPage.vue'
import LoginPage from '@/pages/LoginPage.vue'
import OrderDetailPage from '@/pages/OrderDetailPage.vue'
import OrdersPage from '@/pages/OrdersPage.vue'

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: () => ({ top: 0 }),
  routes: [
    { path: '/', redirect: '/orders/buying' },
    { path: '/login', name: 'login', component: LoginPage, meta: { public: true } },
    { path: '/invite', name: 'invite', component: InvitePage, meta: { public: true } },
    {
      path: '/privacy',
      name: 'privacy',
      component: LegalPage,
      meta: { public: true, title: '隱私政策' },
    },
    { path: '/tos', name: 'tos', component: LegalPage, meta: { public: true, title: '服務條款' } },
    {
      path: '/oauth/callback',
      alias: '/auth/callback',
      name: 'callback',
      component: CallbackPage,
      meta: { public: true },
    },
    { path: '/orders/buying', name: 'buying', component: OrdersPage },
    { path: '/orders/requested', name: 'requested', component: OrdersPage },
    { path: '/orders/archived', name: 'archived', component: OrdersPage },
    { path: '/orders/:id', name: 'order-detail', component: OrderDetailPage, props: true },
    { path: '/orders/:id/edit', name: 'order-edit', component: OrderDetailPage, props: true },
    { path: '/blocks', name: 'blocks', component: BlocksPage },
    { path: '/:pathMatch(.*)*', redirect: '/login' },
  ],
})

router.beforeEach(async (to) => {
  if (to.meta.public) return true
  await refreshAuth()
  return auth.user ? true : { name: 'login', query: { redirect: to.fullPath } }
})

export default router
