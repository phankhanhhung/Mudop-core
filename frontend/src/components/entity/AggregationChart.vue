<script setup lang="ts">
import { computed } from 'vue'
import { Bar, Pie, Line, Doughnut } from 'vue-chartjs'
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  ArcElement,
  PointElement,
  LineElement,
  Filler,
  Title,
  Tooltip,
  Legend
} from 'chart.js'
import { BarChart3, PieChart, TrendingUp, Circle, AreaChart } from 'lucide-vue-next'
import type { AggregationResult, ChartType } from '@/types/aggregation'

ChartJS.register(
  CategoryScale,
  LinearScale,
  BarElement,
  ArcElement,
  PointElement,
  LineElement,
  Filler,
  Title,
  Tooltip,
  Legend
)

interface Props {
  results: AggregationResult
  chartType: ChartType
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:chartType': [type: ChartType]
}>()

// Tailwind-friendly color palette
const COLORS = [
  'rgba(59, 130, 246, 0.7)',   // blue-500
  'rgba(16, 185, 129, 0.7)',   // emerald-500
  'rgba(245, 158, 11, 0.7)',   // amber-500
  'rgba(239, 68, 68, 0.7)',    // red-500
  'rgba(139, 92, 246, 0.7)',   // violet-500
  'rgba(236, 72, 153, 0.7)',   // pink-500
  'rgba(20, 184, 166, 0.7)',   // teal-500
  'rgba(249, 115, 22, 0.7)',   // orange-500
  'rgba(99, 102, 241, 0.7)',   // indigo-500
  'rgba(168, 85, 247, 0.7)'    // purple-500
]

const BORDER_COLORS = [
  'rgba(59, 130, 246, 1)',
  'rgba(16, 185, 129, 1)',
  'rgba(245, 158, 11, 1)',
  'rgba(239, 68, 68, 1)',
  'rgba(139, 92, 246, 1)',
  'rgba(236, 72, 153, 1)',
  'rgba(20, 184, 166, 1)',
  'rgba(249, 115, 22, 1)',
  'rgba(99, 102, 241, 1)',
  'rgba(168, 85, 247, 1)'
]

const FILL_COLORS = [
  'rgba(59, 130, 246, 0.15)',
  'rgba(16, 185, 129, 0.15)',
  'rgba(245, 158, 11, 0.15)',
  'rgba(239, 68, 68, 0.15)',
  'rgba(139, 92, 246, 0.15)',
  'rgba(236, 72, 153, 0.15)',
  'rgba(20, 184, 166, 0.15)',
  'rgba(249, 115, 22, 0.15)',
  'rgba(99, 102, 241, 0.15)',
  'rgba(168, 85, 247, 0.15)'
]

function getColors(count: number): string[] {
  const result: string[] = []
  for (let i = 0; i < count; i++) {
    result.push(COLORS[i % COLORS.length])
  }
  return result
}

function getBorderColors(count: number): string[] {
  const result: string[] = []
  for (let i = 0; i < count; i++) {
    result.push(BORDER_COLORS[i % BORDER_COLORS.length])
  }
  return result
}

const hasMultipleSeries = computed(() => {
  const keys = Object.keys(props.results.series)
  return keys.length > 1
})

const seriesKeys = computed(() => Object.keys(props.results.series))

