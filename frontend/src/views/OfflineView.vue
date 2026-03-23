<script setup lang="ts">
import { WifiOff } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
</script>

<template>
  <div class="min-h-screen flex flex-col items-center justify-center gap-6 bg-background px-4 text-center">
    <div class="rounded-full bg-muted p-6">
      <WifiOff class="h-12 w-12 text-muted-foreground" />
    </div>
    <div class="space-y-2">
      <h1 class="text-2xl font-semibold tracking-tight">You're offline</h1>
      <p class="text-muted-foreground max-w-sm">
        Check your internet connection and try again. Some previously visited pages may still be available.
      </p>
    </div>
    <Button @click="() => window.location.reload()">Try again</Button>
  </div>
</template>
