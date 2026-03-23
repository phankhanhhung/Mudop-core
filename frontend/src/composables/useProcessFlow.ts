import { ref, computed, toValue, type Ref, type ComputedRef, type MaybeRefOrGetter, type Component, markRaw } from 'vue'
import { CheckCircle2, XCircle, AlertTriangle, Circle, Clock } from 'lucide-vue-next'

export type ProcessNodeStatus = 'positive' | 'negative' | 'critical' | 'neutral' | 'planned'

export interface ProcessNode {
  id: string
  title: string
  subtitle?: string
  status: ProcessNodeStatus
  icon?: Component
  children?: ProcessNode[]
  isFocused?: boolean
}

export interface UseProcessFlowOptions {
  nodes: MaybeRefOrGetter<ProcessNode[]>
  onNodeClick?: (node: ProcessNode) => void
}

export interface UseProcessFlowReturn {
  nodes: ComputedRef<ProcessNode[]>
  focusedNodeId: Ref<string | null>
  setFocused: (id: string | null) => void
  getStatusColor: (status: ProcessNodeStatus) => string
  getStatusIcon: (status: ProcessNodeStatus) => Component
  getStatusDotColor: (status: ProcessNodeStatus) => string
}

const statusColorMap: Record<ProcessNodeStatus, string> = {
  positive: 'bg-emerald-100 border-emerald-500 text-emerald-700',
  negative: 'bg-red-100 border-red-500 text-red-700',
  critical: 'bg-amber-100 border-amber-500 text-amber-700',
  neutral: 'bg-slate-100 border-slate-400 text-slate-600',
  planned: 'bg-blue-50 border-blue-300 text-blue-500 border-dashed',
}

const statusDotColorMap: Record<ProcessNodeStatus, string> = {
  positive: 'bg-emerald-500',
  negative: 'bg-red-500',
  critical: 'bg-amber-500',
  neutral: 'bg-slate-400',
  planned: 'bg-blue-300',
}

const statusIconMap: Record<ProcessNodeStatus, Component> = {
  positive: markRaw(CheckCircle2),
  negative: markRaw(XCircle),
  critical: markRaw(AlertTriangle),
  neutral: markRaw(Circle),
  planned: markRaw(Clock),
}

export function useProcessFlow(options: UseProcessFlowOptions): UseProcessFlowReturn {
  const focusedNodeId = ref<string | null>(null)

  const nodes = computed(() => {
    const raw = toValue(options.nodes)
    return raw.map((node) => ({
      ...node,
      isFocused: node.id === focusedNodeId.value,
    }))
  })

  function setFocused(id: string | null): void {
    focusedNodeId.value = id
  }

  function getStatusColor(status: ProcessNodeStatus): string {
    return statusColorMap[status]
  }

  function getStatusIcon(status: ProcessNodeStatus): Component {
    return statusIconMap[status]
  }

  function getStatusDotColor(status: ProcessNodeStatus): string {
    return statusDotColorMap[status]
  }

  return {
    nodes,
    focusedNodeId,
    setFocused,
    getStatusColor,
    getStatusIcon,
    getStatusDotColor,
  }
}
