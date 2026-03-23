<script setup lang="ts">
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Spinner } from '@/components/ui/spinner'
import {
  LayoutDashboard,
  Plus,
  RotateCcw,
  Save,
  Check
} from 'lucide-vue-next'

defineProps<{
  editMode: boolean
  isDirty: boolean
  isSaving: boolean
  columns: 3 | 4
}>()

const emit = defineEmits<{
  'update:editMode': [val: boolean]
  'add-widget': []
  'save': []
  'reset': []
  'set-columns': [columns: 3 | 4]
}>()
</script>

<template>
  <!-- View mode: single Customize button -->
  <template v-if="!editMode">
    <Button variant="outline" size="sm" @click="emit('update:editMode', true)">
      <LayoutDashboard class="mr-1.5 h-4 w-4" />
      {{ $t('dashboard.builder.customize') }}
    </Button>
  </template>

  <!-- Edit mode: full toolbar -->
  <template v-else>
    <Card class="border-primary/30 bg-primary/5">
      <CardContent class="flex flex-wrap items-center gap-3 py-3 px-4">
        <!-- Status label -->
        <span class="text-sm font-medium text-primary">
          {{ $t('dashboard.builder.editingMode') }}
        </span>

        <!-- Columns selector -->
        <div class="flex items-center gap-2">
          <span class="text-xs text-muted-foreground">
            {{ $t('dashboard.builder.columns') }}:
          </span>
          <div class="flex rounded-md border overflow-hidden">
            <button
              v-for="c in [3, 4]"
              :key="c"
              type="button"
              class="px-2.5 py-1 text-xs transition-colors"
              :class="columns === c ? 'bg-primary text-primary-foreground' : 'hover:bg-muted'"
              @click="emit('set-columns', c as 3 | 4)"
            >
              {{ c }}
            </button>
          </div>
        </div>

        <!-- Spacer -->
        <div class="flex-1" />

        <!-- Add Widget -->
        <Button variant="outline" size="sm" @click="emit('add-widget')">
          <Plus class="mr-1.5 h-4 w-4" />
          {{ $t('dashboard.builder.addWidget') }}
        </Button>

        <!-- Reset -->
        <Button variant="ghost" size="sm" @click="emit('reset')">
          <RotateCcw class="mr-1.5 h-4 w-4" />
          {{ $t('dashboard.builder.reset') }}
        </Button>

        <!-- Save -->
        <Button size="sm" :disabled="!isDirty || isSaving" @click="emit('save')">
          <Spinner v-if="isSaving" class="mr-1.5 h-4 w-4" />
          <Save v-else class="mr-1.5 h-4 w-4" />
          {{ isSaving ? $t('dashboard.builder.saving') : $t('dashboard.builder.save') }}
        </Button>

        <!-- Done -->
        <Button variant="ghost" size="sm" @click="emit('update:editMode', false)">
          <Check class="mr-1.5 h-4 w-4" />
          {{ $t('dashboard.builder.done') }}
        </Button>
      </CardContent>
    </Card>
  </template>
</template>
