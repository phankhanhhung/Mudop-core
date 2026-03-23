import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

// vi.mock declarations must come before importing the module under test

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (k: string, p?: Record<string, unknown>) => k }),
}))

vi.mock('lucide-vue-next', () => ({
  Send: { template: '<span data-icon="Send" />' },
  Pin: { template: '<span data-icon="Pin" />' },
  Play: { template: '<span data-icon="Play" />' },
}))

import ChatQueryInput from '../ChatQueryInput.vue'
import type { NlQueryMessage, NlQueryResult } from '@/services/nlQueryService'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const queryResult: NlQueryResult = {
  description: 'Active customers ordered by name',
  filter: "Status eq 'Active'",
  orderby: 'Name asc',
}

const userMessage: NlQueryMessage = {
  id: 'msg-1',
  role: 'user',
  content: 'show active customers',
  timestamp: 1000,
}

const assistantMessageWithQuery: NlQueryMessage = {
  id: 'msg-2',
  role: 'assistant',
  content: 'Here is the query',
  query: queryResult,
  timestamp: 2000,
}

const assistantMessageNoQuery: NlQueryMessage = {
  id: 'msg-3',
  role: 'assistant',
  content: 'I could not generate a query for that.',
  timestamp: 3000,
}

function mountComponent(propsOverrides: Record<string, unknown> = {}) {
  return mount(ChatQueryInput, {
    props: {
      messages: [],
      entityType: 'Customer',
      loading: false,
      ...propsOverrides,
    },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ChatQueryInput', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ── empty state ───────────────────────────────────────────────────────────

  describe('empty state', () => {
    it('renders empty state message when messages is empty', () => {
      const wrapper = mountComponent({ messages: [] })
      expect(wrapper.text()).toContain('nlq.emptyHint')
    })

    it('shows entity type in example hint', () => {
      const wrapper = mountComponent({ messages: [], entityType: 'Customer' })
      expect(wrapper.text()).toContain('nlq.exampleHint')
    })
  })

  // ── messages display ──────────────────────────────────────────────────────

  describe('messages display', () => {
    it('renders user messages right-aligned', () => {
      const wrapper = mountComponent({ messages: [userMessage] })
      const userBubble = wrapper.find('.flex.justify-end')
      expect(userBubble.exists()).toBe(true)
      expect(userBubble.text()).toContain('show active customers')
    })

    it('renders assistant messages left-aligned', () => {
      const wrapper = mountComponent({ messages: [assistantMessageWithQuery] })
      const assistantBubble = wrapper.find('.flex.justify-start')
      expect(assistantBubble.exists()).toBe(true)
    })

    it('renders query preview card when message has a query with filter', () => {
      const wrapper = mountComponent({ messages: [assistantMessageWithQuery] })
      // The query preview card is rendered when filter, expand, or orderby is present
      expect(wrapper.text()).toContain('nlq.queryPreview')
    })

    it('does not render query preview card when query has no filter/expand/orderby', () => {
      const messageWithEmptyQuery: NlQueryMessage = {
        id: 'msg-4',
        role: 'assistant',
        content: 'Here is the description only',
        query: { description: 'Just a description' },
        timestamp: 4000,
      }
      const wrapper = mountComponent({ messages: [messageWithEmptyQuery] })
      expect(wrapper.text()).not.toContain('nlq.queryPreview')
    })

    it('shows Run Query button on query card', () => {
      const wrapper = mountComponent({ messages: [assistantMessageWithQuery] })
      expect(wrapper.text()).toContain('nlq.runQuery')
    })

    it('shows Pin button on query card', () => {
      const wrapper = mountComponent({ messages: [assistantMessageWithQuery] })
      expect(wrapper.text()).toContain('nlq.pinQuery')
    })

    it('shows loading indicator when loading=true', () => {
      const wrapper = mountComponent({ messages: [], loading: true })
      // Loading indicator: three bouncing dots inside a flex container
      const loadingDots = wrapper.find('.animate-bounce')
      expect(loadingDots.exists()).toBe(true)
    })

    it('does not show loading indicator when loading=false', () => {
      const wrapper = mountComponent({ messages: [], loading: false })
      const loadingDots = wrapper.find('.animate-bounce')
      expect(loadingDots.exists()).toBe(false)
    })
  })

  // ── input behavior ────────────────────────────────────────────────────────

  describe('input behavior', () => {
    it('emits send event when Send button is clicked', async () => {
      const wrapper = mountComponent()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('show all orders')
      const sendButton = wrapper.find('button')
      await sendButton.trigger('click')
      expect(wrapper.emitted('send')).toBeTruthy()
      expect(wrapper.emitted('send')![0]).toEqual(['show all orders'])
    })

    it('emits send event when Enter is pressed (no shift)', async () => {
      const wrapper = mountComponent()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('find customers')
      await textarea.trigger('keydown', { key: 'Enter', shiftKey: false })
      expect(wrapper.emitted('send')).toBeTruthy()
      expect(wrapper.emitted('send')![0]).toEqual(['find customers'])
    })

    it('does NOT emit send event when Shift+Enter is pressed', async () => {
      const wrapper = mountComponent()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('find customers')
      await textarea.trigger('keydown', { key: 'Enter', shiftKey: true })
      expect(wrapper.emitted('send')).toBeFalsy()
    })

    it('clears input after sending', async () => {
      const wrapper = mountComponent()
      const textarea = wrapper.find('textarea')
      await textarea.setValue('show all')
      const sendButton = wrapper.find('button')
      await sendButton.trigger('click')
      await nextTick()
      expect((textarea.element as HTMLTextAreaElement).value).toBe('')
    })

    it('disables Send button when input is empty', () => {
      const wrapper = mountComponent()
      const sendButton = wrapper.find('button')
      expect(sendButton.element.disabled).toBe(true)
    })

    it('does NOT emit send when loading=true', async () => {
      const wrapper = mountComponent({ loading: true })
      const textarea = wrapper.find('textarea')
      await textarea.setValue('some query')
      await nextTick()
      // Trigger via keydown since button is disabled
      await textarea.trigger('keydown', { key: 'Enter', shiftKey: false })
      expect(wrapper.emitted('send')).toBeFalsy()
    })
  })

  // ── emit events ───────────────────────────────────────────────────────────

  describe('emit events', () => {
    it('emits runQuery with query result when Run Query clicked', async () => {
      const wrapper = mountComponent({ messages: [assistantMessageWithQuery] })
      // Find the Run Query button by its translation key text
      const buttons = wrapper.findAll('button')
      const runBtn = buttons.find((b) => b.text().includes('nlq.runQuery'))
      expect(runBtn).toBeDefined()
      await runBtn!.trigger('click')
      expect(wrapper.emitted('runQuery')).toBeTruthy()
      expect(wrapper.emitted('runQuery')![0][0]).toEqual(queryResult)
    })

    it('emits pinQuery with query result when Pin clicked', async () => {
      const wrapper = mountComponent({ messages: [assistantMessageWithQuery] })
      const buttons = wrapper.findAll('button')
      const pinBtn = buttons.find((b) => b.text().includes('nlq.pinQuery'))
      expect(pinBtn).toBeDefined()
      await pinBtn!.trigger('click')
      expect(wrapper.emitted('pinQuery')).toBeTruthy()
      expect(wrapper.emitted('pinQuery')![0][0]).toEqual(queryResult)
    })
  })
})
