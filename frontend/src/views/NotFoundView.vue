<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Home, ArrowLeft } from 'lucide-vue-next'

const { t } = useI18n()
const router = useRouter()

function goHome() {
  router.push('/')
}

function goBack() {
  router.back()
}
</script>

<template>
  <div class="min-h-screen flex items-center justify-center bg-background">
    <div class="text-center space-y-6 p-4">
      <div class="space-y-2">
        <h1 class="text-9xl font-bold text-muted-foreground/30">404</h1>
        <h2 class="text-2xl font-semibold">{{ t('notFound.title') }}</h2>
        <p class="text-muted-foreground max-w-md mx-auto">
          {{ t('notFound.description') }}
        </p>
      </div>

      <div class="flex items-center justify-center gap-4">
        <Button variant="outline" @click="goBack">
          <ArrowLeft class="mr-2 h-4 w-4" />
          {{ t('notFound.goBack') }}
        </Button>
        <Button @click="goHome">
          <Home class="mr-2 h-4 w-4" />
          {{ t('notFound.home') }}
        </Button>
      </div>
    </div>
  </div>
</template>
