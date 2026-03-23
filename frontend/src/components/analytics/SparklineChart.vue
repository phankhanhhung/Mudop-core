<script setup lang="ts">
import { computed, ref } from 'vue'

interface Props {
  data: number[]
  width?: number
  height?: number
  color?: 'primary' | 'emerald' | 'amber' | 'rose' | 'violet' | 'cyan'
  showArea?: boolean
  showDots?: boolean
  showMinMax?: boolean
  animate?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  width: 120,
  height: 40,
  color: 'primary',
  showArea: true,
  showDots: false,
  showMinMax: false,
  animate: true,
})

const svgRef = ref<SVGSVGElement | null>(null)
const hoverIndex = ref<number | null>(null)
const mouseX = ref(0)

const colorMap: Record<string, { stroke: string; area: string; dot: string }> = {
  primary: { stroke: 'hsl(var(--primary))', area: 'url(#sparkGradPrimary)', dot: 'hsl(var(--primary))' },
  emerald: { stroke: 'rgb(16, 185, 129)', area: 'url(#sparkGradEmerald)', dot: 'rgb(16, 185, 129)' },
  amber: { stroke: 'rgb(245, 158, 11)', area: 'url(#sparkGradAmber)', dot: 'rgb(245, 158, 11)' },
  rose: { stroke: 'rgb(239, 68, 68)', area: 'url(#sparkGradRose)', dot: 'rgb(239, 68, 68)' },
  violet: { stroke: 'rgb(139, 92, 246)', area: 'url(#sparkGradViolet)', dot: 'rgb(139, 92, 246)' },
  cyan: { stroke: 'rgb(6, 182, 212)', area: 'url(#sparkGradCyan)', dot: 'rgb(6, 182, 212)' },
}

const gradientStops = computed(() => {
  const map: Record<string, { start: string; end: string }> = {
    primary: { start: 'hsl(var(--primary) / 0.3)', end: 'hsl(var(--primary) / 0.02)' },
    emerald: { start: 'rgba(16, 185, 129, 0.3)', end: 'rgba(16, 185, 129, 0.02)' },
    amber: { start: 'rgba(245, 158, 11, 0.3)', end: 'rgba(245, 158, 11, 0.02)' },
    rose: { start: 'rgba(239, 68, 68, 0.3)', end: 'rgba(239, 68, 68, 0.02)' },
    violet: { start: 'rgba(139, 92, 246, 0.3)', end: 'rgba(139, 92, 246, 0.02)' },
    cyan: { start: 'rgba(6, 182, 212, 0.3)', end: 'rgba(6, 182, 212, 0.02)' },
  }
  return map[props.color]
})

const colors = computed(() => colorMap[props.color])

const padding = 4

const dataMin = computed(() => (props.data.length > 0 ? Math.min(...props.data) : 0))
const dataMax = computed(() => {
  const mx = props.data.length > 0 ? Math.max(...props.data) : 1
  return mx === dataMin.value ? mx + 1 : mx
})

const points = computed(() => {
  if (props.data.length < 2) return []
  const innerW = props.width - padding * 2
  const innerH = props.height - padding * 2
  const step = innerW / (props.data.length - 1)
  const min = dataMin.value
  const range = dataMax.value - min
  return props.data.map((val, i) => ({
    x: padding + i * step,
    y: padding + innerH - ((val - min) / range) * innerH,
    value: val,
    index: i,
  }))
})

const minIndex = computed(() => {
  if (props.data.length === 0) return -1
  let idx = 0
  for (let i = 1; i < props.data.length; i++) {
    if (props.data[i] < props.data[idx]) idx = i
  }
  return idx
})

const maxIndex = computed(() => {
  if (props.data.length === 0) return -1
  let idx = 0
  for (let i = 1; i < props.data.length; i++) {
    if (props.data[i] > props.data[idx]) idx = i
  }
  return idx
})

const linePath = computed(() => {
  const pts = points.value
  if (pts.length < 2) return ''
  let d = `M ${pts[0].x},${pts[0].y}`
  for (let i = 0; i < pts.length - 1; i++) {
    const step = pts[i + 1].x - pts[i].x
    const cp1x = pts[i].x + step * 0.4
    const cp1y = pts[i].y
    const cp2x = pts[i + 1].x - step * 0.4
    const cp2y = pts[i + 1].y
    d += ` C ${cp1x},${cp1y} ${cp2x},${cp2y} ${pts[i + 1].x},${pts[i + 1].y}`
  }
  return d
})

const areaPath = computed(() => {
  if (!linePath.value) return ''
  const pts = points.value
  const lastX = pts[pts.length - 1].x
  const firstX = pts[0].x
  return `${linePath.value} L ${lastX},${props.height - padding} L ${firstX},${props.height - padding} Z`
})

const totalPathLength = computed(() => {
  // Approximate path length
  const pts = points.value
  if (pts.length < 2) return 0
  let len = 0
  for (let i = 1; i < pts.length; i++) {
    const dx = pts[i].x - pts[i - 1].x
    const dy = pts[i].y - pts[i - 1].y
    len += Math.sqrt(dx * dx + dy * dy)
  }
  return Math.ceil(len)
})

