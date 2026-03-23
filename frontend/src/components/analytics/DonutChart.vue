<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

export interface DonutChartItem {
  label: string
  value: number
  color: string
}

interface Props {
  data: DonutChartItem[]
  title?: string
  size?: number
  donut?: boolean
  showLegend?: boolean
  showTotal?: boolean
  animate?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  size: 200,
  donut: true,
  showLegend: true,
  showTotal: true,
  animate: true,
})

const { t } = useI18n()

const hoveredIndex = ref<number | null>(null)

const total = computed(() => props.data.reduce((sum, d) => sum + d.value, 0))

const cx = computed(() => props.size / 2)
const cy = computed(() => props.size / 2)
const outerR = computed(() => props.size / 2 - 4)
const innerR = computed(() => (props.donut ? outerR.value * 0.6 : 0))

// Compute arc segments
const segments = computed(() => {
  if (total.value === 0) return []
  const result: {
    startAngle: number
    endAngle: number
    largeArc: boolean
    path: string
    color: string
    label: string
    value: number
    pct: number
    midAngle: number
  }[] = []

  let currentAngle = -Math.PI / 2 // Start from top

  for (const item of props.data) {
    if (item.value <= 0) continue
    const pct = item.value / total.value
    const angle = pct * 2 * Math.PI
    const startAngle = currentAngle
    const endAngle = currentAngle + angle
    const largeArc = angle > Math.PI
    const midAngle = startAngle + angle / 2

    let path: string
    if (props.donut) {
      // Donut: annular sector
      const outerStartX = cx.value + outerR.value * Math.cos(startAngle)
      const outerStartY = cy.value + outerR.value * Math.sin(startAngle)
      const outerEndX = cx.value + outerR.value * Math.cos(endAngle)
      const outerEndY = cy.value + outerR.value * Math.sin(endAngle)
      const innerStartX = cx.value + innerR.value * Math.cos(endAngle)
      const innerStartY = cy.value + innerR.value * Math.sin(endAngle)
      const innerEndX = cx.value + innerR.value * Math.cos(startAngle)
      const innerEndY = cy.value + innerR.value * Math.sin(startAngle)

      path = [
        `M ${outerStartX},${outerStartY}`,
        `A ${outerR.value},${outerR.value} 0 ${largeArc ? 1 : 0} 1 ${outerEndX},${outerEndY}`,
        `L ${innerStartX},${innerStartY}`,
        `A ${innerR.value},${innerR.value} 0 ${largeArc ? 1 : 0} 0 ${innerEndX},${innerEndY}`,
        'Z',
      ].join(' ')
    } else {
      // Pie: sector from center
      const startX = cx.value + outerR.value * Math.cos(startAngle)
      const startY = cy.value + outerR.value * Math.sin(startAngle)
      const endX = cx.value + outerR.value * Math.cos(endAngle)
      const endY = cy.value + outerR.value * Math.sin(endAngle)

      path = [
        `M ${cx.value},${cy.value}`,
        `L ${startX},${startY}`,
        `A ${outerR.value},${outerR.value} 0 ${largeArc ? 1 : 0} 1 ${endX},${endY}`,
        'Z',
      ].join(' ')
    }

    result.push({
      startAngle,
      endAngle,
      largeArc,
      path,
      color: item.color,
      label: item.label,
      value: item.value,
      pct: Math.round(pct * 100),
      midAngle,
    })

    currentAngle = endAngle
  }

  return result
})

const formattedTotal = computed(() => {
  if (total.value >= 1_000_000) return `${(total.value / 1_000_000).toFixed(1)}M`
  if (total.value >= 1_000) return `${(total.value / 1_000).toFixed(1)}K`
  return total.value.toLocaleString()
})

function onSegmentHover(index: number) {
  hoveredIndex.value = index
}

function onSegmentLeave() {
  hoveredIndex.value = null
}
</script>

