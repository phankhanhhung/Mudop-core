import { describe, it, expect, vi, beforeEach } from 'vitest'
import { dashboardLayoutService } from '../dashboardLayoutService'

vi.mock('../userPreferenceService', () => ({
  userPreferenceService: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
}))

import { userPreferenceService } from '../userPreferenceService'

const mockLayout = { version: 1 as const, widgets: [], columns: 3 as const }
const mockPref = {
  id: 'pref-1',
  category: 'dashboard-layout',
  entityKey: 'global:default',
  name: 'Dashboard Layout',
  isDefault: true,
  settings: mockLayout,
  createdAt: '2024-01-01',
  updatedAt: '2024-01-01',
}

describe('dashboardLayoutService', () => {
  beforeEach(() => {
    vi.resetAllMocks()
  })

  describe('getLayout', () => {
    it('returns null when no preferences exist', async () => {
      vi.mocked(userPreferenceService.list).mockResolvedValue([])
      const result = await dashboardLayoutService.getLayout()
      expect(result).toBeNull()
    })

    it('returns the saved layout when a preference exists', async () => {
      vi.mocked(userPreferenceService.list).mockResolvedValue([mockPref])
      const result = await dashboardLayoutService.getLayout()
      expect(result).not.toBeNull()
      expect(result!.id).toBe('pref-1')
      expect(result!.settings.columns).toBe(3)
    })

    it('prefers isDefault preference when multiple exist', async () => {
      const nonDefault = { ...mockPref, id: 'pref-2', isDefault: false }
      vi.mocked(userPreferenceService.list).mockResolvedValue([nonDefault, mockPref])
      const result = await dashboardLayoutService.getLayout()
      expect(result!.id).toBe('pref-1')
    })

    it('uses correct category and entityKey', async () => {
      vi.mocked(userPreferenceService.list).mockResolvedValue([])
      await dashboardLayoutService.getLayout()
      expect(userPreferenceService.list).toHaveBeenCalledWith('dashboard-layout', 'global:default')
    })
  })

  describe('saveLayout', () => {
    it('creates a new preference when no existingId', async () => {
      vi.mocked(userPreferenceService.create).mockResolvedValue({ ...mockPref, id: 'new-id' })
      const id = await dashboardLayoutService.saveLayout(mockLayout)
      expect(id).toBe('new-id')
      expect(userPreferenceService.create).toHaveBeenCalledWith(
        expect.objectContaining({ category: 'dashboard-layout', isDefault: true }),
      )
    })

    it('updates the existing preference when existingId provided', async () => {
      vi.mocked(userPreferenceService.update).mockResolvedValue({ ...mockPref, id: 'pref-1' })
      const id = await dashboardLayoutService.saveLayout(mockLayout, 'pref-1')
      expect(id).toBe('pref-1')
      expect(userPreferenceService.update).toHaveBeenCalledWith('pref-1', expect.objectContaining({ settings: mockLayout }))
    })
  })

  describe('resetLayout', () => {
    it('calls remove with the given id', async () => {
      vi.mocked(userPreferenceService.remove).mockResolvedValue(undefined)
      await dashboardLayoutService.resetLayout('pref-1')
      expect(userPreferenceService.remove).toHaveBeenCalledWith('pref-1')
    })
  })
})
