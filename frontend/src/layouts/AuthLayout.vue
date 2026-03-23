<script setup lang="ts">
import ThemeToggle from '@/components/common/ThemeToggle.vue'
</script>

<template>
  <div class="min-h-screen flex flex-col bg-background">
    <!-- Header -->
    <header class="border-b">
      <div class="container mx-auto px-4 h-16 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="text-xl font-bold">BMMDL</span>
        </div>
        <ThemeToggle />
      </div>
    </header>

    <!-- Main content -->
    <main class="flex-1 flex items-center justify-center p-4">
      <div class="w-full max-w-md">
        <slot />
      </div>
    </main>

    <!-- Footer -->
    <footer class="border-t py-4">
      <div class="container mx-auto px-4 text-center text-sm text-muted-foreground">
        BMMDL Platform
      </div>
    </footer>
  </div>
</template>
