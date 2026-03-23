import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

const { mockAssist } = vi.hoisted(() => ({ mockAssist: vi.fn() }))

vi.mock('@/services/aiService', () => ({
  aiService: {
    assist: mockAssist,
  },
}))

import AiAssistPanel from '../AiAssistPanel.vue'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string) => k }),
}))

const globalMocks = {
  global: {
    mocks: { $t: (k: string) => k },
    stubs: {
      Spinner: { template: '<span data-stub="Spinner" />' },
      BrainCircuit: { template: '<span data-stub="BrainCircuit" />' },
      Sparkles: { template: '<span data-stub="Sparkles" />' },
      Eye: { template: '<span data-stub="Eye" />' },
      AlertCircle: { template: '<span data-stub="AlertCircle" />' },
      Copy: { template: '<span data-stub="Copy" />' },
      ArrowDownToLine: { template: '<span data-stub="ArrowDownToLine" />' },
      RefreshCw: { template: '<span data-stub="RefreshCw" />' },
      X: { template: '<span data-stub="X" />' },
    },
  },
}

// Helper: mount with common defaults
function mountPanel(props: { context: string; markers?: Array<{ line: number; column: number; message: string; severity: 'error' | 'warning' | 'info' }> } = { context: 'module Test {}', markers: [] }) {
  return mount(AiAssistPanel, {
    props,
    ...globalMocks,
  })
}

