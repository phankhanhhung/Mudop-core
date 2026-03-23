import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { computed, ref } from 'vue'

// vi.mock declarations must come before all imports that use them (hoisted by Vitest).
// IMPORTANT: vi.mock factories are hoisted to the top of the file, so they CANNOT
// reference variables declared later in the module body. Use vi.fn() inline and
// access the mocked functions via vi.mocked(useSmartForm) after import.

vi.mock('@/composables/useSmartForm', () => ({
  useSmartForm: vi.fn(() => ({
    sections: computed(() => [
      {
        id: 'general',
        title: 'General Information',
        fields: [],
        collapsed: false,
      }
    ]),
    formData: ref({}),
    isDirty: ref(false),
    dirtyFields: ref(new Set<string>()),
    fieldErrors: ref({}),
    sectionErrors: computed(() => ({})),
    updateField: vi.fn(),
    resetForm: vi.fn(),
    validate: vi.fn().mockReturnValue({ valid: true, errors: {} }),
    getSubmitData: vi.fn().mockReturnValue({ Name: 'Test' }),
  }))
}))

vi.mock('@/components/smart/FieldGroup.vue', () => ({
  default: {
    template: '<div data-testid="field-group"><slot /></div>',
    props: ['title', 'description', 'fieldCount', 'errorCount', 'collapsed', 'icon'],
  },
}))

vi.mock('@/components/smart/SmartField.vue', () => ({
  default: {
    template: '<input data-testid="smart-field" :data-field="field && field.name" />',
    props: ['field', 'modelValue', 'mode', 'module', 'entitySet', 'association', 'error'],
    emits: ['update:modelValue'],
  },
}))

vi.mock('@/components/entity/CompositionFormRows.vue', () => ({
  default: {
    template: '<div data-testid="composition-rows"></div>',
    props: ['association', 'childMetadata', 'parentEntity', 'modelValue'],
    emits: ['update:modelValue'],
  },
}))

// Mock UI components
vi.mock('@/components/ui/button', () => ({
  Button: {
    template: '<button :type="type" :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
    props: ['type', 'variant', 'disabled'],
    emits: ['click'],
  },
}))

vi.mock('@/components/ui/alert', () => ({
  Alert: { template: '<div role="alert" class="alert"><slot /></div>', props: ['variant'] },
  AlertTitle: { template: '<div class="alert-title"><slot /></div>' },
  AlertDescription: { template: '<div class="alert-description"><slot /></div>' },
}))

vi.mock('@/components/ui/spinner', () => ({
  Spinner: { template: '<div data-testid="spinner" />', props: ['size', 'class'] },
}))

vi.mock('lucide-vue-next', () => ({
  Key: { template: '<span data-icon="Key" />' },
  FileText: { template: '<span data-icon="FileText" />' },
  Link2: { template: '<span data-icon="Link2" />' },
  MoreHorizontal: { template: '<span data-icon="MoreHorizontal" />' },
}))

import SmartForm from '../SmartForm.vue'
import { useSmartForm } from '@/composables/useSmartForm'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeField(overrides: Partial<FieldMetadata> = {}): FieldMetadata {
  return {
    name: 'Name',
    type: 'String',
    displayName: 'Name',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
    ...overrides,
  }
}

function createMetadata(fields: FieldMetadata[] = []): EntityMetadata {
  return {
    name: 'Customer',
    namespace: 'sales',
    fields: [
      makeField({ name: 'ID', type: 'UUID', displayName: 'ID' }),
      makeField({ name: 'Name', type: 'String', displayName: 'Name', isRequired: true }),
      makeField({ name: 'Status', type: 'String', displayName: 'Status' }),
      ...fields,
    ],
    keys: ['ID'],
    associations: [],
    annotations: {},
  }
}

function mountSmartForm(propOverrides: Record<string, unknown> = {}) {
  return mount(SmartForm, {
    props: {
      module: 'sales',
      entitySet: 'Customer',
      metadata: createMetadata(),
      mode: 'create',
      ...propOverrides,
    },
    global: {
      mocks: { $t: (k: string) => k },
    },
  })
}

