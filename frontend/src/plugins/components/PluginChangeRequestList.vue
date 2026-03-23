<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { GitPullRequestDraft } from 'lucide-vue-next'
</script>
<template>
  <DefaultLayout>
    <Card>
      <CardHeader>
        <CardTitle class="flex items-center gap-2">
          <GitPullRequestDraft class="h-5 w-5" />
          Change Requests
        </CardTitle>
      </CardHeader>
      <CardContent>
        <p class="text-muted-foreground">
          Change requests are available on each entity detail page.
        </p>
      </CardContent>
    </Card>
  </DefaultLayout>
</template>
