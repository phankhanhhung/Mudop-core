<script setup lang="ts">
import { ref, computed } from 'vue'
import { useMetadataStore } from '@/stores/metadata'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Filter, X } from 'lucide-vue-next'
import type { AuditLogFilter } from '@/types/audit'

const emit = defineEmits<{
  'filter-change': [filter: AuditLogFilter]
}>()

const metadataStore = useMetadataStore()

const entityName = ref('')
const eventType = ref('')
const dateFrom = ref('')
const dateTo = ref('')

const entityNames = computed(() => {
  const names: string[] = []
  for (const module of metadataStore.modules) {
    for (const service of module.services) {
      for (const entity of service.entities) {
        names.push(entity.entityType)
      }
    }
  }
  return names.sort()
})

function applyFilter() {
  const filter: AuditLogFilter = {}
  if (entityName.value) filter.entityName = entityName.value
  if (eventType.value) filter.eventType = eventType.value
  if (dateFrom.value) filter.from = new Date(dateFrom.value).toISOString()
  if (dateTo.value) filter.to = new Date(dateTo.value).toISOString()
  emit('filter-change', filter)
}

function clearFilter() {
  entityName.value = ''
  eventType.value = ''
  dateFrom.value = ''
  dateTo.value = ''
  emit('filter-change', {})
}
</script>

<template>
  <Card>
    <CardContent class="pt-6">
      <div class="flex flex-wrap items-end gap-4">
        <div class="flex flex-col gap-1.5">
          <Label class="text-xs text-muted-foreground">Entity</Label>
          <Select v-model="entityName" class="w-48">
            <option value="">All Entities</option>
            <option v-for="name in entityNames" :key="name" :value="name">{{ name }}</option>
          </Select>
        </div>

        <div class="flex flex-col gap-1.5">
          <Label class="text-xs text-muted-foreground">Event Type</Label>
          <Select v-model="eventType" class="w-40">
            <option value="">All Events</option>
            <option value="Created">Created</option>
            <option value="Updated">Updated</option>
            <option value="Deleted">Deleted</option>
          </Select>
        </div>

        <div class="flex flex-col gap-1.5">
          <Label class="text-xs text-muted-foreground">From</Label>
          <Input v-model="dateFrom" type="datetime-local" class="w-48" />
        </div>

        <div class="flex flex-col gap-1.5">
          <Label class="text-xs text-muted-foreground">To</Label>
          <Input v-model="dateTo" type="datetime-local" class="w-48" />
        </div>

        <div class="flex gap-2">
          <Button size="sm" @click="applyFilter">
            <Filter class="mr-1.5 h-4 w-4" />
            Apply
          </Button>
          <Button size="sm" variant="outline" @click="clearFilter">
            <X class="mr-1.5 h-4 w-4" />
            Clear
          </Button>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
