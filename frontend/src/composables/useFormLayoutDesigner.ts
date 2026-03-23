import { ref, computed, watch, type Ref } from 'vue'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import type { FormLayoutSettings, FormLayoutSection, DesignerField, DesignerSection } from '@/types/formLayout'
import { layoutService, type SavedLayout } from '@/services/layoutService'

const SYSTEM_FIELDS_LOWER = new Set([
  'id', 'createdat', 'updatedat', 'createdby', 'updatedby',
  'tenantid', 'systemstart', 'systemend', 'version',
  'deletedat', 'isdeleted', 'deletedby',
])

function isSystemField(name: string): boolean {
  return SYSTEM_FIELDS_LOWER.has(name.toLowerCase())
}

function toDesignerField(f: FieldMetadata, keys: Set<string>, width: 'full' | 'half' | 'third' = 'full'): DesignerField {
  return {
    name: f.name,
    displayName: f.displayName ?? f.name,
    type: f.type,
    isRequired: f.isRequired,
    isKey: keys.has(f.name),
    width,
  }
}

function buildDefaultLayout(metadata: EntityMetadata): FormLayoutSettings {
  const keys = new Set(metadata.keys ?? [])
  const allFields = (metadata.fields ?? []).filter((f) => !isSystemField(f.name))

  const keyFields = allFields.filter((f) => keys.has(f.name))
  const assocFields = allFields.filter((f) => f.type === 'UUID' && !keys.has(f.name))
  const generalFields = allFields.filter((f) => !keys.has(f.name) && f.type !== 'UUID')

  const sections: FormLayoutSection[] = []

  if (keyFields.length > 0) {
    sections.push({
      id: 'keys',
      title: 'Key Fields',
      collapsed: false,
      fields: keyFields.map((f) => ({ name: f.name, width: 'full', visible: true })),
    })
  }

  if (generalFields.length > 0) {
    sections.push({
      id: 'general',
      title: 'General Information',
      collapsed: false,
      fields: generalFields.map((f) => ({ name: f.name, width: 'half', visible: true })),
    })
  }

  if (assocFields.length > 0) {
    sections.push({
      id: 'associations',
      title: 'Associations',
      collapsed: false,
      fields: assocFields.map((f) => ({ name: f.name, width: 'full', visible: true })),
    })
  }

  return { version: 1, sections, columns: 2 }
}

