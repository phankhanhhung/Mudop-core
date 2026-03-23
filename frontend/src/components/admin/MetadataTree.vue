<script setup lang="ts">
import { ref } from 'vue'
import type { ModuleMetadata, ServiceMetadata, EntitySetMetadata, ActionMetadata, FunctionMetadata } from '@/types/metadata'
import { Badge } from '@/components/ui/badge'
import {
  ChevronRight,
  ChevronDown,
  Database,
  Zap,
  Code,
  Package,
  Server
} from 'lucide-vue-next'

interface SelectedNode {
  type: 'module' | 'service' | 'entity' | 'action' | 'function'
  data: ModuleMetadata | ServiceMetadata | EntitySetMetadata | ActionMetadata | FunctionMetadata
  moduleName: string
  serviceName?: string
}

defineProps<{
  modules: ModuleMetadata[]
}>()

const emit = defineEmits<{
  select: [node: SelectedNode]
}>()

const selectedKey = ref<string | null>(null)
const expandedNodes = ref<Set<string>>(new Set())

function toggleNode(key: string) {
  if (expandedNodes.value.has(key)) {
    expandedNodes.value.delete(key)
  } else {
    expandedNodes.value.add(key)
  }
}

function isExpanded(key: string): boolean {
  return expandedNodes.value.has(key)
}

function isSelected(key: string): boolean {
  return selectedKey.value === key
}

function selectModule(mod: ModuleMetadata) {
  const key = `module:${mod.name}`
  selectedKey.value = key
  emit('select', { type: 'module', data: mod, moduleName: mod.name })
}

function selectService(mod: ModuleMetadata, service: ServiceMetadata) {
  const key = `service:${mod.name}:${service.name}`
  selectedKey.value = key
  emit('select', { type: 'service', data: service, moduleName: mod.name, serviceName: service.name })
}

function selectEntity(mod: ModuleMetadata, service: ServiceMetadata, entity: EntitySetMetadata) {
  const key = `entity:${mod.name}:${service.name}:${entity.name}`
  selectedKey.value = key
  emit('select', { type: 'entity', data: entity, moduleName: mod.name, serviceName: service.name })
}

function selectAction(mod: ModuleMetadata, service: ServiceMetadata, action: ActionMetadata) {
  const key = `action:${mod.name}:${service.name}:${action.name}`
  selectedKey.value = key
  emit('select', { type: 'action', data: action, moduleName: mod.name, serviceName: service.name })
}

function selectFunction(mod: ModuleMetadata, service: ServiceMetadata, fn: FunctionMetadata) {
  const key = `function:${mod.name}:${service.name}:${fn.name}`
  selectedKey.value = key
  emit('select', { type: 'function', data: fn, moduleName: mod.name, serviceName: service.name })
}
</script>

