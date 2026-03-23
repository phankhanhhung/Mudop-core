<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import type { ModuleStatus } from '@/services/adminService'
import { Button } from '@/components/ui/button'
import { ZoomIn, ZoomOut, Maximize2, RotateCcw } from 'lucide-vue-next'

// ── Props ──────────────────────────────────────────────────────────────────────
const props = defineProps<{
  modules: ModuleStatus[]
}>()

// ── Color palette (same as AdminModulesView) ───────────────────────────────────
const MODULE_COLORS = [
  'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500',
  'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500',
  'bg-pink-500', 'bg-orange-500'
]

// Hex equivalents for SVG fill
const MODULE_COLOR_HEX = [
  '#3b82f6', '#10b981', '#8b5cf6', '#f59e0b',
  '#f43f5e', '#06b6d4', '#6366f1', '#14b8a6',
  '#ec4899', '#f97316'
]

function hashModuleName(name: string): number {
  let hash = 0
  for (const char of name) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return Math.abs(hash)
}

function getModuleColorIndex(name: string): number {
  return hashModuleName(name) % MODULE_COLORS.length
}

function getModuleHex(name: string): string {
  return MODULE_COLOR_HEX[getModuleColorIndex(name)]
}

// ── Types ──────────────────────────────────────────────────────────────────────
interface GraphNode {
  id: string
  name: string
  version: string
  entityCount: number
  serviceCount: number
  schemaInitialized: boolean
  author?: string
  x: number
  y: number
  vx: number
  vy: number
  width: number
  height: number
  color: string
  pinned: boolean
}

interface GraphEdge {
  source: string
  target: string
  isResolved: boolean
  versionRange: string
}

// ── Refs ───────────────────────────────────────────────────────────────────────
const svgRef = ref<SVGSVGElement | null>(null)
const containerRef = ref<HTMLDivElement | null>(null)

// View transform
const viewX = ref(0)
const viewY = ref(0)
const zoom = ref(1)

// Interaction state
const selectedNodeId = ref<string | null>(null)
const hoveredNodeId = ref<string | null>(null)
const tooltipNode = ref<GraphNode | null>(null)
const tooltipPos = ref({ x: 0, y: 0 })

// Dragging
const isDraggingNode = ref(false)
const dragNodeId = ref<string | null>(null)
const isPanning = ref(false)
const panStart = ref({ x: 0, y: 0 })
const panViewStart = ref({ x: 0, y: 0 })

// Layout
const nodes = ref<GraphNode[]>([])
const edges = ref<GraphEdge[]>([])
const isSimulating = ref(false)
const animFrameId = ref<number | null>(null)
const iterationCount = ref(0)
const MAX_ITERATIONS = 200

// Node dimensions
const NODE_WIDTH = 180
const NODE_HEIGHT = 60
const NODE_RX = 10

// ── Build graph data ───────────────────────────────────────────────────────────
function buildGraph() {
  const newNodes: GraphNode[] = []
  const newEdges: GraphEdge[] = []
  const moduleMap = new Map<string, ModuleStatus>()

  for (const mod of props.modules) {
    moduleMap.set(mod.name, mod)
  }

  // Create nodes with random initial positions in a circle
  const count = props.modules.length
  const radius = Math.max(150, count * 40)

  for (let i = 0; i < count; i++) {
    const mod = props.modules[i]
    const angle = (2 * Math.PI * i) / count
    newNodes.push({
      id: mod.id,
      name: mod.name,
      version: mod.version,
      entityCount: mod.entityCount,
      serviceCount: mod.serviceCount,
      schemaInitialized: mod.schemaInitialized,
      author: mod.author,
      x: radius * Math.cos(angle) + (Math.random() - 0.5) * 20,
      y: radius * Math.sin(angle) + (Math.random() - 0.5) * 20,
      vx: 0,
      vy: 0,
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      color: getModuleHex(mod.name),
      pinned: false
    })
  }

  // Create edges from dependencies
  for (const mod of props.modules) {
    if (!mod.dependencies) continue
    for (const dep of mod.dependencies) {
      const targetMod = moduleMap.get(dep.dependsOnName)
      if (targetMod) {
        newEdges.push({
          source: mod.id,
          target: targetMod.id,
          isResolved: dep.isResolved,
          versionRange: dep.versionRange
        })
      } else {
        // Unresolved external dependency -- no target node, skip edge
      }
    }
  }

  nodes.value = newNodes
  edges.value = newEdges
}

