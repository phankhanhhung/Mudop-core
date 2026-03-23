import { ref, computed, watch } from 'vue'
import { metadataService } from '@/services/metadataService'
import type { EntityMetadata, ModuleMetadata } from '@/types/metadata'

// ── Public types ──────────────────────────────────────────────────────────────

export interface ErdNode {
  id: string            // fully qualified: 'namespace.Name' or just 'Name'
  name: string          // short entity name
  namespace: string     // module/namespace
  moduleName: string    // which module this entity belongs to
  fieldCount: number
  keyFields: string[]
  isAbstract: boolean
  // Force layout state (mutable)
  x: number
  y: number
  vx: number
  vy: number
  width: number
  height: number
  pinned: boolean
}

export interface ErdEdge {
  id: string            // unique: sourceId + '->' + targetId + ':' + assocName
  sourceId: string      // ErdNode.id
  targetId: string      // ErdNode.id
  label: string         // cardinality label e.g. '[0..1]'
  isComposition: boolean
  associationName: string
}

// ── Cardinality label mapping ─────────────────────────────────────────────────

function cardinalityLabel(cardinality: string): string {
  switch (cardinality) {
    case 'ZeroOrOne':  return '[0..1]'
    case 'One':        return '[1..1]'
    case 'Many':       return '[0..*]'
    case 'OneOrMore':  return '[1..*]'
    default:           return cardinality
  }
}

// ── Node ID helper ────────────────────────────────────────────────────────────

function entityNodeId(namespace: string, name: string): string {
  return namespace ? `${namespace}.${name}` : name
}

// ── Composable ────────────────────────────────────────────────────────────────

export function useEntityGraph() {
  const modules = ref<ModuleMetadata[]>([])
  const allEntities = ref<Array<{ moduleName: string; entity: EntityMetadata }>>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  // Filter state
  const selectedModules = ref<string[]>([])  // empty = show all

  // Graph output (mutable refs for force simulation)
  const nodes = ref<ErdNode[]>([])
  const edges = ref<ErdEdge[]>([])

  async function loadModules() {
    isLoading.value = true
    error.value = null
    try {
      modules.value = await metadataService.getModules()
      // Default: select all modules
      if (selectedModules.value.length === 0) {
        selectedModules.value = modules.value.map(m => m.name)
      }
    } catch (err) {
      error.value = err instanceof Error ? err.message : String(err)
    } finally {
      isLoading.value = false
    }
  }

  async function loadEntitiesForModule(moduleName: string) {
    const entities = await metadataService.getEntities(moduleName)
    // Remove any existing entries for this module and add fresh ones
    allEntities.value = [
      ...allEntities.value.filter(e => e.moduleName !== moduleName),
      ...entities.map(entity => ({ moduleName, entity })),
    ]
  }

  async function loadAll() {
    isLoading.value = true
    error.value = null
    try {
      await loadModules()
      await Promise.all(modules.value.map(m => loadEntitiesForModule(m.name)))
    } catch (err) {
      error.value = err instanceof Error ? err.message : String(err)
    } finally {
      isLoading.value = false
    }
  }

  // Filtered entities based on selectedModules
  const visibleEntities = computed(() =>
    allEntities.value.filter(e =>
      selectedModules.value.length === 0 || selectedModules.value.includes(e.moduleName)
    )
  )

  function buildGraph() {
    const NODE_WIDTH = 160
    const NODE_HEIGHT = 56

    // Preserve existing positions to avoid jitter on filter change
    const existingPositions = new Map<string, { x: number; y: number }>()
    for (const n of nodes.value) {
      existingPositions.set(n.id, { x: n.x, y: n.y })
    }

    const seen = new Set<string>()
    const newNodes: ErdNode[] = []
    const count = visibleEntities.value.length
    const radius = Math.max(200, count * 50)

    visibleEntities.value.forEach(({ moduleName, entity }, i) => {
      const id = entityNodeId(entity.namespace, entity.name)
      if (seen.has(id)) return
      seen.add(id)
      const existing = existingPositions.get(id)
      const angle = (2 * Math.PI * i) / Math.max(1, count)
      newNodes.push({
        id,
        name: entity.name,
        namespace: entity.namespace ?? '',
        moduleName,
        fieldCount: entity.fields?.length ?? 0,
        keyFields: entity.keys ?? [],
        isAbstract: entity.isAbstract ?? false,
        x: existing?.x ?? radius * Math.cos(angle) + (Math.random() - 0.5) * 20,
        y: existing?.y ?? radius * Math.sin(angle) + (Math.random() - 0.5) * 20,
        vx: 0,
        vy: 0,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        pinned: false,
      })
    })
    nodes.value = newNodes

    // Build edges
    const nodeIds = new Set(newNodes.map(n => n.id))
    const newEdges: ErdEdge[] = []
    const edgeSeen = new Set<string>()

    for (const { entity } of visibleEntities.value) {
      if (!entity.associations) continue
      const sourceId = entityNodeId(entity.namespace, entity.name)
      for (const assoc of entity.associations) {
        if (!assoc.targetEntity) continue
        const targetId = assoc.targetEntity
        if (!nodeIds.has(targetId)) continue
        const edgeKey = `${sourceId}→${targetId}:${assoc.name}`
        if (edgeSeen.has(edgeKey)) continue
        edgeSeen.add(edgeKey)
        newEdges.push({
          id: edgeKey,
          sourceId,
          targetId,
          label: cardinalityLabel(assoc.cardinality),
          isComposition: assoc.isComposition,
          associationName: assoc.name,
        })
      }
    }
    edges.value = newEdges
  }

  // Rebuild graph whenever visible entity set changes
  watch(visibleEntities, buildGraph, { deep: false })

  function toggleModule(moduleName: string) {
    const idx = selectedModules.value.indexOf(moduleName)
    if (idx === -1) {
      selectedModules.value.push(moduleName)
    } else {
      selectedModules.value.splice(idx, 1)
    }
  }

  function selectOnlyModule(moduleName: string) {
    selectedModules.value = [moduleName]
  }

  function selectAllModules() {
    selectedModules.value = modules.value.map(m => m.name)
  }

  return {
    modules,
    allEntities,
    isLoading,
    error,
    selectedModules,
    visibleEntities,
    nodes,
    edges,
    loadModules,
    loadEntitiesForModule,
    loadAll,
    toggleModule,
    selectOnlyModule,
    selectAllModules,
  }
}
