import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface TreeNode {
  [key: string]: unknown
  children?: TreeNode[]
}

export interface FlatTreeRow {
  data: Record<string, unknown>
  key: string
  depth: number
  isExpanded: boolean
  hasChildren: boolean
  isLastChild: boolean
}

export interface UseTreeTableOptions {
  treeData: Ref<TreeNode[]>
  getRowKey: (row: Record<string, unknown>) => string
  initialExpanded?: boolean
}

export interface UseTreeTableReturn {
  flatRows: ComputedRef<FlatTreeRow[]>
  expandedKeys: Ref<Set<string>>
  toggleExpand: (key: string) => void
  expandAll: () => void
  collapseAll: () => void
  isExpanded: (key: string) => boolean
}

export function useTreeTable(options: UseTreeTableOptions): UseTreeTableReturn {
  const { treeData, getRowKey, initialExpanded = false } = options
  const expandedKeys = ref<Set<string>>(new Set())

  function collectAllKeys(nodes: TreeNode[]): string[] {
    const keys: string[] = []
    for (const node of nodes) {
      keys.push(getRowKey(node as Record<string, unknown>))
      if (node.children?.length) {
        keys.push(...collectAllKeys(node.children))
      }
    }
    return keys
  }

  if (initialExpanded) {
    expandedKeys.value = new Set(collectAllKeys(treeData.value))
  }

  const flatRows = computed<FlatTreeRow[]>(() => {
    const result: FlatTreeRow[] = []

    function walk(nodes: TreeNode[], depth: number) {
      for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i]
        const key = getRowKey(node as Record<string, unknown>)
        const hasChildren = !!(node.children && node.children.length > 0)
        const isExpanded = expandedKeys.value.has(key)
        const isLastChild = i === nodes.length - 1

        result.push({
          data: node as Record<string, unknown>,
          key,
          depth,
          isExpanded,
          hasChildren,
          isLastChild,
        })

        if (hasChildren && isExpanded) {
          walk(node.children!, depth + 1)
        }
      }
    }

    walk(treeData.value, 0)
    return result
  })

  function toggleExpand(key: string) {
    const next = new Set(expandedKeys.value)
    if (next.has(key)) {
      next.delete(key)
    } else {
      next.add(key)
    }
    expandedKeys.value = next
  }

  function expandAll() {
    expandedKeys.value = new Set(collectAllKeys(treeData.value))
  }

  function collapseAll() {
    expandedKeys.value = new Set()
  }

  function isExpanded(key: string): boolean {
    return expandedKeys.value.has(key)
  }

  return {
    flatRows,
    expandedKeys,
    toggleExpand,
    expandAll,
    collapseAll,
    isExpanded,
  }
}
