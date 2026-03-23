<script setup lang="ts">
import { cn } from '@/lib/utils'
import { useProcessFlow, type ProcessNode } from '@/composables/useProcessFlow'
import { ChevronRight, GitBranch } from 'lucide-vue-next'

interface Props {
  nodes: ProcessNode[]
  scrollable?: boolean
  showLabels?: boolean
  class?: string
}

const props = withDefaults(defineProps<Props>(), {
  scrollable: true,
  showLabels: true,
})

const emit = defineEmits<{
  'node-click': [node: ProcessNode]
  'node-focus': [node: ProcessNode]
}>()

const {
  nodes: flowNodes,
  setFocused,
  getStatusColor,
  getStatusIcon,
  getStatusDotColor,
} = useProcessFlow({
  nodes: () => props.nodes,
})

function onNodeClick(node: ProcessNode) {
  setFocused(node.id)
  emit('node-click', node)
  emit('node-focus', node)
}
</script>

<template>
  <div :class="cn('process-flow-container', scrollable ? 'overflow-x-auto' : '', props.class)">
    <div class="flex items-start gap-0 min-w-max py-4 px-6">
      <template v-for="(node, index) in flowNodes" :key="node.id">
        <!-- Connector line (between nodes, not before first) -->
        <div v-if="index > 0" class="flex items-center self-center">
          <div class="w-8 h-0.5 bg-muted-foreground/30" />
          <ChevronRight class="h-4 w-4 -ml-1 text-muted-foreground/50" />
        </div>

        <!-- Node -->
        <div
          class="flex flex-col items-center gap-2 cursor-pointer group"
          @click="onNodeClick(node)"
        >
          <!-- Circle/icon -->
          <div class="relative">
            <div
              :class="cn(
                'w-16 h-16 rounded-full border-2 flex items-center justify-center transition-all',
                'group-hover:shadow-md group-hover:scale-105',
                getStatusColor(node.status),
                node.isFocused ? 'ring-2 ring-ring ring-offset-2' : '',
              )"
            >
              <component
                :is="node.icon || getStatusIcon(node.status)"
                class="h-6 w-6"
              />
            </div>
            <!-- Status dot (bottom-right corner) -->
            <div
              :class="cn(
                'absolute -bottom-0.5 -right-0.5 w-4 h-4 rounded-full border-2 border-background',
                getStatusDotColor(node.status),
              )"
            />
          </div>

          <!-- Labels -->
          <div v-if="showLabels" class="text-center max-w-24">
            <div class="text-sm font-medium truncate">{{ node.title }}</div>
            <div v-if="node.subtitle" class="text-xs text-muted-foreground truncate">
              {{ node.subtitle }}
            </div>
          </div>

          <!-- Sub-process indicator -->
          <div
            v-if="node.children?.length"
            class="text-xs text-muted-foreground flex items-center gap-1"
          >
            <GitBranch class="h-3 w-3" />
            {{ node.children.length }} sub-steps
          </div>
        </div>
      </template>
    </div>
  </div>
</template>
