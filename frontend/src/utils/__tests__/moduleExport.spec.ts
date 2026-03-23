import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { FieldMetadata, EntityMetadata } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

vi.mock('jszip', () => {
  const mockFolder = {
    file: vi.fn(),
  }
  const MockJSZip = vi.fn().mockImplementation(() => ({
    folder: vi.fn().mockReturnValue(mockFolder),
    generateAsync: vi.fn().mockResolvedValue(new Blob(['test'])),
    file: vi.fn(),
  }))
  return { default: MockJSZip }
})

vi.mock('@/utils/dataExport', () => ({
  formatCsvValue: vi.fn((val) => String(val ?? '')),
  generateFilename: vi.fn((entityType) => `${entityType}.csv`),
}))

global.URL.createObjectURL = vi.fn().mockReturnValue('blob:mock-url')
global.URL.revokeObjectURL = vi.fn()

// ---------------------------------------------------------------------------
// Imports (after mocks)
// ---------------------------------------------------------------------------

import { buildCsvContent, buildSchemaCsv, packageAsZip, downloadBlob } from '@/utils/moduleExport'
import JSZip from 'jszip'
import { formatCsvValue } from '@/utils/dataExport'

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

function makeEntity(overrides: Partial<EntityMetadata> = {}): EntityMetadata {
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
// buildCsvContent
// ---------------------------------------------------------------------------

describe('buildCsvContent', () => {
  beforeEach(() => {
    vi.mocked(formatCsvValue).mockImplementation((val) => String(val ?? ''))
  })

  it('returns a string starting with UTF-8 BOM', () => {
    const fields = [makeField({ name: 'Name' })]
    const result = buildCsvContent(fields, [])
    expect(result.startsWith('\uFEFF')).toBe(true)
  })

  it('uses displayName for header when present', () => {
    const fields = [makeField({ name: 'customerName', displayName: 'Customer Name' })]
    const result = buildCsvContent(fields, [])
    expect(result).toContain('Customer Name')
    expect(result).not.toContain('customerName')
  })

  it('falls back to field name when displayName is absent', () => {
    const fields = [makeField({ name: 'Email', displayName: undefined })]
    const result = buildCsvContent(fields, [])
    expect(result).toContain('Email')
  })

  it('skips computed fields in the header row', () => {
    const fields = [
      makeField({ name: 'Name', isComputed: false }),
      makeField({ name: 'OrderCount', isComputed: true }),
    ]
    const result = buildCsvContent(fields, [])
    expect(result).toContain('Name')
    expect(result).not.toContain('OrderCount')
  })

  it('skips computed fields in data rows', () => {
    const fields = [
      makeField({ name: 'Name', isComputed: false }),
      makeField({ name: 'OrderCount', isComputed: true }),
    ]
    vi.mocked(formatCsvValue).mockImplementation((val) => String(val ?? ''))
    const rows = [{ Name: 'Alice', OrderCount: 5 }]
    const result = buildCsvContent(fields, rows)
    const lines = result.replace('\uFEFF', '').split('\r\n')
    // Only one column (Name) should appear in the data row
    expect(lines[1]).toBe('Alice')
  })

  it('returns only the header line when rows array is empty', () => {
    const fields = [makeField({ name: 'ID' }), makeField({ name: 'Name' })]
    const result = buildCsvContent(fields, [])
    const lines = result.replace('\uFEFF', '').split('\r\n')
    expect(lines).toHaveLength(1)
    expect(lines[0]).toBe('ID,Name')
  })

  it('wraps header cell in quotes when displayName contains a comma', () => {
    const fields = [makeField({ name: 'f', displayName: 'Last, First' })]
    const result = buildCsvContent(fields, [])
    expect(result).toContain('"Last, First"')
  })

  it('produces one data row per input row using formatCsvValue', () => {
    vi.mocked(formatCsvValue).mockImplementation((val) => String(val ?? ''))
    const fields = [makeField({ name: 'Name' }), makeField({ name: 'Age', type: 'Integer' })]
    const rows = [{ Name: 'Alice', Age: 30 }]
    const result = buildCsvContent(fields, rows)
    const lines = result.replace('\uFEFF', '').split('\r\n')
    expect(lines).toHaveLength(2)
    expect(lines[1]).toBe('Alice,30')
  })

  it('wraps data values containing commas in double quotes', () => {
    vi.mocked(formatCsvValue).mockImplementation((val) => String(val ?? ''))
    const fields = [makeField({ name: 'Address' })]
    const rows = [{ Address: '123 Main St, Apt 4' }]
    const result = buildCsvContent(fields, rows)
    expect(result).toContain('"123 Main St, Apt 4"')
  })

  it('escapes double quotes inside data values by doubling them', () => {
    vi.mocked(formatCsvValue).mockImplementation((val) => String(val ?? ''))
    const fields = [makeField({ name: 'Note' })]
    const rows = [{ Note: 'She said "hello"' }]
    const result = buildCsvContent(fields, rows)
    expect(result).toContain('"She said ""hello"""')
  })

  it('wraps data values containing newlines in double quotes', () => {
    vi.mocked(formatCsvValue).mockImplementation((val) => String(val ?? ''))
    const fields = [makeField({ name: 'Description' })]
    const rows = [{ Description: 'line1\nline2' }]
    const result = buildCsvContent(fields, rows)
    expect(result).toContain('"line1\nline2"')
  })
})

// ---------------------------------------------------------------------------
// buildSchemaCsv
// ---------------------------------------------------------------------------

describe('buildSchemaCsv', () => {
  it('starts with UTF-8 BOM', () => {
    const result = buildSchemaCsv([])
    expect(result.startsWith('\uFEFF')).toBe(true)
  })

  it('has the correct header row', () => {
    const result = buildSchemaCsv([])
    const firstLine = result.replace('\uFEFF', '').split('\r\n')[0]
    expect(firstLine).toBe('Entity,Field,Type,Required,ReadOnly,Computed,MaxLength')
  })

  it('returns only the header when entities array is empty', () => {
    const result = buildSchemaCsv([])
    const lines = result.replace('\uFEFF', '').split('\r\n')
    expect(lines).toHaveLength(1)
  })

  it('returns only the header when entity has no fields', () => {
    const entity = makeEntity({ name: 'Empty', fields: [] })
    const result = buildSchemaCsv([entity])
    const lines = result.replace('\uFEFF', '').split('\r\n')
    expect(lines).toHaveLength(1)
  })

  it('produces one data row per field', () => {
    const entity = makeEntity({
      name: 'Customer',
      fields: [
        makeField({ name: 'ID', type: 'UUID' }),
        makeField({ name: 'Name', type: 'String' }),
      ],
    })
    const result = buildSchemaCsv([entity])
    const lines = result.replace('\uFEFF', '').split('\r\n')
    expect(lines).toHaveLength(3) // header + 2 data rows
  })

  it('includes entity name, field name, and type in each data row', () => {
    const entity = makeEntity({
      name: 'Order',
      fields: [makeField({ name: 'Total', type: 'Decimal' })],
    })
    const result = buildSchemaCsv([entity])
    const dataRow = result.replace('\uFEFF', '').split('\r\n')[1]
    expect(dataRow).toContain('Order')
    expect(dataRow).toContain('Total')
    expect(dataRow).toContain('Decimal')
  })

  it('outputs Yes for isRequired when true, No when false', () => {
    const entity = makeEntity({
      name: 'Item',
      fields: [
        makeField({ name: 'Req', isRequired: true }),
        makeField({ name: 'Opt', isRequired: false }),
      ],
    })
    const result = buildSchemaCsv([entity])
    const lines = result.replace('\uFEFF', '').split('\r\n')
    expect(lines[1].split(',')[3]).toBe('Yes')
    expect(lines[2].split(',')[3]).toBe('No')
  })

  it('outputs Yes for isReadOnly and isComputed when both are true', () => {
    const entity = makeEntity({
      name: 'Item',
      fields: [makeField({ name: 'Computed', isReadOnly: true, isComputed: true })],
    })
    const result = buildSchemaCsv([entity])
    const cols = result.replace('\uFEFF', '').split('\r\n')[1].split(',')
    expect(cols[4]).toBe('Yes') // ReadOnly
    expect(cols[5]).toBe('Yes') // Computed
  })

  it('includes maxLength value when set on field', () => {
    const entity = makeEntity({
      name: 'Item',
      fields: [makeField({ name: 'Name', type: 'String', maxLength: 255 })],
    })
    const result = buildSchemaCsv([entity])
    const dataRow = result.replace('\uFEFF', '').split('\r\n')[1]
    expect(dataRow.endsWith('255')).toBe(true)
  })

  it('leaves maxLength column empty when maxLength is not set', () => {
    const entity = makeEntity({
      name: 'Item',
      fields: [makeField({ name: 'ID', type: 'UUID', maxLength: undefined })],
    })
    const result = buildSchemaCsv([entity])
    const dataRow = result.replace('\uFEFF', '').split('\r\n')[1]
    expect(dataRow.endsWith(',')).toBe(true)
  })
})

// ---------------------------------------------------------------------------
// packageAsZip
// ---------------------------------------------------------------------------

// JSZip instance mocks — shared across packageAsZip tests and reset per-test
const zipMocks = {
  folderFile: vi.fn(),
  folderFn: vi.fn(),
  generateAsync: vi.fn(),
}

function resetZipMocks() {
  zipMocks.folderFile.mockReset()
  zipMocks.folderFn.mockReset()
  zipMocks.generateAsync.mockReset()
  zipMocks.folderFn.mockReturnValue({ file: zipMocks.folderFile })
  zipMocks.generateAsync.mockResolvedValue(new Blob(['test']))
  vi.mocked(JSZip).mockImplementation(function (this: unknown) {
    Object.assign(this as object, {
      folder: zipMocks.folderFn,
      generateAsync: zipMocks.generateAsync,
      file: vi.fn(),
    })
  } as unknown as typeof JSZip)
}

describe('packageAsZip', () => {
  beforeEach(() => {
    resetZipMocks()
  })

  it('returns a Blob', async () => {
    const result = await packageAsZip('MyModule', '', [])
    expect(result).toBeInstanceOf(Blob)
  })

  it('includes _schema.csv in zip when schemaContent is non-empty', async () => {
    await packageAsZip('MyModule', 'schema content', [])
    expect(zipMocks.folderFile).toHaveBeenCalledWith('_schema.csv', 'schema content')
  })

  it('does not include _schema.csv when schemaContent is empty string', async () => {
    await packageAsZip('MyModule', '', [])
    const filenames = zipMocks.folderFile.mock.calls.map((c: unknown[]) => c[0])
    expect(filenames).not.toContain('_schema.csv')
  })

  it('adds a CSV file named {entityType}.csv for each entityResult', async () => {
    const results = [
      { entityType: 'Customer', rowCount: 2, csvContent: 'csv1' },
      { entityType: 'Order', rowCount: 3, csvContent: 'csv2' },
    ]
    await packageAsZip('MyModule', '', results)
    const filenames = zipMocks.folderFile.mock.calls.map((c: unknown[]) => c[0])
    expect(filenames).toContain('Customer.csv')
    expect(filenames).toContain('Order.csv')
  })

  it('creates a folder named after the moduleName', async () => {
    await packageAsZip('SalesModule', '', [])
    expect(zipMocks.folderFn).toHaveBeenCalledWith('SalesModule')
  })
})

// ---------------------------------------------------------------------------
// downloadBlob
// ---------------------------------------------------------------------------

describe('downloadBlob', () => {
  let mockAnchor: {
    href: string
    download: string
    style: { display: string }
    click: ReturnType<typeof vi.fn>
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(global.URL.createObjectURL).mockReturnValue('blob:mock-url')

    mockAnchor = {
      href: '',
      download: '',
      style: { display: '' },
      click: vi.fn(),
    }

    vi.spyOn(document, 'createElement').mockReturnValue(mockAnchor as unknown as HTMLElement)
    vi.spyOn(document.body, 'appendChild').mockImplementation(() => mockAnchor as unknown as Node)
    vi.spyOn(document.body, 'removeChild').mockImplementation(() => mockAnchor as unknown as Node)
  })

  it('calls URL.createObjectURL with the provided blob', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'test.csv')
    expect(global.URL.createObjectURL).toHaveBeenCalledWith(blob)
  })

  it('creates an anchor element via document.createElement', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'test.csv')
    expect(document.createElement).toHaveBeenCalledWith('a')
  })

  it('sets href on the anchor to the created object URL', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'export.zip')
    expect(mockAnchor.href).toBe('blob:mock-url')
  })

  it('sets download attribute to the provided filename', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'my-export.zip')
    expect(mockAnchor.download).toBe('my-export.zip')
  })

  it('appends the anchor to document.body then removes it', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'test.csv')
    expect(document.body.appendChild).toHaveBeenCalled()
    expect(document.body.removeChild).toHaveBeenCalled()
  })

  it('clicks the anchor to trigger the download', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'test.csv')
    expect(mockAnchor.click).toHaveBeenCalledOnce()
  })

  it('revokes the object URL after clicking', () => {
    const blob = new Blob(['data'])
    downloadBlob(blob, 'test.csv')
    expect(global.URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')
  })
})
