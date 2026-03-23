import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// ---------------------------------------------------------------------------
// Mocks — hoisted
// ---------------------------------------------------------------------------

const { mockListComments, mockCreateComment, mockToggleLike, mockDeleteComment } = vi.hoisted(() => ({
  mockListComments: vi.fn(),
  mockCreateComment: vi.fn(),
  mockToggleLike: vi.fn(),
  mockDeleteComment: vi.fn(),
}))

vi.mock('@/services/commentService', () => ({
  commentService: {
    listComments: mockListComments,
    createComment: mockCreateComment,
    toggleLike: mockToggleLike,
    deleteComment: mockDeleteComment,
  },
}))

const mockJoinRecordGroup = vi.fn()
let newCommentCallback: ((n: Record<string, unknown>) => void) | null = null

vi.mock('@/composables/useSignalR', () => ({
  useSignalR: () => ({
    joinRecordGroup: mockJoinRecordGroup,
    onNewComment: (cb: (n: Record<string, unknown>) => void) => {
      newCommentCallback = cb
    },
  }),
}))

// Stub child components to avoid deep rendering
vi.mock('@/components/smart/FeedList.vue', () => ({
  default: {
    name: 'FeedList',
    template: '<div data-testid="feed-list"><slot /></div>',
    props: ['items', 'currentUser', 'inputPlaceholder'],
    emits: ['post', 'like', 'delete'],
  },
}))

vi.mock('@/components/ui/card', () => ({
  Card: { template: '<div><slot /></div>' },
  CardHeader: { template: '<div><slot /></div>' },
  CardTitle: { template: '<h3><slot /></h3>' },
  CardContent: { template: '<div><slot /></div>' },
}))

vi.mock('@/components/ui/spinner', () => ({
  Spinner: { template: '<div data-testid="spinner" />' },
}))

// ---------------------------------------------------------------------------
// Import component AFTER mocks
// ---------------------------------------------------------------------------

import CommentSection from '../CommentSection.vue'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeComment(overrides = {}) {
  return {
    id: 'c-1',
    authorId: 'user-1',
    authorName: 'Alice',
    content: 'Hello world',
    mentions: [],
    likedBy: [],
    createdAt: '2026-01-01T10:00:00Z',
    ...overrides,
  }
}

const defaultProps = {
  module: 'crm',
  entityType: 'Customer',
  entityId: 'cust-1',
  currentUserId: 'user-1',
  currentUserName: 'Alice',
}

