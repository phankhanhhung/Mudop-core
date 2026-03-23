import { ref, computed, watch, onMounted, onBeforeUnmount, type Ref, type ComputedRef } from 'vue'

export type SemanticColor = 'positive' | 'negative' | 'critical' | 'neutral'

export interface IconTab {
  key: string
  label: string
  icon?: string
  count?: number
  semanticColor?: SemanticColor
  disabled?: boolean
  visible?: boolean
}

export interface UseIconTabBarOptions {
  tabs: Ref<IconTab[]>
  modelValue?: Ref<string>
  enableOverflow?: boolean
  containerRef?: Ref<HTMLElement | null>
}

export interface UseIconTabBarReturn {
  activeTab: Ref<string>
  visibleTabs: ComputedRef<IconTab[]>
  overflowTabs: ComputedRef<IconTab[]>
  hasOverflow: ComputedRef<boolean>
  setActiveTab: (key: string) => void
  navigateTab: (direction: 'prev' | 'next' | 'first' | 'last') => void
}

export function useIconTabBar(options: UseIconTabBarOptions): UseIconTabBarReturn {
  const { tabs, modelValue, enableOverflow = false, containerRef } = options

  const activeTab = ref(modelValue?.value || tabs.value[0]?.key || '')
  const overflowIndex = ref<number>(tabs.value.length)

  if (modelValue) {
    watch(modelValue, (val) => { activeTab.value = val })
    watch(activeTab, (val) => { modelValue.value = val })
  }

  const enabledTabs = computed(() => tabs.value.filter(t => t.visible !== false))

  const visibleTabs = computed(() => {
    if (!enableOverflow) return enabledTabs.value
    return enabledTabs.value.slice(0, overflowIndex.value)
  })

  const overflowTabs = computed(() => {
    if (!enableOverflow) return []
    return enabledTabs.value.slice(overflowIndex.value)
  })

  const hasOverflow = computed(() => overflowTabs.value.length > 0)

  let resizeObserver: ResizeObserver | null = null

  function calculateOverflow() {
    if (!enableOverflow || !containerRef?.value) {
      overflowIndex.value = enabledTabs.value.length
      return
    }

    const container = containerRef.value
    const containerWidth = container.clientWidth
    const moreButtonWidth = 80
    const tabElements = container.querySelectorAll<HTMLElement>('[data-tab-item]')

    let totalWidth = 0
    let cutoffIndex = enabledTabs.value.length

    for (let i = 0; i < tabElements.length; i++) {
      totalWidth += tabElements[i].offsetWidth
      if (totalWidth > containerWidth - moreButtonWidth) {
        cutoffIndex = i
        break
      }
    }

    overflowIndex.value = cutoffIndex
  }

  onMounted(() => {
    if (enableOverflow && containerRef?.value) {
      resizeObserver = new ResizeObserver(() => {
        calculateOverflow()
      })
      resizeObserver.observe(containerRef.value)
      calculateOverflow()
    }
  })

  onBeforeUnmount(() => {
    resizeObserver?.disconnect()
  })

  function setActiveTab(key: string) {
    const tab = enabledTabs.value.find(t => t.key === key)
    if (tab && !tab.disabled) {
      activeTab.value = key
    }
  }

  function navigateTab(direction: 'prev' | 'next' | 'first' | 'last') {
    const available = enabledTabs.value.filter(t => !t.disabled)
    if (available.length === 0) return

    const currentIdx = available.findIndex(t => t.key === activeTab.value)

    switch (direction) {
      case 'prev':
        setActiveTab(available[(currentIdx - 1 + available.length) % available.length].key)
        break
      case 'next':
        setActiveTab(available[(currentIdx + 1) % available.length].key)
        break
      case 'first':
        setActiveTab(available[0].key)
        break
      case 'last':
        setActiveTab(available[available.length - 1].key)
        break
    }
  }

  return {
    activeTab,
    visibleTabs,
    overflowTabs,
    hasOverflow,
    setActiveTab,
    navigateTab,
  }
}
