import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

// ---------------------------------------------------------------------------
// Stub lucide icons to prevent jsdom render issues
// ---------------------------------------------------------------------------

vi.mock('lucide-vue-next', () => ({
  AlertCircle: { template: '<span data-icon="alert-circle" />' },
  RefreshCw: { template: '<span data-icon="refresh-cw" />' },
  Download: { template: '<span data-icon="download" />' },
  Printer: { template: '<span data-icon="printer" />' },
}))

// ---------------------------------------------------------------------------
// Stub pdfGenerator utilities
// ---------------------------------------------------------------------------

vi.mock('@/utils/pdfGenerator', () => ({
  formatValue: vi.fn((value: unknown, _format?: string) => (value == null ? '' : String(value))),
  computeAggregates: vi.fn((fields: { name: string; aggregate?: string }[], rows: Record<string, unknown>[]) => {
    const result: Record<string, string> = {}
    for (const f of fields) {
      if (!f.aggregate) continue
      const values = rows.map(r => Number(r[f.name])).filter(v => !isNaN(v))
      const sum = values.reduce((acc, v) => acc + v, 0)
      result[f.name] = `${f.aggregate.toUpperCase()}: ${sum}`
    }
    return result
  }),
}))

// ---------------------------------------------------------------------------
// Import component AFTER mocks are registered
// ---------------------------------------------------------------------------

import ReportPreview from '../ReportPreview.vue'
import type { ReportTemplate } from '@/services/reportService'

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function createTestTemplate(overrides?: Partial<ReportTemplate>): ReportTemplate {
  return {
    id: '1',
    name: 'Test Report',
    module: 'crm',
    entityType: 'Customer',
    layoutType: 'list',
    fields: [
      { name: 'Name', label: 'Name', width: 150 },
      { name: 'Status', label: 'Status', width: 100 },
    ],
    sortBy: [],
    scheduleRecipients: [],
    isPublic: false,
    createdAt: '2024-01-01',
    ...overrides,
  }
}