const chartData = computed(() => {
  const count = props.results.labels.length
  const isRound = props.chartType === 'pie' || props.chartType === 'doughnut'
  const isArea = props.chartType === 'area'

  if (isRound) {
    return {
      labels: props.results.labels,
      datasets: [
        {
          data: props.results.values,
          backgroundColor: getColors(count),
          borderColor: getBorderColors(count),
          borderWidth: 2
        }
      ]
    }
  }

  // For bar/line/area, support multiple series
  if (hasMultipleSeries.value) {
    return {
      labels: props.results.labels,
      datasets: seriesKeys.value.map((key, i) => ({
        label: key,
        data: props.results.series[key],
        backgroundColor: isArea ? FILL_COLORS[i % FILL_COLORS.length] : COLORS[i % COLORS.length],
        borderColor: BORDER_COLORS[i % BORDER_COLORS.length],
        borderWidth: 2,
        fill: isArea,
        tension: isArea ? 0.4 : 0,
        pointBackgroundColor: BORDER_COLORS[i % BORDER_COLORS.length],
        pointRadius: 4,
        pointHoverRadius: 6
      }))
    }
  }

  return {
    labels: props.results.labels,
    datasets: [
      {
        label: seriesKeys.value[0] || 'Value',
        data: props.results.values,
        backgroundColor: isArea ? FILL_COLORS[0] : getColors(count),
        borderColor: isArea ? BORDER_COLORS[0] : getBorderColors(count),
        borderWidth: 2,
        fill: isArea,
        tension: isArea ? 0.4 : 0,
        pointBackgroundColor: BORDER_COLORS[0],
        pointRadius: 4,
        pointHoverRadius: 6
      }
    ]
  }
})

const chartOptions = computed(() => {
  const isRound = props.chartType === 'pie' || props.chartType === 'doughnut'

  return {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        display: isRound || hasMultipleSeries.value,
        position: isRound ? 'right' as const : 'top' as const,
        labels: {
          padding: 16,
          usePointStyle: true,
          pointStyle: 'circle'
        }
      },
      tooltip: {
        backgroundColor: 'rgba(0, 0, 0, 0.8)',
        titleFont: { size: 13, weight: 'bold' as const },
        bodyFont: { size: 12 },
        padding: 12,
        cornerRadius: 8,
        displayColors: true
      }
    },
    scales: isRound
      ? undefined
      : {
          x: {
            grid: {
              display: false
            },
            ticks: {
              maxRotation: 45,
              font: { size: 11 }
            }
          },
          y: {
            beginAtZero: true,
            grid: {
              color: 'rgba(0, 0, 0, 0.06)'
            },
            ticks: {
              font: { size: 11 }
            }
          }
        }
  }
})

const chartTypes: { type: ChartType; label: string; icon: string }[] = [
  { type: 'bar', label: 'Bar', icon: 'bar' },
  { type: 'line', label: 'Line', icon: 'line' },
  { type: 'area', label: 'Area', icon: 'area' },
  { type: 'pie', label: 'Pie', icon: 'pie' },
  { type: 'doughnut', label: 'Donut', icon: 'doughnut' }
]
</script>

<template>
  <div>
    <!-- Chart type toggle -->
    <div class="flex items-center gap-1 mb-4 p-1 bg-muted rounded-lg w-fit">
      <button
        v-for="ct in chartTypes"
        :key="ct.type"
        class="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-all"
        :class="chartType === ct.type
          ? 'bg-background text-foreground shadow-sm'
          : 'text-muted-foreground hover:text-foreground'"
        @click="emit('update:chartType', ct.type)"
      >
        <BarChart3 v-if="ct.icon === 'bar'" class="h-3.5 w-3.5" />
        <TrendingUp v-else-if="ct.icon === 'line'" class="h-3.5 w-3.5" />
        <AreaChart v-else-if="ct.icon === 'area'" class="h-3.5 w-3.5" />
        <PieChart v-else-if="ct.icon === 'pie'" class="h-3.5 w-3.5" />
        <Circle v-else class="h-3.5 w-3.5" />
        {{ ct.label }}
      </button>
    </div>

    <!-- Chart -->
    <div class="h-[400px] relative">
      <Bar
        v-if="chartType === 'bar'"
        :data="chartData"
        :options="chartOptions"
      />
      <Pie
        v-else-if="chartType === 'pie'"
        :data="chartData"
        :options="chartOptions"
      />
      <Doughnut
        v-else-if="chartType === 'doughnut'"
        :data="chartData"
        :options="chartOptions"
      />
      <Line
        v-else
        :data="chartData"
        :options="chartOptions"
      />
    </div>
  </div>
</template>
