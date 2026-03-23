<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { Button } from '@/components/ui/button'
import { ZoomIn, ZoomOut, Maximize2, RotateCcw } from 'lucide-vue-next'

// ── Types ──────────────────────────────────────────────────────────────────────
export interface ErdNode {
  id: string
  name: string
  namespace: string
  moduleName: string
  fieldCount: number
  keyFields: string[]
  isAbstract: boolean
  x: number
  y: number
  vx: number
  vy: number
  width: number
  height: number
  pinned: boolean
}

export interface ErdEdge {
  id: string
  sourceId: string
  targetId: string
  label: string
  isComposition: boolean
  associationName: string
}

// ── Props & Emits ──────────────────────────────────────────────────────────────
const props = defineProps<{
  inputNodes: ErdNode[]
  inputEdges: ErdEdge[]
}>()

const emit = defineEmits<{
  navigate: [entityId: string]
}>()

// ── Color palette (same as ModuleDependencyGraph) ──────────────────────────────
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

function getModuleHex(name: string): string {
  return MODULE_COLOR_HEX[hashModuleName(name) % MODULE_COLOR_HEX.length]
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
const tooltipNode = ref<ErdNode | null>(null)
const tooltipPos = ref({ x: 0, y: 0 })

// Dragging
const isDraggingNode = ref(false)
const dragNodeId = ref<string | null>(null)
const isPanning = ref(false)
const panStart = ref({ x: 0, y: 0 })
const panViewStart = ref({ x: 0, y: 0 })

// Click-vs-drag tracking
const mouseDownPos = ref({ x: 0, y: 0 })

// Layout
const nodes = ref<ErdNode[]>([])
const edges = ref<ErdEdge[]>([])
const isSimulating = ref(false)
const animFrameId = ref<number | null>(null)
const iterationCount = ref(0)
const MAX_ITERATIONS = 300

// Node dimensions
const NODE_WIDTH = 160
const NODE_HEIGHT = 56
const NODE_RX = 8

// ── Force-directed layout ──────────────────────────────────────────────────────
function simulate() {
  if (iterationCount.value >= MAX_ITERATIONS) {
    isSimulating.value = false
    return
  }

  const alpha = 1 - iterationCount.value / MAX_ITERATIONS
  const REPULSION = 12000
  const ATTRACTION = 0.003
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
  const nodeById = new Map<string, ErdNode>()
  for (const n of ns) nodeById.set(n.id, n)

  for (const edge of edges.value) {
    const source = nodeById.get(edge.sourceId)
    const target = nodeById.get(edge.targetId)
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
function computeEdgePath(edge: ErdEdge): string {
  const nodeById = new Map<string, ErdNode>()
  for (const n of nodes.value) nodeById.set(n.id, n)

  const source = nodeById.get(edge.sourceId)
  const target = nodeById.get(edge.targetId)
  if (!source || !target) return ''

  const sx = source.x
  const sy = source.y
  const tx = target.x
  const ty = target.y

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

  const scaleX = hw / Math.abs(dx || 0.001)
  const scaleY = hh / Math.abs(dy || 0.001)
  const scale = Math.min(scaleX, scaleY)

  return [centerX + dx * scale, centerY + dy * scale]
}

// Compute the midpoint of a quadratic bezier at t=0.5
function bezierMidpoint(path: string): { x: number; y: number } | null {
  // Parse "M sx sy Q cx cy tx ty"
  const m = path.match(/M\s+([\d.\-]+)\s+([\d.\-]+)\s+Q\s+([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)/)
  if (!m) return null
  const sx = parseFloat(m[1])
  const sy = parseFloat(m[2])
  const cx = parseFloat(m[3])
  const cy = parseFloat(m[4])
  const tx = parseFloat(m[5])
  const ty = parseFloat(m[6])
  return {
    x: 0.25 * sx + 0.5 * cx + 0.25 * tx,
    y: 0.25 * sy + 0.5 * cy + 0.25 * ty
  }
}

// ── Computed graph elements ────────────────────────────────────────────────────
const edgePaths = computed(() => {
  // Access reactive nodes for dependency tracking
  const _trigger = nodes.value.map(n => `${n.x},${n.y}`).join('|')
  void _trigger
  return edges.value.map(edge => {
    const path = computeEdgePath(edge)
    const mid = bezierMidpoint(path)
    return { ...edge, path, mid }
  })
})

const connectedNodeIds = computed(() => {
  const ids = new Set<string>()
  if (!selectedNodeId.value) return ids
  ids.add(selectedNodeId.value)
  for (const edge of edges.value) {
    if (edge.sourceId === selectedNodeId.value) ids.add(edge.targetId)
    if (edge.targetId === selectedNodeId.value) ids.add(edge.sourceId)
  }
  return ids
})

const connectedEdges = computed(() => {
  if (!selectedNodeId.value) return new Set<number>()
  const indices = new Set<number>()
  edges.value.forEach((edge, i) => {
    if (edge.sourceId === selectedNodeId.value || edge.targetId === selectedNodeId.value) {
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
function onNodeMouseDown(e: MouseEvent, node: ErdNode) {
  e.stopPropagation()
  e.preventDefault()
  isDraggingNode.value = false
  dragNodeId.value = node.id
  node.pinned = true
  mouseDownPos.value = { x: e.clientX, y: e.clientY }

  const onMove = (me: MouseEvent) => {
    if (!svgRef.value || !dragNodeId.value) return

    // Mark as dragging if moved more than 4px
    const ddx = me.clientX - mouseDownPos.value.x
    const ddy = me.clientY - mouseDownPos.value.y
    if (!isDraggingNode.value && Math.sqrt(ddx * ddx + ddy * ddy) > 4) {
      isDraggingNode.value = true
    }

    const svg = svgRef.value
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

  const onUp = (_me: MouseEvent) => {
    const wasDrag = isDraggingNode.value
    const n = nodes.value.find(n => n.id === dragNodeId.value)
    if (n) n.pinned = false

    if (!wasDrag) {
      // It was a click — emit navigate
      emit('navigate', node.id)
    }

    isDraggingNode.value = false
    dragNodeId.value = null
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
  }

  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

function onNodeEnter(node: ErdNode, e: MouseEvent) {
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
  rebuildFromProps(props.inputNodes)
  startSimulation()
  nextTick(() => {
    setTimeout(fitToView, 100)
  })
}

// ── Node opacity helpers ───────────────────────────────────────────────────────
function getNodeOpacity(node: ErdNode): number {
  if (!selectedNodeId.value) return 1
  return connectedNodeIds.value.has(node.id) ? 1 : 0.2
}

function getEdgeOpacity(index: number): number {
  if (!selectedNodeId.value) return 0.7
  return connectedEdges.value.has(index) ? 1 : 0.1
}

// ── Node color helper ──────────────────────────────────────────────────────────
function getNodeColor(node: ErdNode): string {
  return getModuleHex(node.moduleName || node.namespace || node.name)
}

// ── Build nodes from props ─────────────────────────────────────────────────────
function rebuildFromProps(newNodes: ErdNode[]) {
  // Preserve positions for nodes already in layout
  const posMap = new Map(nodes.value.map(n => [n.id, { x: n.x, y: n.y }]))

  const count = newNodes.length
  const radius = Math.max(200, count * 50)

  nodes.value = newNodes.map((n, i) => {
    const existing = posMap.get(n.id)
    if (existing) {
      return { ...n, x: existing.x, y: existing.y }
    }
    const angle = (2 * Math.PI * i) / count
    return {
      ...n,
      x: radius * Math.cos(angle) + (Math.random() - 0.5) * 20,
      y: radius * Math.sin(angle) + (Math.random() - 0.5) * 20,
      vx: 0,
      vy: 0,
      width: NODE_WIDTH,
      height: NODE_HEIGHT
    }
  })
}

// ── Lifecycle ──────────────────────────────────────────────────────────────────
watch(() => props.inputNodes, (newNodes) => {
  rebuildFromProps(newNodes)
  edges.value = [...props.inputEdges]
  startSimulation()
  nextTick(() => {
    setTimeout(fitToView, 250)
  })
}, { immediate: true, deep: false })

watch(() => props.inputEdges, (newEdges) => {
  edges.value = [...newEdges]
}, { deep: false })

onMounted(() => {
  if (props.inputNodes.length > 0) {
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
        <!-- Filled arrowhead for compositions (solid) -->
        <marker
          id="erd-arrow-comp"
          markerWidth="8"
          markerHeight="6"
          refX="8"
          refY="3"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 0, 8 3, 0 6" fill="#10b981" />
        </marker>
        <!-- Outline arrowhead for associations (open/hollow) -->
        <marker
          id="erd-arrow-assoc"
          markerWidth="10"
          markerHeight="8"
          refX="9"
          refY="4"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 1, 8 4, 0 7" fill="none" stroke="#6366f1" stroke-width="1" />
        </marker>
        <!-- Highlighted composition arrow -->
        <marker
          id="erd-arrow-comp-hi"
          markerWidth="8"
          markerHeight="6"
          refX="8"
          refY="3"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 0, 8 3, 0 6" fill="#059669" />
        </marker>
        <!-- Highlighted association arrow -->
        <marker
          id="erd-arrow-assoc-hi"
          markerWidth="10"
          markerHeight="8"
          refX="9"
          refY="4"
          orient="auto"
          markerUnits="strokeWidth"
        >
          <polygon points="0 1, 8 4, 0 7" fill="none" stroke="#4f46e5" stroke-width="1.5" />
        </marker>
      </defs>

      <!-- Edges -->
      <g class="edges">
        <g
          v-for="(edge, i) in edgePaths"
          :key="`edge-${i}`"
          :opacity="getEdgeOpacity(i)"
          class="transition-opacity duration-200"
        >
          <path
            :d="edge.path"
            fill="none"
            :stroke="edge.isComposition ? '#10b981' : '#6366f1'"
            :stroke-width="connectedEdges.has(i) && selectedNodeId ? 2.5 : 1.5"
            :stroke-dasharray="edge.isComposition ? 'none' : '6 4'"
            :marker-end="edge.isComposition
              ? (connectedEdges.has(i) && selectedNodeId ? 'url(#erd-arrow-comp-hi)' : 'url(#erd-arrow-comp)')
              : (connectedEdges.has(i) && selectedNodeId ? 'url(#erd-arrow-assoc-hi)' : 'url(#erd-arrow-assoc)')"
          />
          <!-- Cardinality label at bezier midpoint -->
          <g v-if="edge.mid && edge.label">
            <rect
              :x="edge.mid.x - edge.label.length * 3.5 - 4"
              :y="edge.mid.y - 8"
              :width="edge.label.length * 7 + 8"
              height="16"
              rx="3"
              fill="white"
              fill-opacity="0.85"
              class="stroke-border"
              stroke-width="0.5"
            />
            <text
              :x="edge.mid.x"
              :y="edge.mid.y + 1"
              text-anchor="middle"
              dominant-baseline="middle"
              :fill="edge.isComposition ? '#059669' : '#4f46e5'"
              class="text-[9px] font-medium"
              style="font-size: 9px; font-weight: 500;"
            >
              {{ edge.label }}
            </text>
          </g>
        </g>
      </g>

      <!-- Nodes -->
      <g
        v-for="node in nodes"
        :key="node.id"
        class="node cursor-pointer transition-opacity duration-200"
        :opacity="getNodeOpacity(node)"
        :transform="`translate(${node.x - node.width / 2}, ${node.y - node.height / 2})`"
        @mousedown="onNodeMouseDown($event, node)"
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
          class="fill-background"
          :stroke="selectedNodeId === node.id ? getNodeColor(node) : (hoveredNodeId === node.id ? getNodeColor(node) : '#e2e8f0')"
          :stroke-width="selectedNodeId === node.id ? 2.5 : (hoveredNodeId === node.id ? 1.5 : 1)"
          :stroke-dasharray="node.isAbstract ? '4 3' : 'none'"
        />

        <!-- Left color accent bar -->
        <rect
          :width="4"
          :height="node.height - 2"
          x="1"
          y="1"
          :rx="NODE_RX - 1"
          :fill="getNodeColor(node)"
        />

        <!-- Entity name (bold, truncated) -->
        <text
          :x="14"
          :y="20"
          dominant-baseline="middle"
          class="fill-foreground font-semibold"
          style="font-size: 12px; font-weight: 600;"
        >
          {{ node.name.length > 16 ? node.name.substring(0, 15) + '\u2026' : node.name }}
        </text>

        <!-- Abstract indicator -->
        <text
          v-if="node.isAbstract"
          :x="node.width - 8"
          :y="20"
          text-anchor="end"
          dominant-baseline="middle"
          class="fill-muted-foreground"
          style="font-size: 9px; font-style: italic;"
        >
          abstract
        </text>

        <!-- Namespace / module in muted text -->
        <text
          :x="14"
          :y="38"
          dominant-baseline="middle"
          class="fill-muted-foreground"
          style="font-size: 9px;"
        >
          {{ (node.namespace || node.moduleName || '').length > 22
            ? (node.namespace || node.moduleName || '').substring(0, 21) + '\u2026'
            : (node.namespace || node.moduleName || '') }}
        </text>

        <!-- Field count badge -->
        <g :transform="`translate(${node.width - 10}, ${node.height - 12})`">
          <text
            text-anchor="end"
            dominant-baseline="middle"
            class="fill-muted-foreground"
            style="font-size: 9px;"
          >
            {{ node.fieldCount }} fields
          </text>
        </g>

        <!-- Key field count indicator -->
        <g v-if="node.keyFields && node.keyFields.length > 0" :transform="`translate(${node.width - 10}, 20)`">
          <text
            text-anchor="end"
            dominant-baseline="middle"
            style="font-size: 10px;"
          >
            {{ '\uD83D\uDD11' }}{{ node.keyFields.length > 1 ? node.keyFields.length : '' }}
          </text>
        </g>
      </g>
    </svg>

    <!-- Tooltip (HTML overlay) -->
    <div
      v-if="tooltipNode && !isDraggingNode"
      class="absolute pointer-events-none z-10 bg-popover text-popover-foreground border rounded-lg shadow-lg px-3 py-2 text-sm max-w-[260px]"
      :style="{ left: tooltipPos.x + 'px', top: tooltipPos.y + 'px' }"
    >
      <div class="font-semibold flex items-center gap-1.5">
        <span
          class="inline-block w-2.5 h-2.5 rounded-full flex-shrink-0"
          :style="{ background: getNodeColor(tooltipNode) }"
        />
        {{ tooltipNode.name }}
        <span v-if="tooltipNode.isAbstract" class="text-xs font-normal text-muted-foreground italic">(abstract)</span>
      </div>
      <div v-if="tooltipNode.namespace" class="text-xs text-muted-foreground mt-0.5">{{ tooltipNode.namespace }}</div>
      <div v-if="tooltipNode.moduleName && tooltipNode.moduleName !== tooltipNode.namespace" class="text-xs text-muted-foreground">
        Module: {{ tooltipNode.moduleName }}
      </div>
      <div class="flex gap-3 mt-1.5 text-xs">
        <span class="text-slate-600 dark:text-slate-400">{{ tooltipNode.fieldCount }} fields</span>
        <span v-if="tooltipNode.keyFields && tooltipNode.keyFields.length" class="text-amber-600 dark:text-amber-400">
          Keys: {{ tooltipNode.keyFields.join(', ') }}
        </span>
      </div>
      <div class="text-xs text-muted-foreground mt-1">Click to navigate to entity list</div>
    </div>

    <!-- Controls -->
    <div class="absolute top-3 right-3 flex flex-col gap-1">
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" title="Zoom in" @click="zoomInAction">
        <ZoomIn class="h-4 w-4" />
      </Button>
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" title="Zoom out" @click="zoomOutAction">
        <ZoomOut class="h-4 w-4" />
      </Button>
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" title="Fit to view" @click="fitToView">
        <Maximize2 class="h-4 w-4" />
      </Button>
      <Button variant="outline" size="sm" class="h-8 w-8 p-0" title="Reset layout" @click="resetLayout">
        <RotateCcw class="h-4 w-4" />
      </Button>
    </div>

    <!-- Legend (bottom-left) -->
    <div class="absolute bottom-3 left-3 bg-background/80 border rounded-md px-2.5 py-2 text-xs text-muted-foreground flex flex-col gap-1.5 shadow-sm">
      <div class="flex items-center gap-2">
        <svg width="28" height="10" class="flex-shrink-0">
          <line x1="0" y1="5" x2="22" y2="5" stroke="#10b981" stroke-width="2" />
          <polygon points="22 2, 28 5, 22 8" fill="#10b981" />
        </svg>
        <span>Composition</span>
      </div>
      <div class="flex items-center gap-2">
        <svg width="28" height="10" class="flex-shrink-0">
          <line x1="0" y1="5" x2="22" y2="5" stroke="#6366f1" stroke-width="1.5" stroke-dasharray="4 3" />
          <polygon points="22 2, 28 5, 22 8" fill="none" stroke="#6366f1" stroke-width="1" />
        </svg>
        <span>Association</span>
      </div>
      <div class="flex items-center gap-2">
        <svg width="28" height="10" class="flex-shrink-0">
          <rect x="0" y="1" width="28" height="8" rx="2" fill="none" stroke="#94a3b8" stroke-width="1.5" stroke-dasharray="3 2" />
        </svg>
        <span>Abstract</span>
      </div>
    </div>

    <!-- Simulation indicator -->
    <div v-if="isSimulating" class="absolute bottom-3 right-3 text-xs text-muted-foreground flex items-center gap-1.5">
      <span class="inline-block h-2 w-2 rounded-full bg-amber-500 animate-pulse" />
      Laying out...
    </div>

    <!-- Empty state -->
    <div
      v-if="nodes.length === 0"
      class="absolute inset-0 flex items-center justify-center"
    >
      <div class="text-center text-muted-foreground">
        <p class="text-sm">No entities to display</p>
        <p class="text-xs mt-1">Select a module or namespace to view its entity relationship diagram</p>
      </div>
    </div>
  </div>
</template>
