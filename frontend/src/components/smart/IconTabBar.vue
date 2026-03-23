<script setup lang="ts">
import { ref, computed, toRef, watch, onMounted, onBeforeUnmount } from 'vue'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { useIconTabBar, type IconTab, type SemanticColor } from '@/composables/useIconTabBar'
import {
  Package,
  ShoppingCart,
  Users,
  FileText,
  Settings,
  BarChart3,
  Shield,
  Globe,
  Bell,
  Mail,
  Home,
  Star,
  Heart,
  AlertCircle,
  CheckCircle,
  Clock,
  Truck,
  CreditCard,
  Database,
  Layers,
  ChevronDown,
} from 'lucide-vue-next'

interface Props {
  tabs: IconTab[]
  modelValue?: string
  mode?: 'navigation' | 'filter'
  enableOverflow?: boolean
  dense?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  mode: 'navigation',
  enableOverflow: true,
  dense: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  'tab-change': [key: string, tab: IconTab | undefined]
}>()

const iconMap: Record<string, any> = {
  Package,
  ShoppingCart,
  Users,
  FileText,
  Settings,
  BarChart3,
  Shield,
  Globe,
  Bell,
  Mail,
  Home,
  Star,
  Heart,
  AlertCircle,
  CheckCircle,
  Clock,
  Truck,
  CreditCard,
  Database,
  Layers,
}

const tabBarRef = ref<HTMLElement | null>(null)
const showOverflowMenu = ref(false)

const modelValueRef = ref(props.modelValue || '')
watch(() => props.modelValue, (val) => {
  if (val !== undefined) modelValueRef.value = val
})

const tabsRef = toRef(props, 'tabs')

const {
  activeTab,
  visibleTabs,
  overflowTabs,
  hasOverflow,
  setActiveTab: setActive,
  navigateTab,
} = useIconTabBar({
  tabs: tabsRef,
  modelValue: modelValueRef,
  enableOverflow: props.enableOverflow,
  containerRef: tabBarRef,
})

watch(activeTab, (val) => {
  emit('update:modelValue', val)
  const tab = props.tabs.find(t => t.key === val)
  emit('tab-change', val, tab)
})

const totalCount = computed(() =>
  props.tabs
    .filter(t => t.visible !== false)
    .reduce((sum, t) => sum + (t.count ?? 0), 0)
)

function semanticColorClass(color: SemanticColor): string {
  switch (color) {
    case 'positive': return 'bg-emerald-500'
    case 'negative': return 'bg-destructive'
    case 'critical': return 'bg-amber-500'
    case 'neutral': return 'bg-muted-foreground'
  }
}

function setActiveTab(key: string) {
  setActive(key)
}

function handleTabClick(tab: IconTab) {
  if (!tab.disabled) {
    setActiveTab(tab.key)
  }
}

function handleOverflowSelect(tab: IconTab) {
  if (!tab.disabled) {
    setActiveTab(tab.key)
    showOverflowMenu.value = false
  }
}

function handleKeydown(event: KeyboardEvent, _tab: IconTab) {
  switch (event.key) {
    case 'ArrowLeft':
      event.preventDefault()
      navigateTab('prev')
      break
    case 'ArrowRight':
      event.preventDefault()
      navigateTab('next')
      break
    case 'Home':
      event.preventDefault()
      navigateTab('first')
      break
    case 'End':
      event.preventDefault()
      navigateTab('last')
      break
    case 'Enter':
    case ' ':
      event.preventDefault()
      handleTabClick(_tab)
      break
  }
}

// Close overflow menu on outside click
function onDocumentClick(e: MouseEvent) {
  if (showOverflowMenu.value) {
    const target = e.target as HTMLElement
    if (!target.closest('[data-overflow-menu]')) {
      showOverflowMenu.value = false
    }
  }
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick, true)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocumentClick, true)
})

const allEnabledTabs = computed(() =>
  props.tabs.filter(t => t.visible !== false)
)

const tabPadding = computed(() => props.dense ? 'px-3 py-1.5 text-xs' : 'px-4 py-2.5 text-sm')
</script>