<template>
  <div class="text-sm">
    <div v-for="mod in modules" :key="mod.name" class="mb-1">
      <!-- Module node -->
      <div
        class="flex items-center gap-1 py-1.5 px-2 rounded-md cursor-pointer hover:bg-muted transition-colors"
        :class="{ 'bg-primary/10 text-primary': isSelected(`module:${mod.name}`) }"
        @click="selectModule(mod)"
      >
        <button
          class="p-0.5 hover:bg-muted-foreground/10 rounded shrink-0"
          @click.stop="toggleNode(`module:${mod.name}`)"
        >
          <ChevronDown v-if="isExpanded(`module:${mod.name}`)" class="h-3.5 w-3.5" />
          <ChevronRight v-else class="h-3.5 w-3.5" />
        </button>
        <Package class="h-4 w-4 shrink-0 text-muted-foreground" />
        <span class="font-medium truncate">{{ mod.name }}</span>
        <Badge variant="outline" class="ml-auto text-xs scale-90">v{{ mod.version }}</Badge>
      </div>

      <!-- Module children -->
      <div v-if="isExpanded(`module:${mod.name}`)" class="pl-4">
        <div v-for="service in mod.services" :key="service.name" class="mb-1">
          <!-- Service node -->
          <div
            class="flex items-center gap-1 py-1.5 px-2 rounded-md cursor-pointer hover:bg-muted transition-colors"
            :class="{ 'bg-primary/10 text-primary': isSelected(`service:${mod.name}:${service.name}`) }"
            @click="selectService(mod, service)"
          >
            <button
              class="p-0.5 hover:bg-muted-foreground/10 rounded shrink-0"
              @click.stop="toggleNode(`service:${mod.name}:${service.name}`)"
            >
              <ChevronDown v-if="isExpanded(`service:${mod.name}:${service.name}`)" class="h-3.5 w-3.5" />
              <ChevronRight v-else class="h-3.5 w-3.5" />
            </button>
            <Server class="h-4 w-4 shrink-0 text-muted-foreground" />
            <span class="font-medium truncate">{{ service.name }}</span>
          </div>

          <!-- Service children -->
          <div v-if="isExpanded(`service:${mod.name}:${service.name}`)" class="pl-4">
            <!-- Entities category -->
            <div v-if="service.entities.length > 0" class="mb-1">
              <div
                class="flex items-center gap-1 py-1 px-2 text-muted-foreground cursor-pointer hover:bg-muted rounded-md transition-colors"
                @click="toggleNode(`entities:${mod.name}:${service.name}`)"
              >
                <button class="p-0.5 shrink-0">
                  <ChevronDown v-if="isExpanded(`entities:${mod.name}:${service.name}`)" class="h-3 w-3" />
                  <ChevronRight v-else class="h-3 w-3" />
                </button>
                <span class="text-xs font-semibold uppercase tracking-wider">Entities</span>
                <Badge variant="secondary" class="ml-auto text-xs scale-85">{{ service.entities.length }}</Badge>
              </div>

              <div v-if="isExpanded(`entities:${mod.name}:${service.name}`)" class="pl-4">
                <div
                  v-for="entity in service.entities"
                  :key="entity.name"
                  class="flex items-center gap-1.5 py-1 px-2 rounded-md cursor-pointer hover:bg-muted transition-colors"
                  :class="{ 'bg-primary/10 text-primary': isSelected(`entity:${mod.name}:${service.name}:${entity.name}`) }"
                  @click="selectEntity(mod, service, entity)"
                >
                  <Database class="h-3.5 w-3.5 shrink-0" />
                  <span class="truncate">{{ entity.name }}</span>
                </div>
              </div>
            </div>

            <!-- Actions category -->
            <div v-if="service.actions.length > 0" class="mb-1">
              <div
                class="flex items-center gap-1 py-1 px-2 text-muted-foreground cursor-pointer hover:bg-muted rounded-md transition-colors"
                @click="toggleNode(`actions:${mod.name}:${service.name}`)"
              >
                <button class="p-0.5 shrink-0">
                  <ChevronDown v-if="isExpanded(`actions:${mod.name}:${service.name}`)" class="h-3 w-3" />
                  <ChevronRight v-else class="h-3 w-3" />
                </button>
                <span class="text-xs font-semibold uppercase tracking-wider">Actions</span>
                <Badge variant="secondary" class="ml-auto text-xs scale-85">{{ service.actions.length }}</Badge>
              </div>

              <div v-if="isExpanded(`actions:${mod.name}:${service.name}`)" class="pl-4">
                <div
                  v-for="action in service.actions"
                  :key="action.name"
                  class="flex items-center gap-1.5 py-1 px-2 rounded-md cursor-pointer hover:bg-muted transition-colors"
                  :class="{ 'bg-primary/10 text-primary': isSelected(`action:${mod.name}:${service.name}:${action.name}`) }"
                  @click="selectAction(mod, service, action)"
                >
                  <Zap class="h-3.5 w-3.5 shrink-0" />
                  <span class="truncate">{{ action.name }}</span>
                </div>
              </div>
            </div>

            <!-- Functions category -->
            <div v-if="service.functions.length > 0" class="mb-1">
              <div
                class="flex items-center gap-1 py-1 px-2 text-muted-foreground cursor-pointer hover:bg-muted rounded-md transition-colors"
                @click="toggleNode(`functions:${mod.name}:${service.name}`)"
              >
                <button class="p-0.5 shrink-0">
                  <ChevronDown v-if="isExpanded(`functions:${mod.name}:${service.name}`)" class="h-3 w-3" />
                  <ChevronRight v-else class="h-3 w-3" />
                </button>
                <span class="text-xs font-semibold uppercase tracking-wider">Functions</span>
                <Badge variant="secondary" class="ml-auto text-xs scale-85">{{ service.functions.length }}</Badge>
              </div>

              <div v-if="isExpanded(`functions:${mod.name}:${service.name}`)" class="pl-4">
                <div
                  v-for="fn in service.functions"
                  :key="fn.name"
                  class="flex items-center gap-1.5 py-1 px-2 rounded-md cursor-pointer hover:bg-muted transition-colors"
                  :class="{ 'bg-primary/10 text-primary': isSelected(`function:${mod.name}:${service.name}:${fn.name}`) }"
                  @click="selectFunction(mod, service, fn)"
                >
                  <Code class="h-3.5 w-3.5 shrink-0" />
                  <span class="truncate">{{ fn.name }}</span>
                </div>
              </div>
            </div>

            <!-- Empty state for service with no children -->
            <div
              v-if="service.entities.length === 0 && service.actions.length === 0 && service.functions.length === 0"
              class="py-2 px-2 text-xs text-muted-foreground italic"
            >
              No entities, actions, or functions
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty state -->
    <div v-if="modules.length === 0" class="py-8 text-center text-muted-foreground">
      <Package class="h-8 w-8 mx-auto mb-2 opacity-50" />
      <p class="text-sm">No modules loaded</p>
    </div>
  </div>
</template>
