/**
 * Navigation Property Binding — manages child entity collections
 * through parent navigation properties.
 *
 * Features:
 * - Relative binding: parent context + nav property → child list
 * - Auto-resolve: builds correct URL for navigation queries
 * - Lazy loading: only load when visible (IntersectionObserver)
 * - Nested pagination: each composition section has its own $top/$skip
 * - Inline create/edit within parent context
 *
 * Usage:
 *   const navBinding = new NavigationBinding({
 *     parentContext: { module: 'myapp', entitySet: 'Orders', key: '123' },
 *     navigationProperty: 'Items',
 *     association: { ... },
 *     pageSize: 10,
 *   })
 *
 *   await navBinding.load()
 *   await navBinding.createChild({ product: 'Widget', qty: 5 })
 */

import { ref, computed, type Ref, type ComputedRef } from 'vue'
import { odataService } from '@/services/odataService'
import type { ODataQueryOptions } from '@/types/odata'
import type { NavigationBindingOptions } from './types'
import { buildODataFilter } from '@/utils/odataQueryBuilder'
import { createRequestScope } from '@/utils/requestDedup'

export interface NavigationBindingState<T = Record<string, unknown>> {
  /** Child data */
  data: Ref<T[]>
  /** Total count */
  totalCount: Ref<number>
  /** Loading state */
  isLoading: Ref<boolean>
  /** Error */
  error: Ref<string | null>
  /** Current page */
  currentPage: Ref<number>
  /** Page size */
  pageSize: Ref<number>
  /** Total pages */
  totalPages: ComputedRef<number>
  /** Whether lazy loading has been triggered */
  isInitialized: Ref<boolean>
}

export class NavigationBinding<T = Record<string, unknown>> {
  private options: NavigationBindingOptions
  private requestScope = createRequestScope()
  private observer: IntersectionObserver | null = null

  readonly state: NavigationBindingState<T>

  constructor(options: NavigationBindingOptions) {
    this.options = options

    this.state = {
      data: ref([]) as Ref<T[]>,
      totalCount: ref(0),
      isLoading: ref(false),
      error: ref(null),
      currentPage: ref(1),
      pageSize: ref(options.pageSize ?? 10),
      totalPages: computed(() =>
        Math.ceil(this.state.totalCount.value / this.state.pageSize.value) || 1
      ),
      isInitialized: ref(false),
    }
  }

  /**
   * Load child data from server.
   */
  async load(): Promise<void> {
    const { parentContext, navigationProperty } = this.options
    if (!parentContext.key) return

    const signal = this.requestScope.getSignal()
    this.state.isLoading.value = true
    this.state.error.value = null
    this.state.isInitialized.value = true

    try {
      const queryOptions: ODataQueryOptions = {
        $count: true,
        $top: this.state.pageSize.value,
        $skip: (this.state.currentPage.value - 1) * this.state.pageSize.value,
      }

      // Apply nested query options
      if (this.options.queryOptions?.$filter?.length) {
        queryOptions.$filter = buildODataFilter(this.options.queryOptions.$filter)
      }
      if (this.options.queryOptions?.$orderby?.length) {
        queryOptions.$orderby = this.options.queryOptions.$orderby
          .map(s => `${s.field} ${s.direction}`)
          .join(',')
      }
      if (this.options.queryOptions?.$select?.length) {
        queryOptions.$select = this.options.queryOptions.$select.join(',')
      }

      const response = await odataService.getChildren<T>(
        parentContext.module,
        parentContext.entitySet,
        parentContext.key,
        navigationProperty,
        queryOptions
      )

      if (!signal.aborted) {
        this.state.data.value = response.value
        this.state.totalCount.value = response['@odata.count'] ?? response.value.length
      }
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return
      if (signal.aborted) return
      this.state.error.value = e instanceof Error ? e.message : 'Failed to load navigation data'
    } finally {
      if (!signal.aborted) {
        this.state.isLoading.value = false
      }
    }
  }

  /**
   * Go to a specific page.
   */
  async goToPage(page: number): Promise<void> {
    if (page < 1 || page > this.state.totalPages.value) return
    this.state.currentPage.value = page
    await this.load()
  }

  /**
   * Refresh the current data.
   */
  async refresh(): Promise<void> {
    await this.load()
  }

  /**
   * Create a child entity through the navigation property.
   */
  async createChild(data: Partial<T>): Promise<T> {
    const { parentContext, navigationProperty, association } = this.options
    if (!parentContext.key) throw new Error('Parent key is required')

    // Auto-populate FK
    const payload: Record<string, unknown> = { ...data }
    if (association.foreignKey) {
      payload[association.foreignKey] = parentContext.key
    }

    // Use containment navigation URL
    const response = await odataService.create<T>(
      parentContext.module,
      `${parentContext.entitySet}/${parentContext.key}/${navigationProperty}`,
      payload as Partial<T>
    )

    // Refresh to show new item
    await this.load()
    return response
  }

  /**
   * Update a child entity.
   */
  async updateChild(childKey: string, data: Partial<T>): Promise<T> {
    const { parentContext, navigationProperty } = this.options
    if (!parentContext.key) throw new Error('Parent key is required')

    const result = await odataService.update<T>(
      parentContext.module,
      `${parentContext.entitySet}/${parentContext.key}/${navigationProperty}`,
      childKey,
      data
    )

    await this.load()
    return result
  }

  /**
   * Delete a child entity.
   */
  async deleteChild(childKey: string): Promise<void> {
    const { parentContext, navigationProperty } = this.options
    if (!parentContext.key) throw new Error('Parent key is required')

    await odataService.delete(
      parentContext.module,
      `${parentContext.entitySet}/${parentContext.key}/${navigationProperty}`,
      childKey
    )

    await this.load()
  }

  /**
   * Set up lazy loading with IntersectionObserver.
   * Data will only be loaded when the target element becomes visible.
   */
  observeVisibility(element: HTMLElement): void {
    if (this.options.lazy === false) {
      this.load()
      return
    }

    this.observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting && !this.state.isInitialized.value) {
            this.load()
            // Stop observing after first load
            this.observer?.disconnect()
          }
        }
      },
      { threshold: 0.1 }
    )

    this.observer.observe(element)
  }

  /**
   * Clean up resources.
   */
  destroy(): void {
    this.requestScope.cancel()
    this.observer?.disconnect()
  }

  /**
   * Get the binding URL for this navigation.
   */
  getUrl(): string {
    const { parentContext, navigationProperty } = this.options
    return `/odata/${parentContext.module}/${parentContext.entitySet}/${parentContext.key}/${navigationProperty}`
  }
}
