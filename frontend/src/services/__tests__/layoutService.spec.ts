import { describe, it, expect, vi, beforeEach } from 'vitest'
import { layoutService } from '../layoutService'

vi.mock('../userPreferenceService', () => ({
  userPreferenceService: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
}))

import { userPreferenceService } from '../userPreferenceService'

const mockPref = {
  id: 'pref-1',
  category: 'form-layout',
  entityKey: 'myns:Customer',
  name: 'Default Layout',
  isDefault: true,
  settings: { version: 1, sections: [], columns: 2 },
  createdAt: '2024-01-01',
  updatedAt: '2024-01-01',
}

describe('layoutService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  describe('getLayout', () => {
    it('returns null when no preferences exist', async () => {
      vi.mocked(userPreferenceService.list).mockResolvedValue([])
      const result = await layoutService.getLayout('myns', 'Customer')
      expect(result).toBeNull()
    })

    it('returns the default preference when one exists', async () => {
      vi.mocked(userPreferenceService.list).mockResolvedValue([mockPref])
      const result = await layoutService.getLayout('myns', 'Customer')
      expect(result).not.toBeNull()
      expect(result!.id).toBe('pref-1')
      expect(result!.settings.columns).toBe(2)
    })

    it('prefers the isDefault preference when multiple exist', async () => {
      const nonDefault = { ...mockPref, id: 'pref-2', isDefault: false }
      vi.mocked(userPreferenceService.list).mockResolvedValue([nonDefault, mockPref])
      const result = await layoutService.getLayout('myns', 'Customer')
      expect(result!.id).toBe('pref-1')
    })

    it('uses correct category and entityKey', async () => {
      vi.mocked(userPreferenceService.list).mockResolvedValue([])
      await layoutService.getLayout('hr', 'Employee')
      expect(userPreferenceService.list).toHaveBeenCalledWith('form-layout', 'hr:Employee')
    })
  })

  describe('saveLayout', () => {
    const settings = { version: 1 as const, sections: [], columns: 2 as const }

    it('creates a new preference when no existingId', async () => {
      vi.mocked(userPreferenceService.create).mockResolvedValue({ ...mockPref, id: 'new-id' })
      const id = await layoutService.saveLayout('myns', 'Customer', settings)
      expect(id).toBe('new-id')
      expect(userPreferenceService.create).toHaveBeenCalledWith(
        expect.objectContaining({ category: 'form-layout', isDefault: true }),
      )
    })

    it('updates the existing preference when existingId provided', async () => {
      vi.mocked(userPreferenceService.update).mockResolvedValue({ ...mockPref, id: 'pref-1' })
      const id = await layoutService.saveLayout('myns', 'Customer', settings, 'pref-1')
      expect(id).toBe('pref-1')
      expect(userPreferenceService.update).toHaveBeenCalledWith('pref-1', expect.objectContaining({ settings }))
    })
  })

  describe('resetLayout', () => {
    it('calls remove with the given id', async () => {
      vi.mocked(userPreferenceService.remove).mockResolvedValue(undefined)
      await layoutService.resetLayout('pref-1')
      expect(userPreferenceService.remove).toHaveBeenCalledWith('pref-1')
    })
  })
})
