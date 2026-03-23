<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

export interface BarChartItem {
  label: string
  value: number
  color?: string
}

interface Props {
  data: BarChartItem[]
  title?: string
  height?: number
  orientation?: 'vertical' | 'horizontal'
  showValues?: boolean
  showGrid?: boolean
  maxBars?: number
  animate?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  height: 300,
  orientation: 'vertical',
  showValues: true,
  showGrid: true,
  maxBars: 20,
  animate: true,
})

const { t } = useI18n()

const hoveredIndex = ref<number | null>(null)
const tooltipPos = ref({ x: 0, y: 0 })

const DEFAULT_COLORS = [
  'hsl(var(--primary))',
  'rgb(16, 185, 129)',
  'rgb(245, 158, 11)',
  'rgb(239, 68, 68)',
  'rgb(139, 92, 246)',
  'rgb(6, 182, 212)',
  'rgb(236, 72, 153)',
  'rgb(249, 115, 22)',
  'rgb(99, 102, 241)',
  'rgb(168, 85, 247)',
]

const truncatedData = computed(() => props.data.slice(0, props.maxBars))

const maxValue = computed(() => {
  const max = truncatedData.value.length > 0 ? Math.max(...truncatedData.value.map((d) => d.value)) : 1
  return Math.max(max, 1)
})

// Y-axis tick marks (4-5 nice values)
const yTicks = computed(() => {
  const max = maxValue.value
  const rawStep = max / 4
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)))
  const niceStep = Math.ceil(rawStep / magnitude) * magnitude
  const ticks: number[] = []
  for (let v = 0; v <= max + niceStep * 0.1; v += niceStep) {
    ticks.push(Math.round(v))
    if (ticks.length >= 6) break
  }
  return ticks
})

// SVG dimensions
const MARGIN = { top: 20, right: 16, bottom: 48, left: 56 }
const svgWidth = 600

const innerWidth = computed(() => svgWidth - MARGIN.left - MARGIN.right)
const innerHeight = computed(() => props.height - MARGIN.top - MARGIN.bottom)

// Vertical bar geometry
const vertBars = computed(() => {
  const items = truncatedData.value
  if (items.length === 0) return []
  const gap = Math.max(2, Math.min(8, innerWidth.value / items.length * 0.15))
  const barW = Math.max(4, (innerWidth.value - (items.length - 1) * gap) / items.length)
  const max = maxValue.value
  return items.map((item, i) => {
    const h = (item.value / max) * innerHeight.value
    return {
      x: MARGIN.left + i * (barW + gap),
      y: MARGIN.top + innerHeight.value - h,
      width: barW,
      height: h,
      color: item.color ?? DEFAULT_COLORS[i % DEFAULT_COLORS.length],
      label: item.label,
      value: item.value,
    }
  })
})

// Horizontal bar geometry
const horizBars = computed(() => {
  const items = truncatedData.value
  if (items.length === 0) return []
  const gap = Math.max(2, Math.min(6, innerHeight.value / items.length * 0.1))
  const barH = Math.max(4, (innerHeight.value - (items.length - 1) * gap) / items.length)
  const max = maxValue.value
  return items.map((item, i) => {
    const w = (item.value / max) * innerWidth.value
    return {
      x: MARGIN.left,
      y: MARGIN.top + i * (barH + gap),
      width: w,
      height: barH,
      color: item.color ?? DEFAULT_COLORS[i % DEFAULT_COLORS.length],
      label: item.label,
      value: item.value,
    }
  })
})

const bars = computed(() => (props.orientation === 'vertical' ? vertBars.value : horizBars.value))

function formatValue(val: number): string {
  if (val >= 1_000_000) return `${(val / 1_000_000).toFixed(1)}M`
  if (val >= 1_000) return `${(val / 1_000).toFixed(1)}K`
  return String(val)
}

function onBarHover(index: number, event: MouseEvent) {
  hoveredIndex.value = index
  const target = event.currentTarget as SVGElement
  const svg = target.closest('svg')
  if (svg) {
    const rect = svg.getBoundingClientRect()
    tooltipPos.value = {
      x: event.clientX - rect.left,
      y: event.clientY - rect.top,
    }
  }
}

function onBarLeave() {
  hoveredIndex.value = null
}

// Label truncation for x-axis
function truncateLabel(label: string, maxLen: number): string {
  return label.length > maxLen ? label.slice(0, maxLen - 1) + '\u2026' : label
}
</script>

