<script setup lang="ts">
import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import EntityRelationshipDiagram from '@/components/entity/EntityRelationshipDiagram.vue'
import { useEntityGraph } from '@/composables/useEntityGraph'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const {
  modules,
  isLoading,
  error,
  selectedModules,
  nodes: graphNodes,
  edges: graphEdges,
  loadAll,
  toggleModule,
  selectAllModules,
} = useEntityGraph()

// Pre-select module from query param
watch(() => route.query.module, (modName) => {
  if (modName && typeof modName === 'string' && modules.value.length > 0) {
    if (!selectedModules.value.includes(modName)) {
      selectedModules.value = [modName]
    }
  }
}, { immediate: true })

// Load all metadata on mount
loadAll()

function clearAllModules() {
  selectedModules.value = []
}

function handleNavigate(entityId: string) {
  // entityId format: 'namespace.EntityName' or 'EntityName'
  // Find which module contains this entity via the graph nodes
  const node = graphNodes.value.find(n => n.id === entityId)
  if (node) {
    router.push(`/odata/${node.moduleName}/${node.name}`)
  }
}
</script>

<template>
  <DefaultLayout>
    <div class="h-full flex flex-col -m-6">
      <!-- Header -->
      <div class="flex items-center gap-3 px-4 py-3 border-b bg-white dark:bg-gray-900">
        <h1 class="text-lg font-semibold text-gray-900 dark:text-gray-100">{{ t('erd.title') }}</h1>
        <div class="flex-1" />
        <!-- Loading indicator -->
        <span v-if="isLoading" class="text-sm text-gray-500">{{ t('common.loading') }}</span>
      </div>

      <!-- Module filter bar -->
      <div class="flex flex-wrap items-center gap-2 px-4 py-2 border-b bg-gray-50 dark:bg-gray-800/50">
        <span class="text-sm text-gray-600 dark:text-gray-400">{{ t('erd.filterByModule') }}:</span>
        <button
          v-for="mod in modules"
          :key="mod.name"
          @click="toggleModule(mod.name)"
          :class="[
            'px-3 py-1 text-xs rounded-full font-medium transition-colors',
            selectedModules.includes(mod.name)
              ? 'bg-blue-600 text-white'
              : 'bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600'
          ]"
        >
          {{ mod.name }}
        </button>
        <button @click="selectAllModules" class="text-xs text-blue-600 hover:underline ml-2">{{ t('erd.selectAll') }}</button>
        <button @click="clearAllModules" class="text-xs text-gray-500 hover:underline">{{ t('erd.clearAll') }}</button>
      </div>

      <!-- Legend -->
      <div class="flex items-center gap-4 px-4 py-1.5 border-b text-xs text-gray-500 dark:text-gray-400 bg-white dark:bg-gray-900">
        <div class="flex items-center gap-1.5">
          <svg width="28" height="4"><line x1="0" y1="2" x2="28" y2="2" stroke="currentColor" stroke-width="2"/></svg>
          {{ t('erd.composition') }}
        </div>
        <div class="flex items-center gap-1.5">
          <svg width="28" height="4"><line x1="0" y1="2" x2="28" y2="2" stroke="currentColor" stroke-width="2" stroke-dasharray="5 3"/></svg>
          {{ t('erd.association') }}
        </div>
        <div class="flex items-center gap-1.5">
          <span class="inline-block w-3 h-3 border-2 border-dashed border-gray-400 rounded"></span>
          {{ t('erd.abstractEntity') }}
        </div>
      </div>

      <!-- ERD canvas -->
      <div class="flex-1 overflow-hidden">
        <div v-if="error" class="flex items-center justify-center h-full text-red-500 text-sm">
          {{ error }}
        </div>
        <EntityRelationshipDiagram
          v-else
          :input-nodes="graphNodes"
          :input-edges="graphEdges"
          @navigate="handleNavigate"
        />
      </div>
    </div>
  </DefaultLayout>
</template>