// ── Force-directed layout ──────────────────────────────────────────────────────
function simulate() {
  if (iterationCount.value >= MAX_ITERATIONS) {
    isSimulating.value = false
    return
  }

  const alpha = 1 - iterationCount.value / MAX_ITERATIONS
  const REPULSION = 8000
  const ATTRACTION = 0.005
  const GRAVITY = 0.02
  const DAMPING = 0.85
  const MIN_DIST = 30

  const ns = nodes.value

  // Repulsion between all node pairs
  for (let i = 0; i < ns.length; i++) {
    for (let j = i + 1; j < ns.length; j++) {
      let dx = ns[i].x - ns[j].x
      let dy = ns[i].y - ns[j].y
      let dist = Math.sqrt(dx * dx + dy * dy)
      if (dist < MIN_DIST) dist = MIN_DIST

      const force = (REPULSION * alpha) / (dist * dist)
      const fx = (dx / dist) * force
      const fy = (dy / dist) * force

      if (!ns[i].pinned) {
        ns[i].vx += fx
        ns[i].vy += fy
      }
      if (!ns[j].pinned) {
        ns[j].vx -= fx
        ns[j].vy -= fy
      }
    }
  }

  // Attraction along edges
  const nodeById = new Map<string, GraphNode>()
  for (const n of ns) nodeById.set(n.id, n)

  for (const edge of edges.value) {
    const source = nodeById.get(edge.source)
    const target = nodeById.get(edge.target)
    if (!source || !target) continue

    const dx = target.x - source.x
    const dy = target.y - source.y
    const dist = Math.sqrt(dx * dx + dy * dy)
    if (dist < 1) continue

    const force = dist * ATTRACTION * alpha
    const fx = (dx / dist) * force
    const fy = (dy / dist) * force

    if (!source.pinned) {
      source.vx += fx
      source.vy += fy
    }
    if (!target.pinned) {
      target.vx -= fx
      target.vy -= fy
    }
  }

  // Gravity toward center
  for (const n of ns) {
    if (n.pinned) continue
    n.vx -= n.x * GRAVITY * alpha
    n.vy -= n.y * GRAVITY * alpha
  }

  // Apply velocities with damping
  for (const n of ns) {
    if (n.pinned) continue
    n.vx *= DAMPING
    n.vy *= DAMPING
    n.x += n.vx
    n.y += n.vy
  }

  iterationCount.value++

  if (iterationCount.value < MAX_ITERATIONS) {
    animFrameId.value = requestAnimationFrame(simulate)
  } else {
    isSimulating.value = false
  }
}

function startSimulation() {
  stopSimulation()
  iterationCount.value = 0
  isSimulating.value = true
  animFrameId.value = requestAnimationFrame(simulate)
}

function stopSimulation() {
  if (animFrameId.value !== null) {
    cancelAnimationFrame(animFrameId.value)
    animFrameId.value = null
  }
  isSimulating.value = false
}

// ── Edge path computation ──────────────────────────────────────────────────────
function computeEdgePath(edge: GraphEdge): string {
  const nodeById = new Map<string, GraphNode>()
  for (const n of nodes.value) nodeById.set(n.id, n)

  const source = nodeById.get(edge.source)
  const target = nodeById.get(edge.target)
  if (!source || !target) return ''

  const sx = source.x
  const sy = source.y
  const tx = target.x
  const ty = target.y

  // Compute midpoint with offset for curve
  const mx = (sx + tx) / 2
  const my = (sy + ty) / 2
  const dx = tx - sx
  const dy = ty - sy
  const dist = Math.sqrt(dx * dx + dy * dy)
  if (dist < 1) return `M ${sx} ${sy} L ${tx} ${ty}`

  // Perpendicular offset for curve (proportional to distance, capped)
  const offset = Math.min(dist * 0.15, 40)
  const nx = -dy / dist
  const ny = dx / dist
  const cx = mx + nx * offset
  const cy = my + ny * offset

  // Clip endpoints to node rectangle boundaries
  const [startX, startY] = clipToRect(sx, sy, cx, cy, source.width, source.height)
  const [endX, endY] = clipToRect(tx, ty, cx, cy, target.width, target.height)

  return `M ${startX} ${startY} Q ${cx} ${cy} ${endX} ${endY}`
}