<template>
  <div class="w-full">
    <h3 v-if="title" class="text-sm font-semibold text-foreground mb-3">{{ title }}</h3>
    <div class="relative w-full overflow-x-auto">
      <svg
        :viewBox="`0 0 ${svgWidth} ${height}`"
        class="w-full bar-chart"
        :style="{ minHeight: `${height}px` }"
        role="img"
        :aria-label="title ?? t('common.analytics', 'Bar chart')"
        @mouseleave="onBarLeave"
      >
        <!-- Grid lines (vertical orientation) -->
        <template v-if="showGrid && orientation === 'vertical'">
          <line
            v-for="tick in yTicks"
            :key="tick"
            :x1="MARGIN.left"
            :y1="MARGIN.top + innerHeight - (tick / maxValue) * innerHeight"
            :x2="MARGIN.left + innerWidth"
            :y2="MARGIN.top + innerHeight - (tick / maxValue) * innerHeight"
            stroke="hsl(var(--border))"
            stroke-width="0.5"
            stroke-dasharray="4,4"
          />
        </template>

        <!-- Y-axis labels (vertical orientation) -->
        <template v-if="orientation === 'vertical'">
          <text
            v-for="tick in yTicks"
            :key="'yLabel' + tick"
            :x="MARGIN.left - 8"
            :y="MARGIN.top + innerHeight - (tick / maxValue) * innerHeight + 4"
            text-anchor="end"
            fill="hsl(var(--muted-foreground))"
            font-size="10"
          >
            {{ formatValue(tick) }}
          </text>
        </template>

        <!-- Bars -->
        <rect
          v-for="(bar, i) in bars"
          :key="i"
          :x="bar.x"
          :y="bar.y"
          :width="bar.width"
          :height="Math.max(bar.height, 1)"
          :fill="hoveredIndex === i ? bar.color : bar.color"
          :opacity="hoveredIndex === null || hoveredIndex === i ? 1 : 0.5"
          :rx="2"
          class="bar-chart-bar transition-opacity duration-150"
          :class="{ 'bar-chart-bar-animated': animate }"
          :style="{
            '--bar-delay': `${i * 30}ms`,
            '--bar-origin': orientation === 'vertical' ? 'bottom' : 'left',
          }"
          @mouseenter="onBarHover(i, $event)"
        />

        <!-- Value labels on bars (vertical) -->
        <template v-if="showValues && orientation === 'vertical'">
          <text
            v-for="(bar, i) in bars"
            :key="'val' + i"
            :x="bar.x + bar.width / 2"
            :y="bar.y - 4"
            text-anchor="middle"
            fill="hsl(var(--muted-foreground))"
            font-size="9"
            font-weight="500"
            :opacity="hoveredIndex === null || hoveredIndex === i ? 1 : 0.3"
            class="transition-opacity duration-150"
          >
            {{ formatValue(bar.value) }}
          </text>
        </template>

        <!-- Value labels on bars (horizontal) -->
        <template v-if="showValues && orientation === 'horizontal'">
          <text
            v-for="(bar, i) in bars"
            :key="'hval' + i"
            :x="bar.x + bar.width + 6"
            :y="bar.y + bar.height / 2 + 3"
            text-anchor="start"
            fill="hsl(var(--muted-foreground))"
            font-size="9"
            font-weight="500"
            :opacity="hoveredIndex === null || hoveredIndex === i ? 1 : 0.3"
            class="transition-opacity duration-150"
          >
            {{ formatValue(bar.value) }}
          </text>
        </template>

        <!-- X-axis labels (vertical orientation) -->
        <template v-if="orientation === 'vertical'">
          <text
            v-for="(bar, i) in bars"
            :key="'xlabel' + i"
            :x="bar.x + bar.width / 2"
            :y="MARGIN.top + innerHeight + 16"
            text-anchor="middle"
            fill="hsl(var(--muted-foreground))"
            font-size="9"
            :transform="
              truncatedData.length > 8
                ? `rotate(-35, ${bar.x + bar.width / 2}, ${MARGIN.top + innerHeight + 16})`
                : undefined
            "
          >
            {{ truncateLabel(bar.label, truncatedData.length > 10 ? 8 : 14) }}
          </text>
        </template>

        <!-- Y-axis labels (horizontal orientation) -->
        <template v-if="orientation === 'horizontal'">
          <text
            v-for="(bar, i) in bars"
            :key="'ylabel' + i"
            :x="MARGIN.left - 8"
            :y="bar.y + bar.height / 2 + 3"
            text-anchor="end"
            fill="hsl(var(--muted-foreground))"
            font-size="9"
          >
            {{ truncateLabel(bar.label, 12) }}
          </text>
        </template>

        <!-- Axis lines -->
        <line
          :x1="MARGIN.left"
          :y1="MARGIN.top"
          :x2="MARGIN.left"
          :y2="MARGIN.top + innerHeight"
          stroke="hsl(var(--border))"
          stroke-width="1"
        />
        <line
          :x1="MARGIN.left"
          :y1="MARGIN.top + innerHeight"
          :x2="MARGIN.left + innerWidth"
          :y2="MARGIN.top + innerHeight"
          stroke="hsl(var(--border))"
          stroke-width="1"
        />

        <!-- Hover tooltip -->
        <g v-if="hoveredIndex !== null && bars[hoveredIndex]">
          <rect
            :x="Math.max(0, Math.min(tooltipPos.x - 40, svgWidth - 80))"
            :y="Math.max(0, tooltipPos.y - 36)"
            width="80"
            height="30"
            rx="4"
            fill="hsl(var(--foreground))"
            opacity="0.92"
          />
          <text
            :x="Math.max(40, Math.min(tooltipPos.x, svgWidth - 40))"
            :y="Math.max(14, tooltipPos.y - 22)"
            text-anchor="middle"
            fill="hsl(var(--background))"
            font-size="9"
            font-weight="400"
          >
            {{ bars[hoveredIndex].label }}
          </text>
          <text
            :x="Math.max(40, Math.min(tooltipPos.x, svgWidth - 40))"
            :y="Math.max(26, tooltipPos.y - 10)"
            text-anchor="middle"
            fill="hsl(var(--background))"
            font-size="11"
            font-weight="700"
          >
            {{ bars[hoveredIndex].value.toLocaleString() }}
          </text>
        </g>
      </svg>
    </div>
  </div>
</template>

<style scoped>
@media (prefers-reduced-motion: no-preference) {
  .bar-chart-bar-animated {
    animation: bar-grow 500ms ease-out both;
    animation-delay: var(--bar-delay, 0ms);
  }

  .bar-chart-bar-animated[style*="--bar-origin: bottom"] {
    transform-origin: center bottom;
  }

  .bar-chart-bar-animated[style*="--bar-origin: left"] {
    transform-origin: left center;
  }

  @keyframes bar-grow {
    from {
      transform: scale(0);
    }
    to {
      transform: scale(1);
    }
  }
}
</style>