const gradientId = computed(() => `sparkGrad${props.color.charAt(0).toUpperCase() + props.color.slice(1)}`)

// Hover logic
const hoveredPoint = computed(() => {
  if (hoverIndex.value === null || !points.value[hoverIndex.value]) return null
  return points.value[hoverIndex.value]
})

function onMouseMove(event: MouseEvent) {
  const svg = svgRef.value
  if (!svg || points.value.length === 0) return
  const rect = svg.getBoundingClientRect()
  const x = ((event.clientX - rect.left) / rect.width) * props.width
  mouseX.value = x

  // Find nearest point
  let nearest = 0
  let minDist = Infinity
  for (let i = 0; i < points.value.length; i++) {
    const dist = Math.abs(points.value[i].x - x)
    if (dist < minDist) {
      minDist = dist
      nearest = i
    }
  }
  hoverIndex.value = nearest
}

function onMouseLeave() {
  hoverIndex.value = null
}
</script>

<template>
  <div class="inline-block" :style="{ width: `${width}px`, height: `${height}px` }">
    <svg
      ref="svgRef"
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="sparkline-chart"
      role="img"
      aria-label="Sparkline chart"
      @mousemove="onMouseMove"
      @mouseleave="onMouseLeave"
    >
      <defs>
        <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" :stop-color="gradientStops.start" />
          <stop offset="100%" :stop-color="gradientStops.end" />
        </linearGradient>
      </defs>

      <!-- Area fill -->
      <path
        v-if="showArea && areaPath"
        :d="areaPath"
        :fill="colors.area"
        class="sparkline-area"
        :class="{ 'sparkline-area-animated': animate }"
      />

      <!-- Line -->
      <path
        v-if="linePath"
        :d="linePath"
        fill="none"
        :stroke="colors.stroke"
        stroke-width="1.5"
        stroke-linecap="round"
        stroke-linejoin="round"
        class="sparkline-line"
        :class="{ 'sparkline-line-animated': animate }"
        :style="animate ? { '--path-length': `${totalPathLength}px` } as Record<string, string> : undefined"
      />

      <!-- Data point dots -->
      <template v-if="showDots">
        <circle
          v-for="pt in points"
          :key="pt.index"
          :cx="pt.x"
          :cy="pt.y"
          r="2"
          :fill="colors.dot"
          class="sparkline-dot"
        />
      </template>

      <!-- Min/Max highlights -->
      <template v-if="showMinMax && points.length > 0">
        <circle
          v-if="points[minIndex]"
          :cx="points[minIndex].x"
          :cy="points[minIndex].y"
          r="3"
          fill="rgb(239, 68, 68)"
          stroke="white"
          stroke-width="1"
        />
        <circle
          v-if="points[maxIndex]"
          :cx="points[maxIndex].x"
          :cy="points[maxIndex].y"
          r="3"
          fill="rgb(16, 185, 129)"
          stroke="white"
          stroke-width="1"
        />
      </template>

      <!-- Hover crosshair + tooltip -->
      <template v-if="hoveredPoint">
        <line
          :x1="hoveredPoint.x"
          :y1="padding"
          :x2="hoveredPoint.x"
          :y2="height - padding"
          stroke="hsl(var(--muted-foreground) / 0.3)"
          stroke-width="1"
          stroke-dasharray="2,2"
        />
        <circle
          :cx="hoveredPoint.x"
          :cy="hoveredPoint.y"
          r="3"
          :fill="colors.stroke"
          stroke="hsl(var(--background))"
          stroke-width="1.5"
        />
        <rect
          :x="Math.max(0, Math.min(hoveredPoint.x - 18, width - 36))"
          :y="Math.max(0, hoveredPoint.y - 20)"
          width="36"
          height="14"
          rx="3"
          fill="hsl(var(--foreground))"
          opacity="0.9"
        />
        <text
          :x="Math.max(18, Math.min(hoveredPoint.x, width - 18))"
          :y="Math.max(10, hoveredPoint.y - 10)"
          text-anchor="middle"
          fill="hsl(var(--background))"
          font-size="9"
          font-weight="600"
        >
          {{ hoveredPoint.value }}
        </text>
      </template>
    </svg>
  </div>
</template>

<style scoped>
@media (prefers-reduced-motion: no-preference) {
  .sparkline-line-animated {
    stroke-dasharray: var(--path-length, 400px);
    stroke-dashoffset: var(--path-length, 400px);
    animation: sparkline-draw 800ms ease-out forwards;
  }

  @keyframes sparkline-draw {
    to {
      stroke-dashoffset: 0;
    }
  }

  .sparkline-area-animated {
    opacity: 0;
    animation: sparkline-fade 400ms ease-out 500ms forwards;
  }

  @keyframes sparkline-fade {
    from {
      opacity: 0;
    }
    to {
      opacity: 1;
    }
  }
}

.sparkline-dot {
  transition: r 150ms ease;
}

.sparkline-dot:hover {
  r: 3.5;
}
</style>