function clipToRect(
  centerX: number, centerY: number,
  toX: number, toY: number,
  width: number, height: number
): [number, number] {
  const hw = width / 2
  const hh = height / 2
  const dx = toX - centerX
  const dy = toY - centerY

  if (Math.abs(dx) < 0.001 && Math.abs(dy) < 0.001) {
    return [centerX, centerY]
  }

  // Scale factor to reach rectangle border
  const scaleX = hw / Math.abs(dx || 0.001)
  const scaleY = hh / Math.abs(dy || 0.001)
  const scale = Math.min(scaleX, scaleY)

  return [centerX + dx * scale, centerY + dy * scale]
}

// ── Computed graph elements ────────────────────────────────────────────────────
const edgePaths = computed(() => {
  // Access reactive nodes for dependency tracking
  const _trigger = nodes.value.map(n => `${n.x},${n.y}`).join('|')
  void _trigger
  return edges.value.map(edge => ({
    ...edge,
    path: computeEdgePath(edge)
  }))
})

const connectedNodeIds = computed(() => {
  const ids = new Set<string>()
  if (!selectedNodeId.value) return ids
  ids.add(selectedNodeId.value)
  for (const edge of edges.value) {
    if (edge.source === selectedNodeId.value) ids.add(edge.target)
    if (edge.target === selectedNodeId.value) ids.add(edge.source)
  }
  return ids
})

const connectedEdges = computed(() => {
  if (!selectedNodeId.value) return new Set<number>()
  const indices = new Set<number>()
  edges.value.forEach((edge, i) => {
    if (edge.source === selectedNodeId.value || edge.target === selectedNodeId.value) {
      indices.add(i)
    }
  })
  return indices
})

// ── SVG viewBox ────────────────────────────────────────────────────────────────
const viewBox = computed(() => {
  if (!containerRef.value) return '0 0 800 500'
  const w = containerRef.value.clientWidth / zoom.value
  const h = containerRef.value.clientHeight / zoom.value
  const x = -w / 2 + viewX.value
  const y = -h / 2 + viewY.value
  return `${x} ${y} ${w} ${h}`
})

