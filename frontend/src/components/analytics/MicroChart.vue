<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

type MicroChartType = 'bar' | 'line' | 'bullet' | 'radial' | 'stacked'

interface Props {
  type: MicroChartType
  data: number[]
  width?: number
  height?: number
  color?: 'primary' | 'emerald' | 'amber' | 'rose' | 'violet' | 'cyan'
  showLabels?: boolean
  target?: number
  maxValue?: number
}

const props = withDefaults(defineProps<Props>(), {
  width: 80,
  height: 32,
  color: 'primary',
  showLabels: false,
  target: undefined,
  maxValue: undefined,
})

const { t } = useI18n()

const hoveredIndex = ref<number | null>(null)
const tooltipX = ref(0)
const tooltipY = ref(0)

const colorMap: Record<string, { fill: string; stroke: string; bg: string }> = {
  primary: { fill: 'hsl(var(--primary) / 0.6)', stroke: 'hsl(var(--primary))', bg: 'hsl(var(--primary) / 0.15)' },
  emerald: { fill: 'rgba(16, 185, 129, 0.6)', stroke: 'rgb(16, 185, 129)', bg: 'rgba(16, 185, 129, 0.15)' },
  amber: { fill: 'rgba(245, 158, 11, 0.6)', stroke: 'rgb(245, 158, 11)', bg: 'rgba(245, 158, 11, 0.15)' },
  rose: { fill: 'rgba(239, 68, 68, 0.6)', stroke: 'rgb(239, 68, 68)', bg: 'rgba(239, 68, 68, 0.15)' },
  violet: { fill: 'rgba(139, 92, 246, 0.6)', stroke: 'rgb(139, 92, 246)', bg: 'rgba(139, 92, 246, 0.15)' },
  cyan: { fill: 'rgba(6, 182, 212, 0.6)', stroke: 'rgb(6, 182, 212)', bg: 'rgba(6, 182, 212, 0.15)' },
}

const colors = computed(() => colorMap[props.color])

const computedMax = computed(() => {
  if (props.maxValue !== undefined) return props.maxValue
  const dataMax = props.data.length > 0 ? Math.max(...props.data) : 1
  return Math.max(dataMax, props.target ?? 0, 1)
})

// -- Bar chart --
const barBars = computed(() => {
  if (props.data.length === 0) return []
  const max = computedMax.value
  const gap = 2
  const barWidth = Math.max(1, (props.width - (props.data.length - 1) * gap) / props.data.length)
  return props.data.map((val, i) => {
    const h = (val / max) * props.height
    return {
      x: i * (barWidth + gap),
      y: props.height - h,
      width: barWidth,
      height: h,
      value: val,
    }
  })
})

// -- Line chart --
const linePath = computed(() => {
  if (props.data.length < 2) return ''
  const max = computedMax.value
  const step = props.width / (props.data.length - 1)
  const points = props.data.map((val, i) => ({
    x: i * step,
    y: props.height - (val / max) * props.height,
  }))

  // Build cubic bezier smooth path
  let d = `M ${points[0].x},${points[0].y}`
  for (let i = 0; i < points.length - 1; i++) {
    const cp1x = points[i].x + step * 0.4
    const cp1y = points[i].y
    const cp2x = points[i + 1].x - step * 0.4
    const cp2y = points[i + 1].y
    d += ` C ${cp1x},${cp1y} ${cp2x},${cp2y} ${points[i + 1].x},${points[i + 1].y}`
  }
  return d
})

const lineAreaPath = computed(() => {
  if (!linePath.value) return ''
  return `${linePath.value} L ${props.width},${props.height} L 0,${props.height} Z`
})

// -- Bullet chart --
const bulletGeom = computed(() => {
  const max = computedMax.value
  const actual = props.data[0] ?? 0
  const target = props.target ?? max * 0.8
  return {
    actualWidth: (actual / max) * props.width,
    targetX: (target / max) * props.width,
    actual,
    target,
    max,
  }
})

