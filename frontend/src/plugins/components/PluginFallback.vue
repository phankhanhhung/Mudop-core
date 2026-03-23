<script setup lang="ts">
import { useRouter } from 'vue-router'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { AlertTriangle, ArrowLeft } from 'lucide-vue-next'

const router = useRouter()
</script>

<template>
  <div class="flex items-center justify-center min-h-[50vh]">
    <Card class="border-dashed max-w-md w-full">
      <CardContent class="flex flex-col items-center justify-center py-16">
        <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
          <AlertTriangle class="h-8 w-8 text-muted-foreground" />
        </div>
        <h3 class="text-lg font-semibold mb-1">Plugin Page Not Available</h3>
        <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">
          The requested plugin component could not be loaded.
          Please check that the plugin is properly installed and enabled.
        </p>
        <Button variant="outline" size="sm" @click="router.push('/admin/plugins')">
          <ArrowLeft class="mr-2 h-4 w-4" />
          Go to Plugin Management
        </Button>
      </CardContent>
    </Card>
  </div>
</template>
