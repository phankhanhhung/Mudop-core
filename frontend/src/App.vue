<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'
import { computed } from 'vue'
import { ToastContainer, LoadingOverlay } from '@/components/common'
import NotificationProvider from '@/components/common/NotificationProvider.vue'
import OfflineBanner from '@/components/common/OfflineBanner.vue'
import PwaUpdateBanner from '@/components/common/PwaUpdateBanner.vue'

const route = useRoute()
// Force re-creation of entity views when route params change (module/entity/id).
// Use route.path (not fullPath) so query-only changes (search, sort, pagination)
// don't destroy/recreate the entire view and its local state.
const routeKey = computed(() => route.path)
</script>

<template>
  <PwaUpdateBanner />
  <OfflineBanner />
  <NotificationProvider>
    <RouterView :key="routeKey" />
  </NotificationProvider>
  <ToastContainer />
  <LoadingOverlay />
</template>