export function useFormLayoutDesigner(
  namespace: Ref<string>,
  entityName: Ref<string>,
  metadata: Ref<EntityMetadata | null>,
) {
  // ─── Layout list management ─────────────────────────────────────────────────
  const savedLayouts = ref<SavedLayout[]>([])
  const activeLayoutId = ref<string | null>(null)
  const activeLayoutName = ref('')

  const layout = ref<FormLayoutSettings | null>(null)
  const isLoading = ref(false)
  const isSaving = ref(false)
  const isDirty = ref(false)
  const error = ref<string | null>(null)

  // ─── Computed: all field names currently placed in any section ───────────────
  const usedFieldNames = computed<Set<string>>(() => {
    if (!layout.value) return new Set()
    return new Set(
      layout.value.sections.flatMap((s) => s.fields.filter((f) => f.visible).map((f) => f.name)),
    )
  })

  // ─── Computed: palette fields (unplaced) ────────────────────────────────────
  const paletteFields = computed<DesignerField[]>(() => {
    if (!metadata.value || !layout.value) return []
    const keys = new Set(metadata.value.keys ?? [])
    return (metadata.value.fields ?? [])
      .filter((f) => !isSystemField(f.name) && !usedFieldNames.value.has(f.name))
      .map((f) => toDesignerField(f, keys))
  })

  // ─── Computed: designer sections ────────────────────────────────────────────
  const designerSections = computed<DesignerSection[]>(() => {
    if (!metadata.value || !layout.value) return []
    const keys = new Set(metadata.value.keys ?? [])
    const fieldMap = new Map((metadata.value.fields ?? []).map((f) => [f.name, f]))

    return layout.value.sections.map((s): DesignerSection => ({
      id: s.id,
      title: s.title,
      icon: s.icon,
      collapsed: s.collapsed,
      fields: s.fields
        .filter((f) => f.visible)
        .map((f) => {
          const meta = fieldMap.get(f.name)
          if (!meta) return null
          return toDesignerField(meta, keys, f.width)
        })
        .filter((f): f is DesignerField => f !== null),
    }))
  })

  // ─── Load all layouts for entity ────────────────────────────────────────────
  async function loadLayouts(): Promise<void> {
    if (!namespace.value || !entityName.value || !metadata.value) return
    isLoading.value = true
    error.value = null
    try {
      savedLayouts.value = await layoutService.listLayouts(namespace.value, entityName.value)
      if (savedLayouts.value.length > 0) {
        // Select first layout
        selectLayout(savedLayouts.value[0].id)
      } else {
        // No saved layouts — start with default
        activeLayoutId.value = null
        activeLayoutName.value = ''
        layout.value = buildDefaultLayout(metadata.value)
      }
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load layouts'
      layout.value = metadata.value ? buildDefaultLayout(metadata.value) : null
    } finally {
      isLoading.value = false
      isDirty.value = false
    }
  }

  // ─── Select a layout from the list ──────────────────────────────────────────
  function selectLayout(id: string): void {
    const found = savedLayouts.value.find((l) => l.id === id)
    if (!found) return
    activeLayoutId.value = found.id
    activeLayoutName.value = found.name
    layout.value = JSON.parse(JSON.stringify(found.settings))
    isDirty.value = false
  }

  // ─── Start new layout (from default) ───────────────────────────────────────
  function newLayout(): void {
    if (!metadata.value) return
    activeLayoutId.value = null
    activeLayoutName.value = ''
    layout.value = buildDefaultLayout(metadata.value)
    isDirty.value = true
  }

  // ─── Save layout (create or update) ────────────────────────────────────────
  async function saveLayout(name?: string): Promise<void> {
    if (!layout.value || !namespace.value || !entityName.value) return
    isSaving.value = true
    error.value = null
    const layoutName = name ?? (activeLayoutName.value || 'Untitled Layout')
    try {
      const id = await layoutService.saveLayout(
        namespace.value,
        entityName.value,
        layout.value,
        activeLayoutId.value ?? undefined,
        layoutName,
      )
      activeLayoutId.value = id
      activeLayoutName.value = layoutName
      isDirty.value = false
      // Refresh list
      savedLayouts.value = await layoutService.listLayouts(namespace.value, entityName.value)
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to save layout'
      throw e
    } finally {
      isSaving.value = false
    }
  }

  // ─── Save as new copy ──────────────────────────────────────────────────────
  async function saveAsNew(name: string): Promise<void> {
    if (!layout.value || !namespace.value || !entityName.value) return
    isSaving.value = true
    error.value = null
    try {
      const id = await layoutService.saveLayout(
        namespace.value,
        entityName.value,
        layout.value,
        undefined, // force create new
        name,
      )
      activeLayoutId.value = id
      activeLayoutName.value = name
      isDirty.value = false
      savedLayouts.value = await layoutService.listLayouts(namespace.value, entityName.value)
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to save layout'
      throw e
    } finally {
      isSaving.value = false
    }
  }

  // ─── Delete layout ─────────────────────────────────────────────────────────
  async function deleteLayout(id: string): Promise<void> {
    await layoutService.deleteLayout(id)
    savedLayouts.value = savedLayouts.value.filter((l) => l.id !== id)
    if (activeLayoutId.value === id) {
      if (savedLayouts.value.length > 0) {
        selectLayout(savedLayouts.value[0].id)
      } else {
        activeLayoutId.value = null
        activeLayoutName.value = ''
        if (metadata.value) {
          layout.value = buildDefaultLayout(metadata.value)
        }
        isDirty.value = false
      }
    }
  }

  // ─── Rename layout ─────────────────────────────────────────────────────────
  async function renameLayout(id: string, name: string): Promise<void> {
    await layoutService.renameLayout(id, name)
    const item = savedLayouts.value.find((l) => l.id === id)
    if (item) item.name = name
    if (activeLayoutId.value === id) activeLayoutName.value = name
  }

  // ─── Reset to default ────────────────────────────────────────────────────────
  async function resetToDefault(): Promise<void> {
    if (metadata.value) {
      layout.value = buildDefaultLayout(metadata.value)
    }
    isDirty.value = true
  }

  // ─── Mutations ───────────────────────────────────────────────────────────────
  function reorderSections(from: number, to: number): void {
    if (!layout.value) return
    const s = [...layout.value.sections]
    const [moved] = s.splice(from, 1)
    s.splice(to, 0, moved)
    layout.value = { ...layout.value, sections: s }
    isDirty.value = true
  }

  function reorderFieldsInSection(sectionId: string, from: number, to: number): void {
    if (!layout.value) return
    const sections = layout.value.sections.map((s) => {
      if (s.id !== sectionId) return s
      const fields = [...s.fields]
      const [moved] = fields.splice(from, 1)
      fields.splice(to, 0, moved)
      return { ...s, fields }
    })
    layout.value = { ...layout.value, sections }
    isDirty.value = true
  }

  function moveFieldToSection(fieldName: string, targetSectionId: string): void {
    if (!layout.value) return
    const sections = layout.value.sections.map((s) => {
      if (s.id === targetSectionId) {
        const existing = s.fields.find((f) => f.name === fieldName)
        if (existing) {
          return { ...s, fields: s.fields.map((f) => f.name === fieldName ? { ...f, visible: true } : f) }
        }
        return { ...s, fields: [...s.fields, { name: fieldName, width: 'full' as const, visible: true }] }
      }
      return { ...s, fields: s.fields.filter((f) => f.name !== fieldName) }
    })
    layout.value = { ...layout.value, sections }
    isDirty.value = true
  }

  function hideField(sectionId: string, fieldName: string): void {
    if (!layout.value) return
    const sections = layout.value.sections.map((s) => {
      if (s.id !== sectionId) return s
      return { ...s, fields: s.fields.filter((f) => f.name !== fieldName) }
    })
    layout.value = { ...layout.value, sections }
    isDirty.value = true
  }

  function addSection(): void {
    if (!layout.value) return
    const id = `section-${Date.now()}`
    layout.value = {
      ...layout.value,
      sections: [...layout.value.sections, { id, title: 'New Section', collapsed: false, fields: [] }],
    }
    isDirty.value = true
  }

  function removeSection(sectionId: string): void {
    if (!layout.value) return
    layout.value = {
      ...layout.value,
      sections: layout.value.sections.filter((s) => s.id !== sectionId),
    }
    isDirty.value = true
  }

  function renameSection(sectionId: string, title: string): void {
    if (!layout.value) return
    const sections = layout.value.sections.map((s) =>
      s.id === sectionId ? { ...s, title } : s,
    )
    layout.value = { ...layout.value, sections }
    isDirty.value = true
  }

  function setFieldWidth(sectionId: string, fieldName: string, width: 'full' | 'half' | 'third'): void {
    if (!layout.value) return
    const sections = layout.value.sections.map((s) => {
      if (s.id !== sectionId) return s
      return { ...s, fields: s.fields.map((f) => f.name === fieldName ? { ...f, width } : f) }
    })
    layout.value = { ...layout.value, sections }
    isDirty.value = true
  }

  function setColumns(columns: 1 | 2 | 3): void {
    if (!layout.value) return
    layout.value = { ...layout.value, columns }
    isDirty.value = true
  }

  // ─── Auto-load when inputs change ────────────────────────────────────────────
  watch(
    [namespace, entityName, metadata],
    ([ns, en, meta]) => {
      if (ns && en && meta) loadLayouts()
      else {
        layout.value = null
        savedLayouts.value = []
        activeLayoutId.value = null
        activeLayoutName.value = ''
      }
    },
    { immediate: true },
  )

  return {
    // Layout list
    savedLayouts,
    activeLayoutId,
    activeLayoutName,
    // Current layout
    layout,
    isLoading,
    isSaving,
    isDirty,
    error,
    paletteFields,
    designerSections,
    // Actions
    loadLayouts,
    selectLayout,
    newLayout,
    saveLayout,
    saveAsNew,
    deleteLayout,
    renameLayout,
    resetToDefault,
    // Mutations
    reorderSections,
    reorderFieldsInSection,
    moveFieldToSection,
    hideField,
    addSection,
    removeSection,
    renameSection,
    setFieldWidth,
    setColumns,
  }
}
