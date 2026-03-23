import { describe, it, expect, beforeEach, vi } from 'vitest'
import { PluginRegistry } from '../PluginRegistry'
import type { ActionHookContext } from '../types'

// ── Shared mock component ─────────────────────────────────────────────────────

const MockComponent = { template: '<div />' }

// ── Helpers ───────────────────────────────────────────────────────────────────

function makePlugin(id: string) {
  return { id, name: `Plugin ${id}`, version: '1.0.0' }
}

function makeCtx(overrides: Partial<ActionHookContext> = {}): ActionHookContext {
  return {
    entityType: 'Customer',
    module: 'crm',
    operation: 'create',
    ...overrides,
  }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe('PluginRegistry', () => {
  let registry: PluginRegistry

  beforeEach(() => {
    registry = new PluginRegistry()
  })

  // ── Registration ────────────────────────────────────────────────────────────

  describe('Registration', () => {
    it('register stores plugin definition and makes it retrievable', () => {
      const plugin = makePlugin('my-plugin')
      registry.register({ plugin })

      expect(registry.getPlugin('my-plugin')).toEqual(plugin)
      expect(registry.getPlugins()).toContainEqual(plugin)
    })

    it('duplicate register with same id is a no-op and emits a warning', () => {
      const plugin = makePlugin('dup-plugin')
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

      registry.register({ plugin, columnRenderers: [{ fieldType: 'String', component: MockComponent, priority: 0 }] })
      registry.register({ plugin, columnRenderers: [{ fieldType: 'String', component: MockComponent, priority: 0 }] })

      expect(warnSpy).toHaveBeenCalledOnce()
      expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('dup-plugin'))
      // Only one column renderer should exist (second registration was skipped)
      expect(registry.getColumnRenderer('String')).not.toBeNull()
      const renderer = registry.getColumnRenderer('String')
      // Confirm only 1 renderer total by checking it still resolves correctly
      expect(renderer?.pluginId).toBe('dup-plugin')

      warnSpy.mockRestore()
    })

    it('unregister removes plugin and all its registrations', () => {
      const plugin = makePlugin('rm-plugin')
      registry.register({
        plugin,
        columnRenderers: [{ fieldType: 'Boolean', component: MockComponent, priority: 0 }],
        detailSections: [{ id: 'sec1', label: 'Sec', entityType: 'Customer', component: MockComponent, position: 'after', order: 1 }],
        actionHooks: [{ entityType: 'Customer', operation: 'create', timing: 'before', handler: () => true, priority: 0 }],
        customViews: [{ id: 'view1', label: 'View', entityType: 'Customer', component: MockComponent }],
        themeExtensions: [{ id: 'theme1', name: 'Theme', cssVariables: { '--color': 'red' } }],
      })

      registry.unregister('rm-plugin')

      expect(registry.getPlugin('rm-plugin')).toBeUndefined()
      expect(registry.getPlugins()).toHaveLength(0)
      expect(registry.getColumnRenderer('Boolean')).toBeNull()
      expect(registry.getDetailSections('Customer')).toHaveLength(0)
      expect(registry.getActionHooks('Customer', 'create', 'before')).toHaveLength(0)
      expect(registry.getCustomViews('Customer')).toHaveLength(0)
      expect(registry.getMergedCssVariables()).toEqual({})
    })

    it('register with all optional arrays empty still stores the plugin', () => {
      const plugin = makePlugin('bare-plugin')
      registry.register({ plugin })

      expect(registry.getPlugin('bare-plugin')).toEqual(plugin)
      expect(registry.getPlugins()).toHaveLength(1)
    })

    it('clear() removes all plugins and registrations', () => {
      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [{ fieldType: 'String', component: MockComponent, priority: 0 }],
        detailSections: [{ id: 'sec', label: 'S', entityType: '*', component: MockComponent, position: 'after', order: 1 }],
        actionHooks: [{ entityType: '*', operation: 'create', timing: 'before', handler: () => true, priority: 0 }],
        customViews: [{ id: 'v', label: 'V', entityType: '*', component: MockComponent }],
        themeExtensions: [{ id: 'th', name: 'T', cssVariables: { '--c': '#fff' } }],
      })

      registry.clear()

      expect(registry.getPlugins()).toHaveLength(0)
      expect(registry.getColumnRenderer('String')).toBeNull()
      expect(registry.getDetailSections('Customer')).toHaveLength(0)
      expect(registry.getActionHooks('Customer', 'create', 'before')).toHaveLength(0)
      expect(registry.getCustomViews('Customer')).toHaveLength(0)
      expect(registry.getMergedCssVariables()).toEqual({})
    })
  })

  // ── Column Renderers ────────────────────────────────────────────────────────

  describe('getColumnRenderer', () => {
    it('returns null when no renderers are registered', () => {
      expect(registry.getColumnRenderer('String')).toBeNull()
    })

    it('matches by exact fieldType', () => {
      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [{ fieldType: 'Enum', component: MockComponent, priority: 0 }],
      })

      const result = registry.getColumnRenderer('Enum')
      expect(result).not.toBeNull()
      expect(result?.fieldType).toBe('Enum')
    })

    it('matches by wildcard fieldType ("*")', () => {
      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [{ fieldType: '*', component: MockComponent, priority: 0 }],
      })

      expect(registry.getColumnRenderer('SomeUnknownType')).not.toBeNull()
    })

    it('fieldName match (specificity 2) wins over plain fieldType match (specificity 0)', () => {
      const genericComponent = { template: '<span>generic</span>' }
      const specificComponent = { template: '<span>specific</span>' }

      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [
          { fieldType: 'String', component: genericComponent, priority: 0 },
          { fieldType: 'String', fieldName: 'email', component: specificComponent, priority: 0 },
        ],
      })

      const result = registry.getColumnRenderer('String', undefined, 'email')
      expect(result?.component).toBe(specificComponent)
    })

    it('entityType match (specificity 1) wins over generic (specificity 0)', () => {
      const genericComponent = { template: '<span>generic</span>' }
      const entityComponent = { template: '<span>entity</span>' }

      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [
          { fieldType: 'String', component: genericComponent, priority: 0 },
          { fieldType: 'String', entityType: 'Customer', component: entityComponent, priority: 0 },
        ],
      })

      const result = registry.getColumnRenderer('String', 'Customer')
      expect(result?.component).toBe(entityComponent)
    })

    it('fieldName + entityType (specificity 3) wins over fieldName alone (specificity 2)', () => {
      const nameOnly = { template: '<span>name-only</span>' }
      const nameAndEntity = { template: '<span>name+entity</span>' }

      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [
          { fieldType: 'String', fieldName: 'email', component: nameOnly, priority: 0 },
          { fieldType: 'String', entityType: 'Customer', fieldName: 'email', component: nameAndEntity, priority: 0 },
        ],
      })

      const result = registry.getColumnRenderer('String', 'Customer', 'email')
      expect(result?.component).toBe(nameAndEntity)
    })

    it('higher priority wins among equal specificity', () => {
      const lowPriority = { template: '<span>low</span>' }
      const highPriority = { template: '<span>high</span>' }

      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [
          { fieldType: 'Integer', component: lowPriority, priority: 5 },
          { fieldType: 'Integer', component: highPriority, priority: 10 },
        ],
      })

      const result = registry.getColumnRenderer('Integer')
      expect(result?.component).toBe(highPriority)
    })

    it('entityType filter excludes renderers registered for a different entity', () => {
      registry.register({
        plugin: makePlugin('p1'),
        columnRenderers: [
          { fieldType: 'String', entityType: 'Order', component: MockComponent, priority: 0 },
        ],
      })

      // Querying for Customer should not match an Order-scoped renderer
      expect(registry.getColumnRenderer('String', 'Customer')).toBeNull()
    })
  })

  // ── Detail Sections ─────────────────────────────────────────────────────────

  describe('getDetailSections', () => {
    it('returns empty array when no sections are registered', () => {
      expect(registry.getDetailSections('Customer')).toHaveLength(0)
    })

    it('returns sections registered for the exact entityType', () => {
      registry.register({
        plugin: makePlugin('p1'),
        detailSections: [
          { id: 'addr', label: 'Address', entityType: 'Customer', component: MockComponent, position: 'after', order: 1 },
        ],
      })

      const sections = registry.getDetailSections('Customer')
      expect(sections).toHaveLength(1)
      expect(sections[0].id).toBe('addr')
    })

    it('returns sections registered with wildcard entityType "*"', () => {
      registry.register({
        plugin: makePlugin('p1'),
        detailSections: [
          { id: 'audit', label: 'Audit', entityType: '*', component: MockComponent, position: 'append', order: 99 },
        ],
      })

      expect(registry.getDetailSections('Customer')).toHaveLength(1)
      expect(registry.getDetailSections('Order')).toHaveLength(1)
    })

    it('returns both specific and wildcard sections together', () => {
      registry.register({
        plugin: makePlugin('p1'),
        detailSections: [
          { id: 'specific', label: 'Specific', entityType: 'Customer', component: MockComponent, position: 'after', order: 2 },
          { id: 'wildcard', label: 'Wildcard', entityType: '*', component: MockComponent, position: 'append', order: 5 },
        ],
      })

      const sections = registry.getDetailSections('Customer')
      expect(sections).toHaveLength(2)
      expect(sections.map(s => s.id)).toContain('specific')
      expect(sections.map(s => s.id)).toContain('wildcard')
    })

    it('sorts sections by order ascending', () => {
      registry.register({
        plugin: makePlugin('p1'),
        detailSections: [
          { id: 'last', label: 'Last', entityType: 'Customer', component: MockComponent, position: 'after', order: 30 },
          { id: 'first', label: 'First', entityType: 'Customer', component: MockComponent, position: 'before', order: 5 },
          { id: 'middle', label: 'Middle', entityType: '*', component: MockComponent, position: 'after', order: 15 },
        ],
      })

      const sections = registry.getDetailSections('Customer')
      expect(sections.map(s => s.id)).toEqual(['first', 'middle', 'last'])
    })
  })

  // ── Action Hooks ────────────────────────────────────────────────────────────

  describe('getActionHooks / runBeforeHooks / runAfterHooks', () => {
    it('returns empty array when no hooks are registered', () => {
      expect(registry.getActionHooks('Customer', 'create', 'before')).toHaveLength(0)
    })

    it('matches hooks by exact entityType, operation, and timing', () => {
      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => {}, priority: 0 },
        ],
      })

      expect(registry.getActionHooks('Customer', 'create', 'before')).toHaveLength(1)
    })

    it('wildcard entityType "*" matches any entity', () => {
      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: '*', operation: 'delete', timing: 'before', handler: () => {}, priority: 0 },
        ],
      })

      expect(registry.getActionHooks('Customer', 'delete', 'before')).toHaveLength(1)
      expect(registry.getActionHooks('Order', 'delete', 'before')).toHaveLength(1)
    })

    it('does not match hooks with wrong operation or wrong timing', () => {
      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => {}, priority: 0 },
        ],
      })

      expect(registry.getActionHooks('Customer', 'update', 'before')).toHaveLength(0)
      expect(registry.getActionHooks('Customer', 'create', 'after')).toHaveLength(0)
    })

    it('sorts hooks by priority ascending (lower runs first)', () => {
      const order: number[] = []

      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => { order.push(10) }, priority: 10 },
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => { order.push(1) }, priority: 1 },
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => { order.push(5) }, priority: 5 },
        ],
      })

      const hooks = registry.getActionHooks('Customer', 'create', 'before')
      expect(hooks.map(h => h.priority)).toEqual([1, 5, 10])
    })

    it('runBeforeHooks returns true when all hooks pass', async () => {
      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => true, priority: 0 },
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => undefined, priority: 1 },
        ],
      })

      const result = await registry.runBeforeHooks('Customer', 'create', makeCtx())
      expect(result).toBe(true)
    })

    it('runBeforeHooks returns false when a hook explicitly returns false and stops processing', async () => {
      const secondHook = vi.fn()

      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => false, priority: 1 },
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: secondHook, priority: 2 },
        ],
      })

      const result = await registry.runBeforeHooks('Customer', 'create', makeCtx())
      expect(result).toBe(false)
      expect(secondHook).not.toHaveBeenCalled()
    })

    it('runAfterHooks calls all after-hooks in priority order', async () => {
      const callLog: string[] = []

      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          { entityType: 'Customer', operation: 'update', timing: 'after', handler: async () => { callLog.push('A') }, priority: 10 },
          { entityType: 'Customer', operation: 'update', timing: 'after', handler: async () => { callLog.push('B') }, priority: 1 },
        ],
      })

      await registry.runAfterHooks('Customer', 'update', makeCtx({ operation: 'update' }))
      expect(callLog).toEqual(['B', 'A'])
    })

    it('hooks can be async and are awaited correctly', async () => {
      let resolved = false

      registry.register({
        plugin: makePlugin('p1'),
        actionHooks: [
          {
            entityType: 'Customer',
            operation: 'create',
            timing: 'before',
            handler: () => new Promise<void>(resolve => setTimeout(() => { resolved = true; resolve() }, 0)),
            priority: 0,
          },
        ],
      })

      await registry.runBeforeHooks('Customer', 'create', makeCtx())
      expect(resolved).toBe(true)
    })
  })

  // ── Custom Views ────────────────────────────────────────────────────────────

  describe('getCustomViews', () => {
    it('returns empty array when no custom views are registered', () => {
      expect(registry.getCustomViews('Customer')).toHaveLength(0)
    })

    it('returns views registered for the exact entityType', () => {
      registry.register({
        plugin: makePlugin('p1'),
        customViews: [
          { id: 'timeline', label: 'Timeline', entityType: 'Customer', component: MockComponent },
        ],
      })

      const views = registry.getCustomViews('Customer')
      expect(views).toHaveLength(1)
      expect(views[0].id).toBe('timeline')
    })

    it('returns views registered with wildcard entityType "*"', () => {
      registry.register({
        plugin: makePlugin('p1'),
        customViews: [
          { id: 'map', label: 'Map', entityType: '*', component: MockComponent },
        ],
      })

      expect(registry.getCustomViews('Customer')).toHaveLength(1)
      expect(registry.getCustomViews('Order')).toHaveLength(1)
    })
  })

  // ── Theme Extensions ────────────────────────────────────────────────────────

  describe('getMergedCssVariables', () => {
    it('returns empty object when no theme extensions are registered', () => {
      expect(registry.getMergedCssVariables()).toEqual({})
    })

    it('returns CSS variables from a single theme extension', () => {
      registry.register({
        plugin: makePlugin('p1'),
        themeExtensions: [
          { id: 'theme1', name: 'Theme 1', cssVariables: { '--primary': '#3b82f6', '--radius': '4px' } },
        ],
      })

      expect(registry.getMergedCssVariables()).toEqual({ '--primary': '#3b82f6', '--radius': '4px' })
    })

    it('merges variables from multiple extensions, later registrations overwrite earlier on conflict', () => {
      registry.register({
        plugin: makePlugin('p1'),
        themeExtensions: [
          { id: 'th1', name: 'Theme 1', cssVariables: { '--primary': 'blue', '--font-size': '14px' } },
        ],
      })
      registry.register({
        plugin: makePlugin('p2'),
        themeExtensions: [
          { id: 'th2', name: 'Theme 2', cssVariables: { '--primary': 'red', '--accent': 'green' } },
        ],
      })

      const merged = registry.getMergedCssVariables()
      expect(merged['--primary']).toBe('red')      // Later plugin overwrites
      expect(merged['--font-size']).toBe('14px')   // Not overwritten
      expect(merged['--accent']).toBe('green')     // Added by p2
    })

    it('unregister removes theme extension variables', () => {
      registry.register({
        plugin: makePlugin('p1'),
        themeExtensions: [
          { id: 'th1', name: 'Theme', cssVariables: { '--color': '#ff0000' } },
        ],
      })

      expect(registry.getMergedCssVariables()).toEqual({ '--color': '#ff0000' })

      registry.unregister('p1')

      expect(registry.getMergedCssVariables()).toEqual({})
    })
  })

  // ── Mixed Plugin Scenarios ──────────────────────────────────────────────────

  describe('Multi-registration scenarios', () => {
    it('one plugin can register multiple things across all categories', () => {
      registry.register({
        plugin: makePlugin('full-plugin'),
        columnRenderers: [
          { fieldType: 'Decimal', component: MockComponent, priority: 5 },
        ],
        detailSections: [
          { id: 'notes', label: 'Notes', entityType: 'Customer', component: MockComponent, position: 'after', order: 10 },
        ],
        actionHooks: [
          { entityType: 'Customer', operation: 'create', timing: 'before', handler: () => true, priority: 0 },
        ],
        customViews: [
          { id: 'chart', label: 'Chart', entityType: 'Customer', component: MockComponent },
        ],
        themeExtensions: [
          { id: 'corp', name: 'Corporate', cssVariables: { '--brand': '#123456' } },
        ],
      })

      expect(registry.getColumnRenderer('Decimal')).not.toBeNull()
      expect(registry.getDetailSections('Customer')).toHaveLength(1)
      expect(registry.getActionHooks('Customer', 'create', 'before')).toHaveLength(1)
      expect(registry.getCustomViews('Customer')).toHaveLength(1)
      expect(registry.getMergedCssVariables()).toEqual({ '--brand': '#123456' })
    })

    it('unregistering one plugin does not affect registrations from another plugin', () => {
      registry.register({
        plugin: makePlugin('plugin-a'),
        columnRenderers: [{ fieldType: 'String', component: MockComponent, priority: 1 }],
        detailSections: [{ id: 'sec-a', label: 'A', entityType: 'Customer', component: MockComponent, position: 'after', order: 1 }],
        customViews: [{ id: 'view-a', label: 'A View', entityType: 'Customer', component: MockComponent }],
      })
      registry.register({
        plugin: makePlugin('plugin-b'),
        columnRenderers: [{ fieldType: 'Boolean', component: MockComponent, priority: 1 }],
        detailSections: [{ id: 'sec-b', label: 'B', entityType: 'Customer', component: MockComponent, position: 'after', order: 2 }],
        customViews: [{ id: 'view-b', label: 'B View', entityType: 'Customer', component: MockComponent }],
      })

      registry.unregister('plugin-a')

      // plugin-a registrations are gone
      expect(registry.getPlugin('plugin-a')).toBeUndefined()
      expect(registry.getColumnRenderer('String')).toBeNull()
      expect(registry.getDetailSections('Customer').map(s => s.id)).not.toContain('sec-a')
      expect(registry.getCustomViews('Customer').map(v => v.id)).not.toContain('view-a')

      // plugin-b registrations are untouched
      expect(registry.getPlugin('plugin-b')).toBeDefined()
      expect(registry.getColumnRenderer('Boolean')).not.toBeNull()
      expect(registry.getDetailSections('Customer').map(s => s.id)).toContain('sec-b')
      expect(registry.getCustomViews('Customer').map(v => v.id)).toContain('view-b')
    })
  })
})