function mountSection(overrides = {}) {
  return mount(CommentSection, {
    props: { ...defaultProps, ...overrides },
    global: { mocks: { $t: (k: string) => k } },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CommentSection', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    newCommentCallback = null
    mockJoinRecordGroup.mockResolvedValue(undefined)
    mockListComments.mockResolvedValue([])
  })

  // ── mount / loading ───────────────────────────────────────────────────

  it('calls listComments on mount (loading flow)', async () => {
    mockListComments.mockResolvedValue([])
    mountSection()
    await flushPromises()
    expect(mockListComments).toHaveBeenCalledTimes(1)
  })

  it('renders FeedList after comments are loaded', async () => {
    mockListComments.mockResolvedValue([makeComment()])
    const wrapper = mountSection()
    await flushPromises()
    expect(wrapper.find('[data-testid="feed-list"]').exists()).toBe(true)
  })

  it('calls listComments with correct module/entityType/entityId on mount', async () => {
    mockListComments.mockResolvedValue([])
    mountSection()
    await flushPromises()
    expect(mockListComments).toHaveBeenCalledWith('crm', 'Customer', 'cust-1')
  })

  it('joins SignalR record group on mount', async () => {
    mockListComments.mockResolvedValue([])
    mountSection()
    await flushPromises()
    expect(mockJoinRecordGroup).toHaveBeenCalledWith('crm/Customer/cust-1')
  })

  // ── handlePost — mention extraction ───────────────────────────────────

  it('extracts @mention names from comment content and passes to createComment', async () => {
    mockListComments.mockResolvedValue([])
    const created = makeComment({ content: 'Hey @bob and @alice!' })
    mockCreateComment.mockResolvedValue(created)

    const wrapper = mountSection()
    await flushPromises()

    const feedList = wrapper.findComponent({ name: 'FeedList' })
    await feedList.vm.$emit('post', { content: 'Hey @bob and @alice!' })
    await flushPromises()

    expect(mockCreateComment).toHaveBeenCalledWith(
      'crm', 'Customer', 'cust-1',
      'Hey @bob and @alice!',
      expect.arrayContaining(['bob', 'alice'])
    )
  })

  it('passes empty mentions array when content has no @mentions', async () => {
    mockListComments.mockResolvedValue([])
    const created = makeComment({ content: 'No mentions here' })
    mockCreateComment.mockResolvedValue(created)

    const wrapper = mountSection()
    await flushPromises()

    const feedList = wrapper.findComponent({ name: 'FeedList' })
    await feedList.vm.$emit('post', { content: 'No mentions here' })
    await flushPromises()

    expect(mockCreateComment).toHaveBeenCalledWith(
      'crm', 'Customer', 'cust-1',
      'No mentions here',
      []
    )
  })

  // ── handleLike ────────────────────────────────────────────────────────

  it('calls toggleLike with correct params when FeedList emits like', async () => {
    const comment = makeComment()
    mockListComments.mockResolvedValue([comment])
    const updated = makeComment({ likedBy: ['user-1'] })
    mockToggleLike.mockResolvedValue(updated)

    const wrapper = mountSection()
    await flushPromises()

    const feedList = wrapper.findComponent({ name: 'FeedList' })
    await feedList.vm.$emit('like', { itemId: 'c-1', liked: true })
    await flushPromises()

    expect(mockToggleLike).toHaveBeenCalledWith('crm', 'Customer', 'cust-1', 'c-1')
  })

  // ── handleDelete ──────────────────────────────────────────────────────

  it('calls deleteComment with correct params when FeedList emits delete', async () => {
    const comment = makeComment()
    mockListComments.mockResolvedValue([comment])
    mockDeleteComment.mockResolvedValue(undefined)

    const wrapper = mountSection()
    await flushPromises()

    const feedList = wrapper.findComponent({ name: 'FeedList' })
    await feedList.vm.$emit('delete', { itemId: 'c-1' })
    await flushPromises()

    expect(mockDeleteComment).toHaveBeenCalledWith('crm', 'Customer', 'cust-1', 'c-1')
  })

  // ── SignalR real-time comment ─────────────────────────────────────────

  it('adds incoming comment from onNewComment when from different user and not duplicate', async () => {
    mockListComments.mockResolvedValue([])
    const wrapper = mountSection()
    await flushPromises()

    newCommentCallback!({
      recordKey: 'crm/Customer/cust-1',
      commentId: 'c-realtime',
      authorId: 'user-2',
      authorName: 'Bob',
      content: 'Real-time!',
      mentions: [],
      createdAt: '2026-01-01T10:05:00Z',
    })
    await flushPromises()

    const feedList = wrapper.findComponent({ name: 'FeedList' })
    const items = feedList.props('items') as { id: string }[]
    expect(items.some((item) => item.id === 'c-realtime')).toBe(true)
  })

  it('ignores real-time comment from the current user (avoids duplicates)', async () => {
    mockListComments.mockResolvedValue([])
    const wrapper = mountSection()
    await flushPromises()

    newCommentCallback!({
      recordKey: 'crm/Customer/cust-1',
      commentId: 'c-self',
      authorId: 'user-1', // same as currentUserId
      authorName: 'Alice',
      content: 'My own comment',
      mentions: [],
      createdAt: '2026-01-01T10:05:00Z',
    })
    await flushPromises()

    const feedList = wrapper.findComponent({ name: 'FeedList' })
    const items = feedList.props('items') as { id: string }[]
    expect(items.some((item) => item.id === 'c-self')).toBe(false)
  })
})
