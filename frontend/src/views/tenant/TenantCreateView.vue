<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'

// TenantCreateView is now a dialog in TenantListView.
// This redirect ensures any bookmarks or links to /tenants/create still work.
const router = useRouter()

onMounted(() => {
  router.replace('/tenants')
})
</script>

<template>
  <div class="flex items-center justify-center min-h-screen">
    <p class="text-muted-foreground">Redirecting...</p>
  </div>
</template>