// -- Radial chart --
const radialGeom = computed(() => {
  const max = computedMax.value
  const val = props.data[0] ?? 0
  const pct = Math.min(val / max, 1)
  const r = Math.min(props.width, props.height) / 2 - 3
  const circumference = 2 * Math.PI * r
  return {
    r,
    cx: props.width / 2,
    cy: props.height / 2,
    circumference,
    dasharray: `${pct * circumference} ${circumference}`,
    pct: Math.round(pct * 100),
    value: val,
  }
})

// -- Stacked chart --
const stackedSegments = computed(() => {
  const total = props.data.reduce((a, b) => a + b, 0)
  if (total === 0) return []
  const segColors = [
    colors.value.stroke,
    colors.value.fill,
    colors.value.bg,
    'hsl(var(--muted-foreground) / 0.3)',
  ]
  let x = 0
  return props.data.map((val, i) => {
    const w = (val / total) * props.width
    const seg = { x, width: w, value: val, pct: Math.round((val / total) * 100), color: segColors[i % segColors.length] }
    x += w
    return seg
  })
})

function onBarHover(index: number, event: MouseEvent) {
  hoveredIndex.value = index
  tooltipX.value = event.offsetX
  tooltipY.value = event.offsetY - 20
}

function onBarLeave() {
  hoveredIndex.value = null
}
</script>

<template>
  <div class="inline-flex items-center" :style="{ width: `${width}px`, height: `${height}px` }">
    <!-- Bar micro chart -->
    <svg
      v-if="type === 'bar'"
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="micro-chart"
      role="img"
      :aria-label="t('common.analytics', 'Chart')"
      @mouseleave="onBarLeave"
    >
      <rect
        v-for="(bar, i) in barBars"
        :key="i"
        :x="bar.x"
        :y="bar.y"
        :width="bar.width"
        :height="bar.height"
        :fill="hoveredIndex === i ? colors.stroke : colors.fill"
        rx="1"
        class="micro-chart-bar"
        :style="{ '--bar-delay': `${i * 40}ms` }"
        @mouseenter="onBarHover(i, $event)"
      />
      <!-- Tooltip -->
      <g v-if="hoveredIndex !== null">
        <rect
          :x="Math.max(0, Math.min(tooltipX - 16, width - 32))"
          :y="Math.max(0, tooltipY - 6)"
          width="32"
          height="14"
          rx="3"
          fill="hsl(var(--foreground))"
          opacity="0.9"
        />
        <text
          :x="Math.max(16, Math.min(tooltipX, width - 16))"
          :y="Math.max(8, tooltipY + 5)"
          text-anchor="middle"
          fill="hsl(var(--background))"
          font-size="9"
          font-weight="600"
        >
          {{ barBars[hoveredIndex]?.value }}
        </text>
      </g>
    </svg>

    <!-- Line micro chart -->
    <svg
      v-else-if="type === 'line'"
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="micro-chart"
      role="img"
      :aria-label="t('common.analytics', 'Chart')"
    >
      <path
        :d="lineAreaPath"
        :fill="colors.bg"
        class="micro-chart-area"
      />
      <path
        :d="linePath"
        fill="none"
        :stroke="colors.stroke"
        stroke-width="1.5"
        stroke-linecap="round"
        class="micro-chart-line"
        :style="{ '--line-length': `${width * 2}px` }"
      />
    </svg>

    <!-- Bullet micro chart -->
    <svg
      v-else-if="type === 'bullet'"
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="micro-chart"
      role="img"
      :aria-label="t('common.analytics', 'Chart')"
    >
      <!-- Background -->
      <rect x="0" y="0" :width="width" :height="height" :fill="colors.bg" rx="3" />
      <!-- Actual bar -->
      <rect
        x="0"
        :y="height * 0.2"
        :width="bulletGeom.actualWidth"
        :height="height * 0.6"
        :fill="colors.fill"
        rx="2"
        class="micro-chart-bullet"
      />
      <!-- Target line -->
      <line
        :x1="bulletGeom.targetX"
        y1="2"
        :x2="bulletGeom.targetX"
        :y2="height - 2"
        :stroke="colors.stroke"
        stroke-width="2"
      />
      <text
        v-if="showLabels"
        :x="width / 2"
        :y="height / 2 + 3"
        text-anchor="middle"
        fill="hsl(var(--foreground))"
        font-size="9"
        font-weight="600"
      >
        {{ bulletGeom.actual }} / {{ bulletGeom.target }}
      </text>
    </svg>

    <!-- Radial micro chart -->
    <svg
      v-else-if="type === 'radial'"
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="micro-chart"
      role="img"
      :aria-label="t('common.analytics', 'Chart')"
    >
      <!-- Background circle -->
      <circle
        :cx="radialGeom.cx"
        :cy="radialGeom.cy"
        :r="radialGeom.r"
        fill="none"
        :stroke="colors.bg"
        stroke-width="3"
      />
      <!-- Progress arc -->
      <circle
        :cx="radialGeom.cx"
        :cy="radialGeom.cy"
        :r="radialGeom.r"
        fill="none"
        :stroke="colors.stroke"
        stroke-width="3"
        stroke-linecap="round"
        :stroke-dasharray="radialGeom.dasharray"
        :transform="`rotate(-90 ${radialGeom.cx} ${radialGeom.cy})`"
        class="micro-chart-radial"
      />
      <text
        v-if="showLabels && height >= 28"
        :x="radialGeom.cx"
        :y="radialGeom.cy + 3"
        text-anchor="middle"
        fill="hsl(var(--foreground))"
        font-size="8"
        font-weight="600"
      >
        {{ radialGeom.pct }}%
      </text>
    </svg>

    <!-- Stacked micro chart -->
    <svg
      v-else-if="type === 'stacked'"
      :width="width"
      :height="height"
      :viewBox="`0 0 ${width} ${height}`"
      class="micro-chart"
      role="img"
      :aria-label="t('common.analytics', 'Chart')"
    >
      <rect
        v-for="(seg, i) in stackedSegments"
        :key="i"
        :x="seg.x"
        y="0"
        :width="seg.width"
        :height="height"
        :fill="seg.color"
        :rx="i === 0 ? 3 : 0"
      />
    </svg>
  </div>