<template>
  <div>
    <!-- Tab bar -->
    <div
      ref="tabBarRef"
      class="flex items-stretch border-b bg-background overflow-hidden"
      role="tablist"
    >
      <!-- Filter mode: "All" tab -->
      <button
        v-if="mode === 'filter'"
        role="tab"
        :aria-selected="activeTab === '__all__'"
        :tabindex="activeTab === '__all__' ? 0 : -1"
        :class="cn(
          'relative flex items-center gap-2 font-medium whitespace-nowrap',
          'transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2',
          'focus-visible:ring-ring focus-visible:ring-offset-1',
          tabPadding,
          activeTab === '__all__' ? 'text-primary' : 'text-muted-foreground'
        )"
        @click="setActiveTab('__all__')"
        @keydown="handleKeydown($event, { key: '__all__', label: 'All' })"
      >
        <span>All</span>
        <Badge variant="secondary" class="ml-1.5">{{ totalCount }}</Badge>
        <span
          v-if="activeTab === '__all__'"
          class="absolute bottom-0 left-0 right-0 h-0.5 bg-primary"
        />
      </button>

      <!-- Tab items -->
      <button
        v-for="tab in visibleTabs"
        :key="tab.key"
        data-tab-item
        role="tab"
        :aria-selected="activeTab === tab.key"
        :aria-controls="'panel-' + tab.key"
        :tabindex="activeTab === tab.key ? 0 : -1"
        :disabled="tab.disabled"
        :class="cn(
          'relative flex items-center gap-2 font-medium whitespace-nowrap',
          'transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2',
          'focus-visible:ring-ring focus-visible:ring-offset-1',
          'disabled:opacity-50 disabled:cursor-not-allowed',
          tabPadding,
          activeTab === tab.key ? 'text-primary' : 'text-muted-foreground'
        )"
        @click="handleTabClick(tab)"
        @keydown="handleKeydown($event, tab)"
      >
        <component
          v-if="tab.icon && iconMap[tab.icon]"
          :is="iconMap[tab.icon]"
          class="h-4 w-4"
        />
        <slot :name="'tab-label-' + tab.key" :tab="tab">
          <span>{{ tab.label }}</span>
        </slot>
        <Badge
          v-if="tab.count !== undefined"
          variant="secondary"
          :class="cn(
            'ml-1',
            tab.semanticColor === 'negative' ? 'bg-destructive/10 text-destructive' : ''
          )"
        >
          {{ tab.count }}
        </Badge>
        <span
          v-if="tab.semanticColor"
          class="absolute bottom-0 left-2 right-2 h-0.5 rounded-full"
          :class="semanticColorClass(tab.semanticColor)"
        />
        <span
          v-if="activeTab === tab.key"
          class="absolute bottom-0 left-0 right-0 h-0.5 bg-primary"
        />
      </button>

      <!-- Overflow "More" button -->
      <div v-if="hasOverflow" class="relative" data-overflow-menu>
        <button
          :class="cn(
            'flex items-center gap-1 text-muted-foreground hover:text-foreground',
            tabPadding
          )"
          @click.stop="showOverflowMenu = !showOverflowMenu"
        >
          More
          <ChevronDown class="h-3 w-3" />
        </button>
        <div
          v-if="showOverflowMenu"
          class="absolute right-0 top-full mt-1 w-48 rounded-md border bg-popover py-1 shadow-md z-50"
        >
          <button
            v-for="tab in overflowTabs"
            :key="tab.key"
            :disabled="tab.disabled"
            class="flex w-full items-center gap-2 px-3 py-1.5 text-sm hover:bg-accent disabled:opacity-50 disabled:cursor-not-allowed"
            :class="activeTab === tab.key ? 'text-primary font-medium' : ''"
            @click="handleOverflowSelect(tab)"
          >
            <component
              v-if="tab.icon && iconMap[tab.icon]"
              :is="iconMap[tab.icon]"
              class="h-4 w-4"
            />
            {{ tab.label }}
            <Badge v-if="tab.count !== undefined" variant="secondary" class="ml-auto">
              {{ tab.count }}
            </Badge>
          </button>
        </div>
      </div>
    </div>

    <!-- Tab content panels -->
    <div :class="dense ? 'py-2' : 'py-4'">
      <template v-if="mode === 'filter' && activeTab === '__all__'">
        <slot
          v-for="tab in allEnabledTabs"
          :key="tab.key"
          :name="'tab-' + tab.key"
        />
      </template>
      <template v-else>
        <div
          v-for="tab in allEnabledTabs"
          :key="tab.key"
          v-show="activeTab === tab.key"
          :id="'panel-' + tab.key"
          role="tabpanel"
          :aria-labelledby="'tab-' + tab.key"
        >
          <slot :name="'tab-' + tab.key" />
        </div>
      </template>
    </div>
  </div>
</template>
