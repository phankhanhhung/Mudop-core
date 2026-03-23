<script setup lang="ts">
import { computed } from 'vue'
import type { FieldMetadata } from '@/types/metadata'
import { formatValue } from '@/utils/formValidator'

interface Props {
  version1: Record<string, unknown>
  version2: Record<string, unknown>
  fields: FieldMetadata[]
}

const props = defineProps<Props>()

type ChangeType = 'added' | 'removed' | 'modified' | 'unchanged'

interface DiffRow {
  fieldName: string
  displayName: string
  value1: string
  value2: string
  changeType: ChangeType
}

const diffRows = computed<DiffRow[]>(() => {
  // Filter out system/temporal fields from display
  const systemFields = new Set([
    'SystemStart', 'system_start',
    'SystemEnd', 'system_end',
    'Version', 'version',
    '@odata.etag', '@odata.context'
  ])

  return props.fields
    .filter((f) => !systemFields.has(f.name))
    .map((field) => {
      const val1 = props.version1[field.name]
      const val2 = props.version2[field.name]
      const str1 = formatValue(val1, field.type, field.enumValues)
      const str2 = formatValue(val2, field.type, field.enumValues)

      let changeType: ChangeType = 'unchanged'
      if (str1 !== str2) {
        if (!val1 && val2) changeType = 'added'
        else if (val1 && !val2) changeType = 'removed'
        else changeType = 'modified'
      }

      return {
        fieldName: field.name,
        displayName: field.displayName || field.name,
        value1: str1,
        value2: str2,
        changeType
      }
    })
})

const changedCount = computed(() => diffRows.value.filter((r) => r.changeType !== 'unchanged').length)

function changeClass(type: ChangeType): string {
  switch (type) {
    case 'added':
      return 'bg-green-50 dark:bg-green-950/30'
    case 'removed':
      return 'bg-red-50 dark:bg-red-950/30'
    case 'modified':
      return 'bg-yellow-50 dark:bg-yellow-950/30'
    default:
      return ''
  }
}

function indicatorClass(type: ChangeType): string {
  switch (type) {
    case 'added':
      return 'text-green-600 dark:text-green-400'
    case 'removed':
      return 'text-red-600 dark:text-red-400'
    case 'modified':
      return 'text-yellow-600 dark:text-yellow-400'
    default:
      return 'text-muted-foreground'
  }
}

function indicatorLabel(type: ChangeType): string {
  switch (type) {
    case 'added':
      return 'Added'
    case 'removed':
      return 'Removed'
    case 'modified':
      return 'Changed'
    default:
      return '-'
  }
}
</script>

<template>
  <div>
    <p class="text-xs text-muted-foreground mb-3">
      {{ changedCount }} field{{ changedCount !== 1 ? 's' : '' }} changed
    </p>

    <div class="border rounded-lg overflow-hidden">
      <table class="w-full text-sm">
        <thead>
          <tr class="border-b bg-muted/50">
            <th class="text-left p-2 font-medium text-muted-foreground w-1/5">Field</th>
            <th class="text-left p-2 font-medium text-muted-foreground w-1/3">Version 1</th>
            <th class="text-left p-2 font-medium text-muted-foreground w-1/3">Version 2</th>
            <th class="text-center p-2 font-medium text-muted-foreground w-20">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="row in diffRows"
            :key="row.fieldName"
            class="border-b last:border-b-0"
            :class="changeClass(row.changeType)"
          >
            <td class="p-2 font-medium text-xs">
              {{ row.displayName }}
            </td>
            <td class="p-2 text-xs break-all">
              <span :class="row.changeType === 'modified' || row.changeType === 'removed' ? 'font-medium' : ''">
                {{ row.value1 || '(empty)' }}
              </span>
            </td>
            <td class="p-2 text-xs break-all">
              <span :class="row.changeType === 'modified' || row.changeType === 'added' ? 'font-medium' : ''">
                {{ row.value2 || '(empty)' }}
              </span>
            </td>
            <td class="p-2 text-center">
              <span class="text-xs font-medium" :class="indicatorClass(row.changeType)">
                {{ indicatorLabel(row.changeType) }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