// Helper to get the mocked useSmartForm return value for the last call
function getMockedFormReturn() {
  const mockedFn = vi.mocked(useSmartForm)
  return mockedFn.mock.results[mockedFn.mock.results.length - 1]?.value as ReturnType<typeof useSmartForm>
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SmartForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()

    // Re-configure the mock after clearAllMocks resets it to no-op
    vi.mocked(useSmartForm).mockImplementation(() => ({
      sections: computed(() => [
        {
          id: 'general',
          title: 'General Information',
          fields: [],
          collapsed: false,
        }
      ]),
      formData: ref({}),
      isDirty: ref(false),
      dirtyFields: ref(new Set<string>()),
      fieldErrors: ref({}),
      sectionErrors: computed(() => ({})),
      updateField: vi.fn(),
      resetForm: vi.fn(),
      validate: vi.fn().mockReturnValue({ valid: true, errors: {} }),
      getSubmitData: vi.fn().mockReturnValue({ Name: 'Test' }),
    }))
  })

  // 1. renders without error with metadata and mode=create
  it('renders without error with metadata and mode=create', () => {
    const wrapper = mountSmartForm({ mode: 'create' })
    expect(wrapper.exists()).toBe(true)
    expect(wrapper.find('form').exists()).toBe(true)
  })

  // 2. renders submit button when showActions=true
  it('renders submit button when showActions=true', () => {
    const wrapper = mountSmartForm({ mode: 'create', showActions: true })
    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(true)
  })

  // 3. hides action buttons when showActions=false
  it('hides action buttons when showActions=false', () => {
    const wrapper = mountSmartForm({ mode: 'create', showActions: false })
    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(false)
  })

  // 4. shows error alert when error prop is set
  it('shows error alert when error prop is set', () => {
    const wrapper = mountSmartForm({ error: 'Something went wrong' })
    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('Something went wrong')
  })

  // 5. hides error alert when error is null
  it('hides error alert when error is null', () => {
    const wrapper = mountSmartForm({ error: null })
    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(false)
  })

  // 6. shows spinner when isLoading=true
  it('shows spinner when isLoading=true', () => {
    const wrapper = mountSmartForm({ isLoading: true })
    const spinner = wrapper.find('[data-testid="spinner"]')
    expect(spinner.exists()).toBe(true)
  })

  // 7. hides spinner when isLoading=false
  it('hides spinner when isLoading=false', () => {
    const wrapper = mountSmartForm({ isLoading: false })
    const spinner = wrapper.find('[data-testid="spinner"]')
    expect(spinner.exists()).toBe(false)
  })

  // 8. submit button is disabled when isLoading=true
  it('submit button is disabled when isLoading=true', () => {
    const wrapper = mountSmartForm({ mode: 'create', isLoading: true })
    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(true)
    expect(submitBtn.attributes('disabled')).toBeDefined()
  })

  // 9. clicking submit emits submit event
  it('clicking submit emits submit event', async () => {
    const submittedData = { Name: 'Alice' }

    vi.mocked(useSmartForm).mockImplementation(() => ({
      sections: computed(() => [{ id: 'general', title: 'General Information', fields: [], collapsed: false }]),
      formData: ref({}),
      isDirty: ref(false),
      dirtyFields: ref(new Set<string>()),
      fieldErrors: ref({}),
      sectionErrors: computed(() => ({})),
      updateField: vi.fn(),
      resetForm: vi.fn(),
      validate: vi.fn().mockReturnValue({ valid: true, errors: {} }),
      getSubmitData: vi.fn().mockReturnValue(submittedData),
    }))

    const wrapper = mountSmartForm({ mode: 'create' })

    const form = wrapper.find('form')
    await form.trigger('submit')

    expect(wrapper.emitted('submit')).toBeTruthy()
    const emittedArgs = wrapper.emitted('submit')![0] as [Record<string, unknown>]
    expect(emittedArgs[0]).toEqual(submittedData)
  })

  // 10. clicking cancel emits cancel event
  it('clicking cancel emits cancel event', async () => {
    const wrapper = mountSmartForm({ mode: 'create' })

    // Cancel button is type="button"
    const cancelBtn = wrapper.find('button[type="button"]')
    expect(cancelBtn.exists()).toBe(true)
    await cancelBtn.trigger('click')

    expect(wrapper.emitted('cancel')).toBeTruthy()
    expect(wrapper.emitted('cancel')!.length).toBe(1)
  })

  // 11. display mode renders without submit/cancel buttons
  it('display mode renders without submit/cancel buttons', () => {
    const wrapper = mountSmartForm({ mode: 'display' })

    // v-if="showActions && mode !== 'display'" hides the action bar entirely
    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(false)

    const cancelBtn = wrapper.find('button[type="button"]')
    expect(cancelBtn.exists()).toBe(false)
  })

  // 12. uses custom submitLabel when provided
  it('uses custom submitLabel when provided', () => {
    const wrapper = mountSmartForm({ mode: 'create', submitLabel: 'Save Record' })
    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(true)
    expect(submitBtn.text()).toContain('Save Record')
  })
})
