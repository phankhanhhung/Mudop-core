import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'

// vi.mock declarations must come before all imports that use them

vi.mock('../EntityField.vue', () => ({
  default: {
    name: 'EntityField',
    template: '<div data-testid="entity-field" />',
    props: ['field', 'modelValue', 'mode', 'readonly', 'association', 'currentModule']
  }
}))

vi.mock('../CompositionFormRows.vue', () => ({
  default: {
    name: 'CompositionFormRows',
    template: '<div data-testid="composition-rows" />'
  }
}))

vi.mock('../fields/FileReferenceField.vue', () => ({
  default: {
    name: 'FileReferenceField',
    template: '<div data-testid="file-reference-field" />'
  }
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useRoute: () => ({ params: {} })
}))

// Mock validateFormData to allow controlling validation behavior in tests
const mockValidateFormData = vi.fn()
vi.mock('@/utils/formValidator', () => ({
  validateFormData: (...args: unknown[]) => mockValidateFormData(...args)
}))

vi.mock('@/utils/odataErrorParser', () => ({
  getFirstFieldError: vi.fn().mockReturnValue(null)
}))

import EntityForm from '../EntityForm.vue'
import type { FieldMetadata } from '@/types/metadata'

// Build a minimal valid FieldMetadata object
const baseField = (overrides: Partial<FieldMetadata> = {}): FieldMetadata => ({
  name: 'Name',
  type: 'String',
  displayName: 'Name',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 100,
  annotations: {},
  ...overrides
})

const globalPlugins = {
  config: {
    globalProperties: {
      $t: (k: string) => k
    }
  }
}