<template>
  <div class="inline-flex flex-col items-center">
    <h3 v-if="title" class="text-sm font-semibold text-foreground mb-3">{{ title }}</h3>

    <div class="flex items-start gap-6" :class="{ 'flex-col items-center': !showLegend }">
      <!-- SVG Chart -->
      <div class="relative shrink-0" :style="{ width: `${size}px`, height: `${size}px` }">
        <svg
          :width="size"
          :height="size"
          :viewBox="`0 0 ${size} ${size}`"
          class="donut-chart"
          role="img"
          :aria-label="title ?? t('common.analytics', 'Chart')"
          @mouseleave="onSegmentLeave"
        >
          <!-- Empty state ring -->
          <circle
            v-if="segments.length === 0"
            :cx="cx"
            :cy="cy"
            :r="outerR - (donut ? (outerR - innerR) / 2 : 0)"
            fill="none"
            stroke="hsl(var(--border))"
            :stroke-width="donut ? outerR - innerR : 1"
          />

          <!-- Segments -->
          <path
            v-for="(seg, i) in segments"
            :key="i"
            :d="seg.path"
            :fill="seg.color"
            :opacity="hoveredIndex === null || hoveredIndex === i ? 1 : 0.45"
            :stroke="hoveredIndex === i ? 'hsl(var(--background))' : 'hsl(var(--background) / 0.6)'"
            :stroke-width="hoveredIndex === i ? 2 : 1"
            class="donut-segment transition-all duration-150 cursor-pointer"
            :class="{ 'donut-segment-animated': animate }"
            :style="{ '--seg-delay': `${i * 80}ms` }"
            @mouseenter="onSegmentHover(i)"
          />

          <!-- Center total for donut -->
          <template v-if="donut && showTotal">
            <text
              :x="cx"
              :y="cy - 4"
              text-anchor="middle"
              fill="hsl(var(--muted-foreground))"
              font-size="10"
            >
              {{ t('analytics.totalLabel', 'Total') }}
            </text>
            <text
              :x="cx"
              :y="cy + 12"
              text-anchor="middle"
              fill="hsl(var(--foreground))"
              font-size="16"
              font-weight="700"
            >
              {{ formattedTotal }}
            </text>
          </template>

          <!-- Hover tooltip -->
          <g v-if="hoveredIndex !== null && segments[hoveredIndex]">
            <rect
              :x="cx - 40"
              :y="donut ? cy - 36 : cy - 16"
              width="80"
              height="28"
              rx="4"
              fill="hsl(var(--foreground))"
              opacity="0.92"
              v-if="!donut"
            />
            <text
              v-if="!donut"
              :x="cx"
              :y="cy - 4"
              text-anchor="middle"
              fill="hsl(var(--background))"
              font-size="9"
            >
              {{ segments[hoveredIndex].label }}
            </text>
            <text
              v-if="!donut"
              :x="cx"
              :y="cy + 8"
              text-anchor="middle"
              fill="hsl(var(--background))"
              font-size="11"
              font-weight="700"
            >
              {{ segments[hoveredIndex].value.toLocaleString() }} ({{ segments[hoveredIndex].pct }}%)
            </text>
          </g>
        </svg>
      </div>

      <!-- Legend -->
      <div v-if="showLegend && segments.length > 0" class="flex flex-col gap-1.5 min-w-[120px]">
        <div
          v-for="(seg, i) in segments"
          :key="i"
          class="flex items-center gap-2 text-xs cursor-pointer rounded px-1.5 py-0.5 transition-colors"
          :class="hoveredIndex === i ? 'bg-muted' : ''"
          @mouseenter="onSegmentHover(i)"
          @mouseleave="onSegmentLeave"
        >
          <span
            class="w-2.5 h-2.5 rounded-sm shrink-0"
            :style="{ backgroundColor: seg.color }"
          />
          <span class="text-foreground truncate max-w-[100px]">{{ seg.label }}</span>
          <span class="text-muted-foreground ml-auto tabular-nums">
            {{ seg.value.toLocaleString() }}
          </span>
          <span class="text-muted-foreground/60 tabular-nums">({{ seg.pct }}%)</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
@media (prefers-reduced-motion: no-preference) {
  .donut-segment-animated {
    opacity: 0;
    animation: donut-appear 300ms ease-out forwards;
    animation-delay: var(--seg-delay, 0ms);
  }

  @keyframes donut-appear {
    from {
      opacity: 0;
      transform: scale(0.92);
    }
    to {
      opacity: 1;
      transform: scale(1);
    }
  }
}

.donut-segment {
  transform-origin: center;
}
</style>
