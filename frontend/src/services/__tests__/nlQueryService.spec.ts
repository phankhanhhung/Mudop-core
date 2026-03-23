import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nlQueryService } from '../nlQueryService'
import type { EntityMetadata, FieldMetadata, AssociationMetadata } from '@/types/metadata'

vi.mock('@/services/api', () => ({
  default: {
    post: vi.fn(),
  },
}))

import api from '@/services/api'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeField(overrides: Partial<FieldMetadata> = {}): FieldMetadata {
  return {
    name: 'Name',
    type: 'String',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
    ...overrides,
  }
}

function makeAssociation(overrides: Partial<AssociationMetadata> = {}): AssociationMetadata {
  return {
    name: 'account',
    targetEntity: 'Account',
    cardinality: 'ZeroOrOne',
    isComposition: false,
    ...overrides,
  }
}

function makeMetadata(overrides: Partial<EntityMetadata> = {}): EntityMetadata {
  return {
    name: 'Customer',
    namespace: 'business.crm',
    fields: [],
    keys: ['ID'],
    associations: [],
    annotations: {},
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('nlQueryService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  // ── buildSchemaContext() ──────────────────────────────────────────────────

  describe('buildSchemaContext', () => {
    it('includes entity name in output', () => {
      const metadata = makeMetadata({ name: 'Customer' })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).toContain('Entity: Customer')
    })

    it('lists all fields with types', () => {
      const metadata = makeMetadata({
        fields: [
          makeField({ name: 'Name', type: 'String' }),
          makeField({ name: 'Age', type: 'Integer' }),
        ],
      })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).toContain('Name: String')
      expect(result).toContain('Age: Integer')
    })

    it('marks required fields', () => {
      const metadata = makeMetadata({
        fields: [
          makeField({ name: 'Email', type: 'String', isRequired: true }),
          makeField({ name: 'Phone', type: 'String', isRequired: false }),
        ],
      })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).toContain('Email: String (required)')
      expect(result).not.toContain('Phone: String (required)')
    })

    it('includes enum values when present', () => {
      const metadata = makeMetadata({
        fields: [
          makeField({
            name: 'Status',
            type: 'Enum',
            enumValues: [
              { name: 'Active', value: 1 },
              { name: 'Inactive', value: 2 },
            ],
          }),
        ],
      })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).toContain('[enum: 1, 2]')
    })

    it('includes maxLength when present', () => {
      const metadata = makeMetadata({
        fields: [
          makeField({ name: 'Name', type: 'String', maxLength: 100 }),
        ],
      })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).toContain('(maxLength: 100)')
    })

    it('includes associations when present', () => {
      const metadata = makeMetadata({
        associations: [
          makeAssociation({ name: 'account', targetEntity: 'Account', cardinality: 'ZeroOrOne' }),
        ],
      })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).toContain('Associations:')
      expect(result).toContain('account → Account (ZeroOrOne)')
    })

    it('omits association section when none', () => {
      const metadata = makeMetadata({ associations: [] })
      const result = nlQueryService.buildSchemaContext(metadata)
      expect(result).not.toContain('Associations:')
    })
  })

  // ── translate() ───────────────────────────────────────────────────────────

  describe('translate', () => {
    it('calls api.post with /ai/nl-query endpoint', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'Filtered customers', filter: "Name eq 'Acme'" } })
      const metadata = makeMetadata()
      await nlQueryService.translate('show active customers', metadata, 'crm')
      expect(api.post).toHaveBeenCalledWith('/ai/nl-query', expect.any(Object))
    })

    it('passes entityType = metadata.name', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'ok' } })
      const metadata = makeMetadata({ name: 'Customer' })
      await nlQueryService.translate('list all', metadata, 'crm')
      const body = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(body.entityType).toBe('Customer')
    })

    it('passes moduleName from argument', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'ok' } })
      const metadata = makeMetadata()
      await nlQueryService.translate('list all', metadata, 'sales')
      const body = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(body.moduleName).toBe('sales')
    })

    it('passes prompt and schemaContext in request body', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'ok' } })
      const metadata = makeMetadata({ name: 'Customer', fields: [] })
      await nlQueryService.translate('find VIP customers', metadata, 'crm')
      const body = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(body.prompt).toBe('find VIP customers')
      expect(typeof body.schemaContext).toBe('string')
      expect(body.schemaContext as string).toContain('Entity: Customer')
    })

    it('returns the response data as NlQueryResult', async () => {
      const responseData = { description: 'Customers with active status', filter: "Status eq 'Active'" }
      vi.mocked(api.post).mockResolvedValue({ data: responseData })
      const metadata = makeMetadata()
      const result = await nlQueryService.translate('active customers', metadata, 'crm')
      expect(result).toEqual(responseData)
    })

    it('passes history to request when provided', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'ok' } })
      const metadata = makeMetadata()
      const history = [
        { id: 'msg-1', role: 'user' as const, content: 'list all customers', timestamp: 1000 },
        {
          id: 'msg-2',
          role: 'assistant' as const,
          content: 'Here is your query',
          query: { description: 'All customers', filter: undefined },
          timestamp: 2000,
        },
      ]
      await nlQueryService.translate('refine it', metadata, 'crm', history)
      const body = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(body.history).toBeDefined()
      expect(Array.isArray(body.history)).toBe(true)
      expect((body.history as unknown[]).length).toBe(2)
    })

    it('omits history from request when not provided', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'ok' } })
      const metadata = makeMetadata()
      await nlQueryService.translate('list all', metadata, 'crm')
      const body = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(body.history).toBeUndefined()
    })

    it('omits history from request when empty array is provided', async () => {
      vi.mocked(api.post).mockResolvedValue({ data: { description: 'ok' } })
      const metadata = makeMetadata()
      await nlQueryService.translate('list all', metadata, 'crm', [])
      const body = vi.mocked(api.post).mock.calls[0][1] as Record<string, unknown>
      expect(body.history).toBeUndefined()
    })
  })
})
