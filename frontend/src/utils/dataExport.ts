import type { FieldMetadata, FieldType } from '@/types/metadata'

/**
 * Options for exporting entity data to CSV
 */
export interface ExportOptions {
  /** Output filename (default: entity name + timestamp) */
  filename?: string
  /** Fields/columns to include in the export */
  fields: FieldMetadata[]
  /** Row data to export */
  data: Record<string, unknown>[]
  /** Whether to include the header row (default: true) */
  includeHeaders?: boolean
}

/**
 * Formats a single value for CSV output based on its field type.
 *
 * Handles all BMMDL field types:
 * - String/UUID: as-is
 * - Integer/Decimal: number as string
 * - Boolean: "Yes" or "No"
 * - Date: YYYY-MM-DD
 * - DateTime/Timestamp: ISO datetime string
 * - Enum: resolved to display name or enum name if enumValues provided, otherwise raw value
 * - Array: JSON stringified
 * - null/undefined: empty string
 */
export function formatCsvValue(value: unknown, fieldType: FieldType, enumValues?: { name: string; value: number | string; displayName?: string }[]): string {
  if (value === null || value === undefined) {
    return ''
  }

  switch (fieldType) {
    case 'String':
    case 'UUID':
      return String(value)

    case 'Integer':
    case 'Decimal':
      return String(value)

    case 'Boolean':
      return value ? 'Yes' : 'No'

    case 'Date': {
      if (value instanceof Date) {
        return value.toISOString().split('T')[0]
      }
      // If already a string in ISO format, extract date part
      const dateStr = String(value)
      if (dateStr.includes('T')) {
        return dateStr.split('T')[0]
      }
      return dateStr
    }

    case 'Time':
      return String(value)

    case 'DateTime':
    case 'Timestamp': {
      if (value instanceof Date) {
        return value.toISOString()
      }
      return String(value)
    }

    case 'Enum': {
      if (enumValues && enumValues.length > 0) {
        const matched = enumValues.find(ev => ev.value === value || ev.name === value)
        if (matched) {
          return matched.displayName || matched.name
        }
      }
      return String(value)
    }

    case 'Binary':
      return String(value)

    case 'Array': {
      try {
        return JSON.stringify(value)
      } catch {
        return String(value)
      }
    }

    default:
      return String(value)
  }
}

/**
 * Generates a timestamped filename for exports.
 *
 * @param entityName - The entity name to use as prefix
 * @param extension - File extension (default: 'csv')
 * @returns Filename in format "EntityName_2026-02-07_143025.csv"
 */
export function generateFilename(entityName: string, extension: string = 'csv'): string {
  const now = new Date()

  const date = now.getFullYear().toString()
    + '-' + String(now.getMonth() + 1).padStart(2, '0')
    + '-' + String(now.getDate()).padStart(2, '0')

  const time = String(now.getHours()).padStart(2, '0')
    + String(now.getMinutes()).padStart(2, '0')
    + String(now.getSeconds()).padStart(2, '0')

  return `${entityName}_${date}_${time}.${extension}`
}

/**
 * Escapes a value for safe inclusion in a CSV cell.
 *
 * Per RFC 4180:
 * - If the value contains a comma, newline, or double quote, wrap it in double quotes
 * - Double quotes within the value are escaped by doubling them
 */
function escapeCsvCell(value: string): string {
  if (value.includes(',') || value.includes('\n') || value.includes('\r') || value.includes('"')) {
    return '"' + value.replace(/"/g, '""') + '"'
  }
  return value
}

/**
 * Exports entity data to a CSV file and triggers a browser download.
 *
 * Features:
 * - UTF-8 BOM prefix for Excel compatibility
 * - Proper CSV escaping (RFC 4180)
 * - Type-aware value formatting for all BMMDL field types
 * - Automatic filename generation if not provided
 */
export function exportToCsv(options: ExportOptions): void {
  const {
    fields,
    data,
    includeHeaders = true,
  } = options

  const filename = options.filename || generateFilename('Export')

  const lines: string[] = []

  // Header row
  if (includeHeaders) {
    const headerCells = fields.map(field =>
      escapeCsvCell(field.displayName || field.name)
    )
    lines.push(headerCells.join(','))
  }

  // Data rows
  for (const row of data) {
    const cells = fields.map(field => {
      const rawValue = row[field.name]
      const formatted = formatCsvValue(rawValue, field.type, field.enumValues)
      return escapeCsvCell(formatted)
    })
    lines.push(cells.join(','))
  }

  const csvContent = lines.join('\r\n')

  // Prepend UTF-8 BOM for Excel compatibility
  const bom = '\uFEFF'
  const blob = new Blob([bom + csvContent], { type: 'text/csv;charset=utf-8;' })

  // Trigger browser download
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.style.display = 'none'

  document.body.appendChild(anchor)
  anchor.click()

  // Clean up after the browser has had time to initiate the download
  setTimeout(() => {
    document.body.removeChild(anchor)
    URL.revokeObjectURL(url)
  }, 100)
}

/**
 * Exports a CSV template for the given entity with headers and one sample row
 * showing expected value formats per field type.
 *
 * Skips computed and read-only fields since they cannot be imported.
 */
export function exportTemplate(entityName: string, fields: FieldMetadata[]): void {
  const importableFields = fields.filter((f) => !f.isComputed && !f.isReadOnly)

  const sampleForType: Record<FieldType, string> = {
    String: 'text',
    Integer: '123',
    Decimal: '123.45',
    Boolean: 'true',
    Date: '2024-01-15',
    Time: '14:30:00',
    DateTime: '2024-01-15T14:30:00Z',
    Timestamp: '2024-01-15T14:30:00Z',
    UUID: '00000000-0000-0000-0000-000000000000',
    Binary: '',
    Enum: '',
    Array: '[]',
  }

  const sampleRow: Record<string, unknown> = {}
  for (const field of importableFields) {
    if (field.type === 'Enum' && field.enumValues && field.enumValues.length > 0) {
      sampleRow[field.name] = field.enumValues[0].name
    } else {
      sampleRow[field.name] = sampleForType[field.type] ?? ''
    }
  }

  exportToCsv({
    filename: generateFilename(`${entityName}_template`),
    fields: importableFields,
    data: [sampleRow],
    includeHeaders: true,
  })
}