describe('EntityForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    // Default: validation succeeds, returning form data as-is
    mockValidateFormData.mockImplementation((data: Record<string, unknown>) => ({
      success: true,
      data
    }))
  })

  // 1. renders submit button
  it('renders submit button', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        mode: 'create'
      },
      global: globalPlugins
    })

    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(true)
  })

  // 2. renders cancel button
  it('renders cancel button', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        mode: 'create'
      },
      global: globalPlugins
    })

    const cancelBtn = wrapper.find('button[type="button"]')
    expect(cancelBtn.exists()).toBe(true)
    expect(cancelBtn.text()).toContain('entity.form.cancel')
  })

  // 3. renders input fields for each non-key, non-system field
  it('renders input fields for each non-key, non-system field', () => {
    const fields: FieldMetadata[] = [
      baseField({ name: 'Name', type: 'String' }),
      baseField({ name: 'Email', type: 'String' }),
      baseField({ name: 'SysField', type: 'String', isReadOnly: true }),
      baseField({ name: 'ComputedField', type: 'String', isComputed: true })
    ]

    const wrapper = mount(EntityForm, {
      props: { fields, mode: 'create' },
      global: globalPlugins
    })

    // EntityField mock renders a div[data-testid="entity-field"] for each editable field.
    // In create mode, isReadOnly and isComputed fields are excluded from editableFields.
    const entityFieldDivs = wrapper.findAll('[data-testid="entity-field"]')
    // Only Name and Email should render — SysField (isReadOnly) and ComputedField (isComputed) excluded
    expect(entityFieldDivs.length).toBe(2)
  })

  // 4. initialData populates form fields
  it('initialData populates form fields', async () => {
    const fields: FieldMetadata[] = [
      baseField({ name: 'Name', type: 'String' })
    ]

    const wrapper = mount(EntityForm, {
      props: {
        fields,
        mode: 'edit',
        initialData: { Name: 'Alice' }
      },
      global: globalPlugins
    })

    await nextTick()

    // Access the internal formData via vm (exposed via script setup reactive state)
    const vm = wrapper.vm as unknown as { formData: Record<string, unknown> }
    expect(vm.formData['Name']).toBe('Alice')
  })

  // 5. clicking cancel emits cancel
  it('clicking cancel emits cancel', async () => {
    const wrapper = mount(EntityForm, {
      props: { fields: [baseField()], mode: 'create' },
      global: globalPlugins
    })

    const cancelBtn = wrapper.find('button[type="button"]')
    await cancelBtn.trigger('click')

    expect(wrapper.emitted('cancel')).toBeTruthy()
    expect(wrapper.emitted('cancel')!.length).toBe(1)
  })

  // 6. clicking submit emits submit with form data
  it('clicking submit emits submit with form data', async () => {
    const fields: FieldMetadata[] = [
      baseField({ name: 'Name', type: 'String' })
    ]

    const submittedData = { Name: 'Alice' }
    mockValidateFormData.mockReturnValue({ success: true, data: submittedData })

    const wrapper = mount(EntityForm, {
      props: { fields, mode: 'create', initialData: { Name: 'Alice' } },
      global: globalPlugins
    })

    await nextTick()

    const form = wrapper.find('form')
    await form.trigger('submit')

    expect(wrapper.emitted('submit')).toBeTruthy()
    const emittedArgs = wrapper.emitted('submit')![0] as [Record<string, unknown>, Record<string, unknown[]>]
    expect(emittedArgs[0]).toEqual(submittedData)
  })

  // 7. shows error alert when error prop is set
  it('shows error alert when error prop is set', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        error: 'Something went wrong'
      },
      global: globalPlugins
    })

    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('Something went wrong')
  })

  // 8. hides error alert when error prop is null
  it('hides error alert when error prop is null', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        error: null
      },
      global: globalPlugins
    })

    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(false)
  })

  // 9. shows spinner when isLoading is true
  it('shows spinner when isLoading is true', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        isLoading: true
      },
      global: globalPlugins
    })

    // Spinner renders an <svg> with the animate-spin class
    const spinner = wrapper.find('svg.animate-spin')
    expect(spinner.exists()).toBe(true)
  })

  // 10. hides spinner when isLoading is false
  it('hides spinner when isLoading is false', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        isLoading: false
      },
      global: globalPlugins
    })

    const spinner = wrapper.find('svg.animate-spin')
    expect(spinner.exists()).toBe(false)
  })

  // 11. submit button is disabled when isLoading is true
  it('submit button is disabled when isLoading is true', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        isLoading: true
      },
      global: globalPlugins
    })

    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.attributes('disabled')).toBeDefined()
  })

  // 12. required field validation prevents submit if empty
  it('required field validation prevents submit if empty', async () => {
    const fields: FieldMetadata[] = [
      baseField({ name: 'Name', type: 'String', isRequired: true })
    ]

    // Simulate validation failure for missing required field
    mockValidateFormData.mockReturnValue({
      success: false,
      errors: { Name: 'Name is required' }
    })

    const wrapper = mount(EntityForm, {
      props: { fields, mode: 'create' },
      global: globalPlugins
    })

    const form = wrapper.find('form')
    await form.trigger('submit')

    // submit event should NOT be emitted when validation fails
    expect(wrapper.emitted('submit')).toBeFalsy()

    // Validation error message should appear in the DOM
    await nextTick()
    const errorMsg = wrapper.find('.text-destructive')
    expect(errorMsg.exists()).toBe(true)
    expect(errorMsg.text()).toContain('Name is required')
  })

  // 13. view mode: submit button not shown
  it('view mode: submit button not shown', () => {
    const wrapper = mount(EntityForm, {
      props: {
        fields: [baseField()],
        mode: 'view'
      },
      global: globalPlugins
    })

    // In view mode the form actions section is hidden (v-if="mode !== 'view'")
    const submitBtn = wrapper.find('button[type="submit"]')
    expect(submitBtn.exists()).toBe(false)

    const cancelBtn = wrapper.find('button[type="button"]')
    expect(cancelBtn.exists()).toBe(false)
  })

  // 14. edit mode: form initializes from initialData
  it('edit mode: form initializes from initialData', async () => {
    const fields: FieldMetadata[] = [
      baseField({ name: 'Title', type: 'String' }),
      baseField({ name: 'Count', type: 'Integer' })
    ]

    const initialData = { Title: 'Hello', Count: 42 }

    const wrapper = mount(EntityForm, {
      props: {
        fields,
        mode: 'edit',
        initialData
      },
      global: globalPlugins
    })

    await nextTick()

    const vm = wrapper.vm as unknown as { formData: Record<string, unknown> }
    expect(vm.formData['Title']).toBe('Hello')
    expect(vm.formData['Count']).toBe(42)
  })
})