describe('AiAssistPanel', () => {
  beforeEach(() => {
    mockAssist.mockReset()
  })

  // ── Rendering ──────────────────────────────────────────────────────────────

  describe('Rendering', () => {
    it('panel renders with data-testid="ai-assist-panel"', () => {
      const wrapper = mountPanel()
      expect(wrapper.find('[data-testid="ai-assist-panel"]').exists()).toBe(true)
    })

    it('shows 3 tab buttons: generate, review, explain', () => {
      const wrapper = mountPanel()
      // The tab bar is .flex.border-b — it holds exactly the 3 tab buttons.
      // Each tab button text is the i18n key (t() returns the key in tests).
      // Use the template structure: tab buttons are direct children of the tab bar div.
      const allButtons = wrapper.findAll('button')
      const texts = allButtons.map((b) => b.text().trim())
      // Verify all 3 tab labels are present somewhere
      expect(texts.some((t) => t === 'ai.generate')).toBe(true)
      expect(texts.some((t) => t === 'ai.review')).toBe(true)
      expect(texts.some((t) => t === 'ai.explain')).toBe(true)
      // The 3 tab buttons have exact single-key text
      const tabButtons = allButtons.filter((b) =>
        ['ai.generate', 'ai.review', 'ai.explain'].includes(b.text().trim()),
      )
      expect(tabButtons).toHaveLength(3)
    })

    it('emits close when X button is clicked', async () => {
      const wrapper = mountPanel()
      // The X button is in the panel header (ghost icon button)
      const closeButton = wrapper.find('button[title="ai.close"]')
      await closeButton.trigger('click')
      expect(wrapper.emitted('close')).toBeTruthy()
      expect(wrapper.emitted('close')).toHaveLength(1)
    })
  })

  // ── Tab switching ──────────────────────────────────────────────────────────

  describe('Tab switching', () => {
    it('clicking "review" tab activates review tab content', async () => {
      const wrapper = mountPanel({ context: 'entity Foo {}' })
      const tabButtons = wrapper.findAll('.flex.border-b button')
      const reviewTab = tabButtons.find((b) => b.text().includes('ai.review'))!
      await reviewTab.trigger('click')
      // Review tab content has "ai.reviewDescription" text
      expect(wrapper.text()).toContain('ai.reviewDescription')
    })

    it('clicking "explain" tab activates explain tab content', async () => {
      const wrapper = mountPanel({ context: 'entity Foo {}', markers: [] })
      const tabButtons = wrapper.findAll('.flex.border-b button')
      const explainTab = tabButtons.find((b) => b.text().includes('ai.explain'))!
      await explainTab.trigger('click')
      // Explain tab with no markers shows the "no errors" message
      expect(wrapper.text()).toContain('ai.explainNoErrors')
    })
  })

  // ── Generate tab ───────────────────────────────────────────────────────────

  describe('Generate tab', () => {
    it('generate tab is active by default', () => {
      const wrapper = mountPanel()
      // The generate tab should be visible: its textarea is present
      expect(wrapper.find('textarea').exists()).toBe(true)
    })

    it('generate button is disabled when prompt textarea is empty', () => {
      const wrapper = mountPanel()
      // Find the main generate button (w-full class, size sm)
      const buttons = wrapper.findAll('button')
      const generateBtn = buttons.find((b) => b.text().includes('ai.generateButton') || b.attributes('disabled') !== undefined)!
      // With empty textarea, the button should have disabled attribute
      const wFullBtn = wrapper.find('.flex-1.overflow-auto button[disabled]')
      expect(wFullBtn.exists()).toBe(true)
    })

    it('generate button is enabled when prompt has text', async () => {
      const wrapper = mountPanel()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('Create a Customer entity with name and email')
      // The generate button should no longer be disabled
      // Find buttons in the tab content area
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const generateBtn = contentButtons.find((b) => b.text().includes('ai.generateButton'))!
      expect(generateBtn.attributes('disabled')).toBeUndefined()
    })

    it('clicking Generate calls aiService.assist with operation, context, and prompt', async () => {
      mockAssist.mockResolvedValue({ result: 'entity Foo {}' })
      const wrapper = mountPanel({ context: 'module Test {}' })
      const textarea = wrapper.find('textarea')
      await textarea.setValue('Create a Customer entity')
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const generateBtn = contentButtons.find((b) => b.text().includes('ai.generateButton'))!
      await generateBtn.trigger('click')
      await flushPromises()
      expect(mockAssist).toHaveBeenCalledWith(expect.objectContaining({
        operation: 'generate',
        context: 'module Test {}',
        prompt: 'Create a Customer entity',
      }))
    })

    it('shows result code block after successful generation', async () => {
      mockAssist.mockResolvedValue({ result: 'entity Foo { key ID: UUID; }' })
      const wrapper = mountPanel({ context: 'module Test {}' })
      const textarea = wrapper.find('textarea')
      await textarea.setValue('Create Foo entity')
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const generateBtn = contentButtons.find((b) => b.text().includes('ai.generateButton'))!
      await generateBtn.trigger('click')
      await flushPromises()
      // Result is rendered in a <pre> element
      const pre = wrapper.find('pre')
      expect(pre.exists()).toBe(true)
      expect(pre.text()).toContain('entity Foo { key ID: UUID; }')
    })

    it('shows error message when aiService.assist rejects', async () => {
      mockAssist.mockRejectedValue(new Error('AI service unavailable'))
      const wrapper = mountPanel({ context: 'module Test {}' })
      const textarea = wrapper.find('textarea')
      await textarea.setValue('Create entity')
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const generateBtn = contentButtons.find((b) => b.text().includes('ai.generateButton'))!
      await generateBtn.trigger('click')
      await flushPromises()
      // Error div should appear
      const errorDiv = wrapper.find('.text-destructive')
      expect(errorDiv.exists()).toBe(true)
      expect(errorDiv.text()).toContain('AI service unavailable')
    })

    it('clicking "Insert into Editor" emits insert event with result code', async () => {
      const generatedCode = 'entity Customer { key ID: UUID; name: String(100); }'
      mockAssist.mockResolvedValue({ result: generatedCode })
      const wrapper = mountPanel({ context: 'module Test {}' })
      const textarea = wrapper.find('textarea')
      await textarea.setValue('Create Customer entity')
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const generateBtn = contentButtons.find((b) => b.text().includes('ai.generateButton'))!
      await generateBtn.trigger('click')
      await flushPromises()
      // Find the "Insert into Editor" button (variant="outline")
      const insertBtn = wrapper.findAll('button').find((b) => b.text().includes('ai.insertCode'))!
      await insertBtn.trigger('click')
      expect(wrapper.emitted('insert')).toBeTruthy()
      expect(wrapper.emitted('insert')![0]).toEqual([generatedCode])
    })
  })

  // ── Review tab ────────────────────────────────────────────────────────────

  describe('Review tab', () => {
    async function switchToReview(wrapper: ReturnType<typeof mount>) {
      const tabButtons = wrapper.findAll('.flex.border-b button')
      const reviewTab = tabButtons.find((b) => b.text().includes('ai.review'))!
      await reviewTab.trigger('click')
    }

    it('review button is disabled when context is empty', async () => {
      const wrapper = mountPanel({ context: '   ' })
      await switchToReview(wrapper)
      const disabledBtn = wrapper.find('.flex-1.overflow-auto button[disabled]')
      expect(disabledBtn.exists()).toBe(true)
    })

    it('review button is enabled when context is non-empty', async () => {
      const wrapper = mountPanel({ context: 'entity Foo { key ID: UUID; }' })
      await switchToReview(wrapper)
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const reviewBtn = contentButtons.find((b) => b.text().includes('ai.reviewButton'))!
      expect(reviewBtn.attributes('disabled')).toBeUndefined()
    })

    it('clicking Review calls aiService.assist with operation review and context', async () => {
      mockAssist.mockResolvedValue({ result: '', suggestions: ['Add a key field'] })
      const wrapper = mountPanel({ context: 'entity Foo {}' })
      await switchToReview(wrapper)
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const reviewBtn = contentButtons.find((b) => b.text().includes('ai.reviewButton'))!
      await reviewBtn.trigger('click')
      await flushPromises()
      expect(mockAssist).toHaveBeenCalledWith(expect.objectContaining({
        operation: 'review',
        context: 'entity Foo {}',
      }))
    })

    it('displays suggestions list from result.suggestions', async () => {
      mockAssist.mockResolvedValue({
        result: '',
        suggestions: ['Add a key field', 'Use UUID for primary key', 'Add Auditable aspect'],
      })
      const wrapper = mountPanel({ context: 'entity Foo {}' })
      await switchToReview(wrapper)
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const reviewBtn = contentButtons.find((b) => b.text().includes('ai.reviewButton'))!
      await reviewBtn.trigger('click')
      await flushPromises()
      expect(wrapper.text()).toContain('Add a key field')
      expect(wrapper.text()).toContain('Use UUID for primary key')
      expect(wrapper.text()).toContain('Add Auditable aspect')
    })
  })

  // ── Explain tab ───────────────────────────────────────────────────────────

  describe('Explain tab', () => {
    async function switchToExplain(wrapper: ReturnType<typeof mount>) {
      const tabButtons = wrapper.findAll('.flex.border-b button')
      const explainTab = tabButtons.find((b) => b.text().includes('ai.explain'))!
      await explainTab.trigger('click')
    }

    const markers = [
      { line: 5, column: 3, message: 'Expected entity keyword', severity: 'error' as const },
      { line: 8, column: 1, message: 'Missing semicolon', severity: 'warning' as const },
    ]

    it('shows "no errors" message when markers prop is empty', async () => {
      const wrapper = mountPanel({ context: 'entity Foo {}', markers: [] })
      await switchToExplain(wrapper)
      expect(wrapper.text()).toContain('ai.explainNoErrors')
    })

    it('shows <select> with error markers when markers are provided', async () => {
      const wrapper = mountPanel({ context: 'entity Foo {}', markers })
      await switchToExplain(wrapper)
      const select = wrapper.find('select')
      expect(select.exists()).toBe(true)
      const options = wrapper.findAll('option')
      expect(options).toHaveLength(2)
    })

    it('clicking Explain calls aiService.assist with operation explain-error and context and error', async () => {
      mockAssist.mockResolvedValue({ result: 'The entity keyword is required here.' })
      const wrapper = mountPanel({ context: 'module Test {}', markers })
      await switchToExplain(wrapper)
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const explainBtn = contentButtons.find((b) => b.text().includes('ai.explainButton'))!
      await explainBtn.trigger('click')
      await flushPromises()
      expect(mockAssist).toHaveBeenCalledWith(expect.objectContaining({
        operation: 'explain-error',
        context: 'module Test {}',
        error: expect.stringContaining('Expected entity keyword'),
      }))
    })

    it('shows explanation text after successful explain', async () => {
      mockAssist.mockResolvedValue({ result: 'You are missing the entity keyword before the block.' })
      const wrapper = mountPanel({ context: 'module Test {}', markers })
      await switchToExplain(wrapper)
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const explainBtn = contentButtons.find((b) => b.text().includes('ai.explainButton'))!
      await explainBtn.trigger('click')
      await flushPromises()
      expect(wrapper.text()).toContain('You are missing the entity keyword before the block.')
    })

    it('error message passed to assist includes line and column from selected marker', async () => {
      mockAssist.mockResolvedValue({ result: 'Explanation here.' })
      const wrapper = mountPanel({ context: 'module Test {}', markers })
      await switchToExplain(wrapper)
      const contentButtons = wrapper.findAll('.flex-1.overflow-auto button')
      const explainBtn = contentButtons.find((b) => b.text().includes('ai.explainButton'))!
      await explainBtn.trigger('click')
      await flushPromises()
      const callArg = mockAssist.mock.calls[0][0] as { error: string }
      // Error string is formatted as "Line {line}, Col {column}: {message}"
      expect(callArg.error).toContain('Line 5')
      expect(callArg.error).toContain('Col 3')
      expect(callArg.error).toContain('Expected entity keyword')
    })
  })
})