// ── Interaction handlers ───────────────────────────────────────────────────────
function onNodeMouseDown(e: MouseEvent, node: GraphNode) {
  e.stopPropagation()
  e.preventDefault()
  isDraggingNode.value = true
  dragNodeId.value = node.id
  node.pinned = true

  const onMove = (me: MouseEvent) => {
    if (!svgRef.value || !dragNodeId.value) return
    const svg = svgRef.value

    // Convert screen coords to viewBox coords
    const rect = svg.getBoundingClientRect()
    const vb = viewBox.value.split(' ').map(Number)
    const scaleX = vb[2] / rect.width
    const scaleY = vb[3] / rect.height
    const nodeX = vb[0] + (me.clientX - rect.left) * scaleX
    const nodeY = vb[1] + (me.clientY - rect.top) * scaleY

    const n = nodes.value.find(n => n.id === dragNodeId.value)
    if (n) {
      n.x = nodeX
      n.y = nodeY
      n.vx = 0
      n.vy = 0
    }
  }

  const onUp = () => {
    isDraggingNode.value = false
    const n = nodes.value.find(n => n.id === dragNodeId.value)
    if (n) n.pinned = false
    dragNodeId.value = null
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
  }

  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

function onNodeClick(e: MouseEvent, node: GraphNode) {
  e.stopPropagation()
  if (selectedNodeId.value === node.id) {
    selectedNodeId.value = null
  } else {
    selectedNodeId.value = node.id
  }
}

function onNodeEnter(node: GraphNode, e: MouseEvent) {
  hoveredNodeId.value = node.id
  tooltipNode.value = node
  updateTooltipPos(e)
}

function onNodeMove(e: MouseEvent) {
  updateTooltipPos(e)
}

function onNodeLeave() {
  hoveredNodeId.value = null
  tooltipNode.value = null
}

function updateTooltipPos(e: MouseEvent) {
  if (!containerRef.value) return
  const rect = containerRef.value.getBoundingClientRect()
  tooltipPos.value = {
    x: e.clientX - rect.left + 12,
    y: e.clientY - rect.top - 10
  }
}

function onBackgroundMouseDown(e: MouseEvent) {
  if (isDraggingNode.value) return
  isPanning.value = true
  panStart.value = { x: e.clientX, y: e.clientY }
  panViewStart.value = { x: viewX.value, y: viewY.value }

  const onMove = (me: MouseEvent) => {
    if (!isPanning.value || !containerRef.value) return
    const dx = me.clientX - panStart.value.x
    const dy = me.clientY - panStart.value.y
    viewX.value = panViewStart.value.x - dx / zoom.value
    viewY.value = panViewStart.value.y - dy / zoom.value
  }

  const onUp = () => {
    isPanning.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
  }

  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

function onBackgroundClick() {
  if (!isDraggingNode.value) {
    selectedNodeId.value = null
  }
}

function onWheel(e: WheelEvent) {
  e.preventDefault()
  const delta = e.deltaY > 0 ? 0.9 : 1.1
  const newZoom = Math.max(0.2, Math.min(5, zoom.value * delta))
  zoom.value = newZoom
}

// ── Controls ───────────────────────────────────────────────────────────────────
function zoomInAction() {
  zoom.value = Math.min(5, zoom.value * 1.2)
}

function zoomOutAction() {
  zoom.value = Math.max(0.2, zoom.value / 1.2)
}

function fitToView() {
  if (nodes.value.length === 0) return
  const padding = 80
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity
  for (const n of nodes.value) {
    minX = Math.min(minX, n.x - n.width / 2)
    maxX = Math.max(maxX, n.x + n.width / 2)
    minY = Math.min(minY, n.y - n.height / 2)
    maxY = Math.max(maxY, n.y + n.height / 2)
  }

  if (!containerRef.value) return
  const cw = containerRef.value.clientWidth
  const ch = containerRef.value.clientHeight
  const graphW = maxX - minX + padding * 2
  const graphH = maxY - minY + padding * 2

  zoom.value = Math.min(cw / graphW, ch / graphH, 2)
  viewX.value = (minX + maxX) / 2
  viewY.value = (minY + maxY) / 2
}

function resetLayout() {
  buildGraph()
  startSimulation()
  nextTick(() => {
    setTimeout(fitToView, 100)
  })
}

// ── Node opacity helpers ───────────────────────────────────────────────────────
function getNodeOpacity(node: GraphNode): number {
  if (!selectedNodeId.value) return 1
  return connectedNodeIds.value.has(node.id) ? 1 : 0.2
}

function getEdgeOpacity(index: number): number {
  if (!selectedNodeId.value) return 0.7
  return connectedEdges.value.has(index) ? 1 : 0.1
}

// ── Lifecycle ──────────────────────────────────────────────────────────────────
watch(() => props.modules, () => {
  buildGraph()
  startSimulation()
  nextTick(() => {
    setTimeout(fitToView, 250)
  })
}, { deep: true })

onMounted(() => {
  if (props.modules.length > 0) {
    buildGraph()
    startSimulation()
    nextTick(() => {
      setTimeout(fitToView, 300)
    })
  }
})

onUnmounted(() => {
  stopSimulation()
})
</script>

<template>
  <div ref="containerRef" class="relative w-full h-full min-h-[400px] bg-muted/30 rounded-lg border overflow-hidden select-none">
    <!-- SVG Canvas -->
    <svg
      ref="svgRef"
      :viewBox="viewBox"
      class="w-full h-full"
      :class="isPanning ? 'cursor-grabbing' : 'cursor-grab'"
      @mousedown="onBackgroundMouseDown"
      @click="onBackgroundClick"
      @wheel.prevent="onWheel"
    >
      <defs>
        <!-- Arrowhead marker for resolved edges -->
        <marker
          id="arrowhead"
          markerWidth="8"
          markerHeight="6"
          refX="8"
          refY="3"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 0, 8 3, 0 6" fill="currentColor" class="text-muted-foreground" />
        </marker>
        <!-- Arrowhead marker for unresolved edges -->
        <marker
          id="arrowhead-unresolved"
          markerWidth="8"
          markerHeight="6"
          refX="8"
          refY="3"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 0, 8 3, 0 6" fill="#ef4444" />
        </marker>
        <!-- Arrowhead for highlighted edges -->
        <marker
          id="arrowhead-highlight"
          markerWidth="8"
          markerHeight="6"
          refX="8"
          refY="3"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 0, 8 3, 0 6" fill="currentColor" class="text-foreground" />
        </marker>
      </defs>

      <!-- Edges -->
      <g class="edges">
        <path
          v-for="(edge, i) in edgePaths"
          :key="`edge-${i}`"
          :d="edge.path"
          fill="none"
          :stroke="edge.isResolved ? (connectedEdges.has(i) && selectedNodeId ? 'currentColor' : 'currentColor') : '#ef4444'"
          :stroke-width="connectedEdges.has(i) && selectedNodeId ? 2.5 : 1.5"
          :stroke-dasharray="edge.isResolved ? 'none' : '6 4'"
          :marker-end="edge.isResolved
            ? (connectedEdges.has(i) && selectedNodeId ? 'url(#arrowhead-highlight)' : 'url(#arrowhead)')
            : 'url(#arrowhead-unresolved)'"
          :opacity="getEdgeOpacity(i)"
          :class="edge.isResolved ? 'text-muted-foreground' : ''"
          class="transition-opacity duration-200"
        />
      </g>

      <!-- Nodes -->
      <g
        v-for="node in nodes"
        :key="node.id"
        class="node cursor-pointer transition-opacity duration-200"
        :opacity="getNodeOpacity(node)"
        :transform="`translate(${node.x - node.width / 2}, ${node.y - node.height / 2})`"
        @mousedown="onNodeMouseDown($event, node)"
        @click="onNodeClick($event, node)"
        @mouseenter="onNodeEnter(node, $event)"
        @mousemove="onNodeMove"
        @mouseleave="onNodeLeave"
      >
        <!-- Node background -->
        <rect
          :width="node.width"
          :height="node.height"
          :rx="NODE_RX"
          :ry="NODE_RX"
          class="fill-background stroke-border"
          :stroke-width="selectedNodeId === node.id ? 2.5 : 1"
          :stroke="selectedNodeId === node.id ? node.color : undefined"
        />

        <!-- Color accent bar -->
        <rect
          :width="4"
          :height="node.height - 2"
          x="1"
          y="1"
          :rx="NODE_RX - 1"
          :fill="node.color"
        />

        <!-- Module name -->
        <text
          :x="16"
          :y="22"
          class="fill-foreground text-[13px] font-medium"
          dominant-baseline="middle"
        >
          {{ node.name.length > 16 ? node.name.substring(0, 15) + '...' : node.name }}
        </text>

        <!-- Version badge -->
        <g :transform="`translate(${node.width - 8}, 14)`">
          <rect
            :x="-('v' + node.version).length * 4.5 - 6"
            y="-9"
            :width="('v' + node.version).length * 4.5 + 12"
            height="18"
            rx="4"
            class="fill-muted"
          />
          <text
            :x="-('v' + node.version).length * 4.5 / 2"
            y="0"
            text-anchor="middle"
            class="fill-muted-foreground text-[10px]"
            dominant-baseline="middle"
          >
            v{{ node.version }}
          </text>
        </g>

        <!-- Entity count badge -->
        <g transform="translate(16, 44)">
          <rect
            x="-2"
            y="-8"
            width="18"
            height="16"
            rx="3"
            fill="#10b981"
            opacity="0.15"
          />
          <text
            x="7"
            y="0"
            text-anchor="middle"
            dominant-baseline="middle"
            fill="#10b981"
            class="text-[10px] font-medium"
          >
            {{ node.entityCount }}
          </text>
          <text
            x="22"
            y="0"
            dominant-baseline="middle"
            class="fill-muted-foreground text-[9px]"
          >
            entities
          </text>
        </g>

        <!-- Service count badge -->
        <g transform="translate(80, 44)">
          <rect
            x="-2"
            y="-8"
            width="18"
            height="16"
            rx="3"
            fill="#8b5cf6"
            opacity="0.15"
          />
          <text
            x="7"
            y="0"
            text-anchor="middle"
            dominant-baseline="middle"
            fill="#8b5cf6"
            class="text-[10px] font-medium"
          >
            {{ node.serviceCount }}
          </text>
          <text
            x="22"
            y="0"
            dominant-baseline="middle"
            class="fill-muted-foreground text-[9px]"
          >
            svcs
          </text>
        </g>

        <!-- Schema status dot -->
        <circle
          :cx="node.width - 12"
          :cy="node.height - 14"
          r="4"
          :fill="node.schemaInitialized ? '#10b981' : '#94a3b8'"
        />
      </g>
    </svg>

    <!-- Tooltip (HTML overlay) -->
    <div
      v-if="tooltipNode && !isDraggingNode"
      class="absolute pointer-events-none z-10 bg-popover text-popover-foreground border rounded-lg shadow-lg px-3 py-2 text-sm max-w-[240px]"
      :style="{ left: tooltipPos.x + 'px', top: tooltipPos.y + 'px' }"
    >
      <div class="font-semibold">{{ tooltipNode.name }}</div>
      <div class="text-xs text-muted-foreground mt-0.5">v{{ tooltipNode.version }}</div>
      <div v-if="tooltipNode.author" class="text-xs text-muted-foreground">by {{ tooltipNode.author }}</div>
      <div class="flex gap-3 mt-1.5 text-xs">
        <span class="text-emerald-600 dark:text-emerald-400">{{ tooltipNode.entityCount }} entities</span>
        <span class="text-violet-600 dark:text-violet-400">{{ tooltipNode.serviceCount }} services</span>
      </div>
      <div class="text-xs mt-1" :class="tooltipNode.schemaInitialized ? 'text-emerald-600 dark:text-emerald-400' : 'text-muted-foreground'">
        Schema: {{ tooltipNode.schemaInitialized ? 'Initialized' : 'Not initialized' }}
      </div>
    </div>

    <!-- Controls -->
    <div class="absolute top-3 right-3 flex flex-col gap-1">
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" @click="zoomInAction" title="Zoom in">
        <ZoomIn class="h-4 w-4" />
      </Button>
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" @click="zoomOutAction" title="Zoom out">
        <ZoomOut class="h-4 w-4" />
      </Button>
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" @click="fitToView" title="Fit to view">
        <Maximize2 class="h-4 w-4" />
      </Button>
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" @click="resetLayout" title="Reset layout">
        <RotateCcw class="h-4 w-4" />
      </Button>
    </div>

    <!-- Simulation indicator -->
    <div v-if="isSimulating" class="absolute bottom-3 left-3 text-xs text-muted-foreground flex items-center gap-1.5">
      <span class="inline-block h-2 w-2 rounded-full bg-amber-500 animate-pulse" />
      Laying out...
    </div>

    <!-- Empty state -->
    <div
      v-if="nodes.length === 0"
      class="absolute inset-0 flex items-center justify-center"
    >
      <div class="text-center text-muted-foreground">
        <p class="text-sm">No modules to display</p>
        <p class="text-xs mt-1">Compile and publish modules to see their dependency graph</p>
      </div>
    </div>
  </div>
</template>
