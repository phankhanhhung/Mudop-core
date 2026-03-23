import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref, nextTick } from 'vue'
import type { EntityMetadata } from '@/types/metadata'

vi.mock('@/services/layoutService', () => ({
  layoutService: {
    getLayout: vi.fn(),
    saveLayout: vi.fn(),
    resetLayout: vi.fn(),
  },
}))

import { useFormLayoutDesigner } from '../useFormLayoutDesigner'
import { layoutService } from '@/services/layoutService'

const mockMeta: EntityMetadata = {
  name: 'Customer',
  namespace: 'crm',
  fields: [
    { name: 'ID', type: 'UUID', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
    { name: 'Name', type: 'String', isRequired: true, isReadOnly: false, isComputed: false, annotations: {} },
    { name: 'Email', type: 'String', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
    { name: 'Status', type: 'Enum', isRequired: false, isReadOnly: false, isComputed: false, annotations: {} },
  ],
  keys: ['ID'],
  associations: [],
  annotations: {},
}

describe('useFormLayoutDesigner', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    vi.mocked(layoutService.getLayout).mockResolvedValue(null)
    vi.mocked(layoutService.saveLayout).mockResolvedValue('new-id')
    vi.mocked(layoutService.resetLayout).mockResolvedValue(undefined)
  })

  function makeDesigner(ns = 'crm', en = 'Customer', meta: EntityMetadata | null = mockMeta) {
    return useFormLayoutDesigner(ref(ns), ref(en), ref(meta))
  }

  it('builds default layout from metadata when no saved layout', async () => {
    const { layout } = makeDesigner()
    await nextTick()
    await nextTick() // watch + async load
    expect(layout.value).not.toBeNull()
    expect(layout.value!.sections.length).toBeGreaterThan(0)
    expect(layout.value!.version).toBe(1)
  })

  it('separates key fields into their own section', async () => {
    const { layout } = makeDesigner()
    await nextTick(); await nextTick()
    const keysSection = layout.value!.sections.find((s) => s.id === 'keys')
    expect(keysSection).toBeDefined()
    expect(keysSection!.fields.some((f) => f.name === 'ID')).toBe(true)
  })

  it('palette fields are empty when all fields are placed', async () => {
    const { paletteFields } = makeDesigner()
    await nextTick(); await nextTick()
    expect(paletteFields.value.length).toBe(0) // default puts all fields in sections
  })

  it('hideField removes field from section', async () => {
    const { layout, hideField, paletteFields } = makeDesigner()
    await nextTick(); await nextTick()
    const section = layout.value!.sections.find((s) => s.id === 'general')!
    const fieldName = section.fields[0].name
    hideField(section.id, fieldName)
    expect(layout.value!.sections.find((s) => s.id === section.id)!.fields.some((f) => f.name === fieldName)).toBe(false)
    expect(paletteFields.value.some((f) => f.name === fieldName)).toBe(true)
  })

  it('addSection creates a new section', async () => {
    const { layout, addSection } = makeDesigner()
    await nextTick(); await nextTick()
    const before = layout.value!.sections.length
    addSection()
    expect(layout.value!.sections.length).toBe(before + 1)
  })

  it('removeSection deletes the section', async () => {
    const { layout, removeSection } = makeDesigner()
    await nextTick(); await nextTick()
    const id = layout.value!.sections[0].id
    removeSection(id)
    expect(layout.value!.sections.some((s) => s.id === id)).toBe(false)
  })

  it('renameSection updates the title', async () => {
    const { layout, renameSection } = makeDesigner()
    await nextTick(); await nextTick()
    const id = layout.value!.sections[0].id
    renameSection(id, 'My Custom Title')
    expect(layout.value!.sections.find((s) => s.id === id)!.title).toBe('My Custom Title')
  })

  it('setFieldWidth updates the width', async () => {
    const { layout, setFieldWidth } = makeDesigner()
    await nextTick(); await nextTick()
    const section = layout.value!.sections.find((s) => s.id === 'general')!
    const fieldName = section.fields[0].name
    setFieldWidth(section.id, fieldName, 'third')
    const field = layout.value!.sections.find((s) => s.id === section.id)!.fields.find((f) => f.name === fieldName)
    expect(field!.width).toBe('third')
  })

  it('setColumns updates columns setting', async () => {
    const { layout, setColumns } = makeDesigner()
    await nextTick(); await nextTick()
    setColumns(3)
    expect(layout.value!.columns).toBe(3)
  })

  it('isDirty becomes true after a mutation', async () => {
    const { isDirty, addSection } = makeDesigner()
    await nextTick(); await nextTick()
    expect(isDirty.value).toBe(false)
    addSection()
    expect(isDirty.value).toBe(true)
  })

  it('saveLayout calls layoutService and clears isDirty', async () => {
    const { isDirty, addSection, saveLayout } = makeDesigner()
    await nextTick(); await nextTick()
    addSection()
    await saveLayout()
    expect(layoutService.saveLayout).toHaveBeenCalled()
    expect(isDirty.value).toBe(false)
  })

  it('moveFieldToSection moves field across sections', async () => {
    const { layout, hideField, moveFieldToSection, addSection } = makeDesigner()
    await nextTick(); await nextTick()
    // Add a new section and move a field into it
    addSection()
    const newSectionId = layout.value!.sections[layout.value!.sections.length - 1].id
    const generalSection = layout.value!.sections.find((s) => s.id === 'general')!
    const fieldName = generalSection.fields[0].name
    hideField('general', fieldName)
    moveFieldToSection(fieldName, newSectionId)
    expect(layout.value!.sections.find((s) => s.id === newSectionId)!.fields.some((f) => f.name === fieldName)).toBe(true)
  })

  it('reorderSections swaps section order', async () => {
    const { layout, reorderSections } = makeDesigner()
    await nextTick(); await nextTick()
    const sections = layout.value!.sections
    if (sections.length < 2) return
    const firstId = sections[0].id
    const secondId = sections[1].id
    reorderSections(0, 1)
    expect(layout.value!.sections[0].id).toBe(secondId)
    expect(layout.value!.sections[1].id).toBe(firstId)
  })
})