function mountPreview(
  template: ReportTemplate,
  rows: Record<string, unknown>[],
  extra?: { loading?: boolean; error?: string | null }
) {
  return mount(ReportPreview, {
    props: {
      template,
      rows,
      loading: extra?.loading ?? false,
      error: extra?.error ?? null,
    },
  })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('ReportPreview', () => {
  // 1. Loading spinner
  it('renders loading spinner when loading=true', () => {
    const wrapper = mountPreview(createTestTemplate(), [], { loading: true })
    // The loading section renders when loading prop is true
    const loadingDiv = wrapper.find('.flex.items-center.justify-center.py-12')
    expect(loadingDiv.exists()).toBe(true)
  })

  // 2. Error message
  it('renders error message when error prop is set', () => {
    const wrapper = mountPreview(createTestTemplate(), [], { error: 'Failed to load report data.' })
    expect(wrapper.text()).toContain('Failed to load report data.')
    expect(wrapper.find('[data-icon="alert-circle"]').exists()).toBe(true)
  })

  // 3. Empty state
  it('renders empty state when rows=[] and not loading', () => {
    const wrapper = mountPreview(createTestTemplate(), [])
    expect(wrapper.text()).toContain('No data to preview.')
    expect(wrapper.text()).toContain('Click Refresh to load report data.')
  })

  // 4. Table headers for list layout
  it('renders table with correct headers for list layout', () => {
    const template = createTestTemplate()
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)

    const headers = wrapper.findAll('thead th')
    expect(headers).toHaveLength(2)
    expect(headers[0].text()).toBe('Name')
    expect(headers[1].text()).toBe('Status')
  })

  // 5. Formatted cell values
  it('renders formatted cell values using formatValue', () => {
    const template = createTestTemplate()
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)

    const cells = wrapper.findAll('tbody td')
    expect(cells[0].text()).toBe('Alice')
    expect(cells[1].text()).toBe('Active')
  })

  // 6. Aggregate footer row
  it('renders aggregate footer row when field has aggregate', () => {
    const template = createTestTemplate({
      fields: [
        { name: 'Name', label: 'Name', width: 150 },
        { name: 'Amount', label: 'Amount', width: 100, aggregate: 'sum' },
      ],
    })
    const rows = [
      { Name: 'Alice', Amount: 100 },
      { Name: 'Bob', Amount: 200 },
    ]
    const wrapper = mountPreview(template, rows)

    const tfoot = wrapper.find('tfoot')
    expect(tfoot.exists()).toBe(true)
    expect(tfoot.text()).toContain('SUM: 300')
  })

  // 7. Emits 'refresh' when Refresh button clicked
  it("emits 'refresh' when Refresh button is clicked", async () => {
    const wrapper = mountPreview(createTestTemplate(), [])
    // Refresh button is always visible in toolbar
    const buttons = wrapper.findAll('button')
    const refreshBtn = buttons.find(b => b.text().includes('Refresh'))
    expect(refreshBtn).toBeDefined()
    await refreshBtn!.trigger('click')
    expect(wrapper.emitted('refresh')).toBeTruthy()
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })

  // 8. Emits 'download-pdf' when Download PDF button clicked
  it("emits 'download-pdf' when Download PDF button is clicked", async () => {
    const template = createTestTemplate()
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)

    const buttons = wrapper.findAll('button')
    const downloadBtn = buttons.find(b => b.text().includes('Download PDF'))
    expect(downloadBtn).toBeDefined()
    await downloadBtn!.trigger('click')
    expect(wrapper.emitted('download-pdf')).toBeTruthy()
    expect(wrapper.emitted('download-pdf')).toHaveLength(1)
  })

  // 9. Emits 'print' when Print button clicked
  it("emits 'print' when Print button is clicked", async () => {
    const template = createTestTemplate()
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)

    const buttons = wrapper.findAll('button')
    const printBtn = buttons.find(b => b.text().includes('Print'))
    expect(printBtn).toBeDefined()
    await printBtn!.trigger('click')
    expect(wrapper.emitted('print')).toBeTruthy()
    expect(wrapper.emitted('print')).toHaveLength(1)
  })

  // 10. Shows row count in toolbar
  it('shows row count in toolbar', () => {
    const template = createTestTemplate()
    const rows = [
      { Name: 'Alice', Status: 'Active' },
      { Name: 'Bob', Status: 'Inactive' },
      { Name: 'Carol', Status: 'Active' },
    ]
    const wrapper = mountPreview(template, rows)
    expect(wrapper.text()).toContain('Showing 3 rows')
  })

  it('shows "Showing 1 row" (singular) when exactly one row', () => {
    const template = createTestTemplate()
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)
    expect(wrapper.text()).toContain('Showing 1 row')
    expect(wrapper.text()).not.toContain('Showing 1 rows')
  })

  // 11. Detail layout: card-style label/value pairs
  it('renders card-style label/value pairs for detail layout', () => {
    const template = createTestTemplate({ layoutType: 'detail' })
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)

    // Detail layout renders cards, not a table
    expect(wrapper.find('table').exists()).toBe(false)
    // Should show "Record 1"
    expect(wrapper.text()).toContain('Record 1')
    // Should display field labels
    expect(wrapper.text()).toContain('Name')
    expect(wrapper.text()).toContain('Status')
    // Should display values
    expect(wrapper.text()).toContain('Alice')
    expect(wrapper.text()).toContain('Active')
  })

  // 12. Summary layout: group header row
  it('renders group header row for summary layout grouped by a field', () => {
    const template = createTestTemplate({
      layoutType: 'summary',
      groupBy: 'Status',
      fields: [
        { name: 'Name', label: 'Name', width: 150 },
        { name: 'Status', label: 'Status', width: 100 },
      ],
    })
    const rows = [
      { Name: 'Alice', Status: 'Active' },
      { Name: 'Bob', Status: 'Inactive' },
      { Name: 'Carol', Status: 'Active' },
    ]
    const wrapper = mountPreview(template, rows)

    // Group header divs should contain "Status: Active" and "Status: Inactive"
    const text = wrapper.text()
    expect(text).toContain('Status: Active')
    expect(text).toContain('Status: Inactive')
  })

  // 13. Report header text above table
  it('renders report header text above the table', () => {
    const template = createTestTemplate({ header: 'Confidential — Q1 2024' })
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)
    expect(wrapper.text()).toContain('Confidential — Q1 2024')
  })

  // 14. Report footer text below table
  it('renders report footer text below the table', () => {
    const template = createTestTemplate({ footer: 'Generated by BMMDL Reporting' })
    const rows = [{ Name: 'Alice', Status: 'Active' }]
    const wrapper = mountPreview(template, rows)
    expect(wrapper.text()).toContain('Generated by BMMDL Reporting')
  })

  // 15. Shows '0 rows' text when rows are empty but toolbar is still visible
  it('shows "Showing 0 rows" text when rows are empty', () => {
    const wrapper = mountPreview(createTestTemplate(), [])
    expect(wrapper.text()).toContain('Showing 0 rows')
  })
})