</template>

<style scoped>
@media (prefers-reduced-motion: no-preference) {
  .micro-chart-bar {
    transform-origin: bottom;
    animation: micro-bar-grow 400ms ease-out both;
    animation-delay: var(--bar-delay, 0ms);
  }

  @keyframes micro-bar-grow {
    from {
      transform: scaleY(0);
    }
    to {
      transform: scaleY(1);
    }
  }

  .micro-chart-line {
    stroke-dasharray: var(--line-length, 200px);
    stroke-dashoffset: var(--line-length, 200px);
    animation: micro-line-draw 600ms ease-out forwards;
  }

  @keyframes micro-line-draw {
    to {
      stroke-dashoffset: 0;
    }
  }

  .micro-chart-area {
    opacity: 0;
    animation: micro-fade-in 400ms ease-out 300ms forwards;
  }

  .micro-chart-bullet {
    transform-origin: left;
    animation: micro-bullet-grow 500ms ease-out forwards;
  }

  @keyframes micro-bullet-grow {
    from {
      transform: scaleX(0);
    }
    to {
      transform: scaleX(1);
    }
  }

  .micro-chart-radial {
    stroke-dashoffset: var(--radial-circumference, 200);
    animation: micro-radial-fill 700ms ease-out forwards;
  }

  @keyframes micro-radial-fill {
    from {
      stroke-dashoffset: 200;
    }
    to {
      stroke-dashoffset: 0;
    }
  }

  @keyframes micro-fade-in {
    from {
      opacity: 0;
    }
    to {
      opacity: 1;
    }
  }
}
</style>
