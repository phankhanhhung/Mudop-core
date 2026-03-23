import Papa from 'papaparse'
import type { FieldMetadata, FieldType } from '@/types/metadata'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface ParseResult {
  headers: string[]
  rows: Record<string, string>[]
  errors: string[]
}

export interface ColumnMapping {
  csvHeader: string
  fieldName: string | null
  field: FieldMetadata | null
  auto: boolean
}

export interface ValidationResult {
  valid: boolean
  errors: Record<string, string>
}

export interface ImportResult {
  validRows: Record<string, unknown>[]
  invalidRows: { rowIndex: number; data: Record<string, string>; errors: Record<string, string> }[]
  totalRows: number
}

// ---------------------------------------------------------------------------
// CSV Parsing
// ---------------------------------------------------------------------------

/**
 * Parse a CSV file using papaparse. Expects a header row.
 */
export function parseCsvFile(file: File): Promise<ParseResult> {
  return new Promise((resolve) => {
    Papa.parse(file, {
      header: true,
      skipEmptyLines: true,
      complete(results) {
        const errors: string[] = []
        for (const err of results.errors) {
          errors.push(`Row ${err.row ?? '?'}: ${err.message}`)
        }
        resolve({
          headers: results.meta.fields ?? [],
          rows: results.data as Record<string, string>[],
          errors,
        })
      },
      error(err) {
        resolve({ headers: [], rows: [], errors: [err.message] })
      },
    })
  })
}

/**
 * Placeholder for Excel parsing (not supported yet).
 */
export function parseExcelFile(_file: File): Promise<ParseResult> {
  return Promise.resolve({
    headers: [],
    rows: [],
    errors: ['Excel format is not supported yet. Please use CSV.'],
  })
}

// ---------------------------------------------------------------------------
// Column Mapping
// ---------------------------------------------------------------------------

/**
 * Normalize a name for comparison: lowercase, strip underscores/hyphens/spaces.
 */
function normalize(name: string): string {
  return name.toLowerCase().replace(/[-_ ]/g, '')
}

/**
 * Auto-map CSV column headers to entity fields by case-insensitive name matching.
 * Handles snake_case, PascalCase, camelCase, and display names.
 */
export function mapColumns(headers: string[], fields: FieldMetadata[]): ColumnMapping[] {
  return headers.map((header) => {
    const normalizedHeader = normalize(header)

    // Try exact match first, then normalized match against name and displayName
    const match = fields.find((f) => {
      if (f.name === header) return true
      if (normalize(f.name) === normalizedHeader) return true
      if (f.displayName && normalize(f.displayName) === normalizedHeader) return true
      return false
    })

    return {
      csvHeader: header,
      fieldName: match?.name ?? null,
      field: match ?? null,
      auto: !!match,
    }
  })
}

// ---------------------------------------------------------------------------
// Type Coercion
// ---------------------------------------------------------------------------

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Coerce a raw CSV string value to the proper JavaScript type
 * based on the field metadata.
 */
export function coerceValue(value: string, field: FieldMetadata): { value: unknown; error: string | null } {
  const trimmed = value.trim()

  // Empty value handling
  if (trimmed === '') {
    return { value: null, error: null }
  }

  switch (field.type as FieldType) {
    case 'String':
      return { value: trimmed, error: null }

    case 'Integer': {
      const num = parseInt(trimmed, 10)
      if (isNaN(num)) return { value: null, error: `"${trimmed}" is not a valid integer` }
      return { value: num, error: null }
    }

    case 'Decimal': {
      const num = parseFloat(trimmed)
      if (isNaN(num)) return { value: null, error: `"${trimmed}" is not a valid decimal` }
      return { value: num, error: null }
    }

    case 'Boolean': {
      const lower = trimmed.toLowerCase()
      if (['true', 'yes', '1'].includes(lower)) return { value: true, error: null }
      if (['false', 'no', '0'].includes(lower)) return { value: false, error: null }
      return { value: null, error: `"${trimmed}" is not a valid boolean (use true/false, yes/no, 1/0)` }
    }

    case 'Date': {
      // Accept YYYY-MM-DD
      if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
        const d = new Date(trimmed + 'T00:00:00')
        if (!isNaN(d.getTime())) return { value: trimmed, error: null }
      }
      return { value: null, error: `"${trimmed}" is not a valid date (use YYYY-MM-DD)` }
    }

    case 'DateTime':
    case 'Timestamp': {
      const d = new Date(trimmed)
      if (!isNaN(d.getTime())) return { value: d.toISOString(), error: null }
      return { value: null, error: `"${trimmed}" is not a valid date/time` }
    }

    case 'Time':
      // Accept HH:MM or HH:MM:SS
      if (/^\d{2}:\d{2}(:\d{2})?$/.test(trimmed)) return { value: trimmed, error: null }
      return { value: null, error: `"${trimmed}" is not a valid time (use HH:MM or HH:MM:SS)` }

    case 'UUID':
      if (UUID_REGEX.test(trimmed)) return { value: trimmed, error: null }
      return { value: null, error: `"${trimmed}" is not a valid UUID` }

    case 'Enum': {
      if (!field.enumValues || field.enumValues.length === 0) {
        return { value: trimmed, error: null }
      }
      const matched = field.enumValues.find(
        (ev) =>
          ev.name.toLowerCase() === trimmed.toLowerCase() ||
          String(ev.value) === trimmed ||
          (ev.displayName && ev.displayName.toLowerCase() === trimmed.toLowerCase())
      )
      if (matched) return { value: matched.value, error: null }
      const allowed = field.enumValues.map((ev) => ev.name).join(', ')
      return { value: null, error: `"${trimmed}" is not a valid enum value. Allowed: ${allowed}` }
    }

    case 'Binary':
      return { value: trimmed, error: null }

    case 'Array':
      try {
        return { value: JSON.parse(trimmed), error: null }
      } catch {
        return { value: null, error: `"${trimmed}" is not valid JSON for array field` }
      }

    default:
      return { value: trimmed, error: null }
  }
}

// ---------------------------------------------------------------------------
// Row Validation
// ---------------------------------------------------------------------------

/**
 * Validate a single row of data against field metadata.
 * Checks required fields and type validity.
 */
export function validateRow(
  row: Record<string, unknown>,
  fields: FieldMetadata[]
): ValidationResult {
  const errors: Record<string, string> = {}

  for (const field of fields) {
    const value = row[field.name]

    // Skip computed/readonly fields
    if (field.isComputed || field.isReadOnly) continue

    // Required check (skip key fields as they may be auto-generated)
    if (field.isRequired && (value === null || value === undefined || value === '')) {
      if (field.type !== 'UUID') {
        errors[field.name] = `${field.displayName || field.name} is required`
      }
    }
  }

  return {
    valid: Object.keys(errors).length === 0,
    errors,
  }
}

// ---------------------------------------------------------------------------
// Data Transformation
// ---------------------------------------------------------------------------

/**
 * Transform raw CSV rows into typed entity objects, applying column mappings
 * and type coercion, with per-row validation.
 */
export function transformImportData(
  rows: Record<string, string>[],
  mappings: ColumnMapping[],
  fields: FieldMetadata[]
): ImportResult {
  const validRows: Record<string, unknown>[] = []
  const invalidRows: ImportResult['invalidRows'] = []

  // Build mapping lookup: csvHeader -> (fieldName, field)
  const activeMappings = mappings.filter((m) => m.fieldName && m.field)

  for (let i = 0; i < rows.length; i++) {
    const rawRow = rows[i]
    const typedRow: Record<string, unknown> = {}
    const rowErrors: Record<string, string> = {}

    for (const mapping of activeMappings) {
      const rawValue = rawRow[mapping.csvHeader] ?? ''
      const field = mapping.field!

      // Skip computed/readonly fields
      if (field.isComputed || field.isReadOnly) continue

      const { value, error } = coerceValue(rawValue, field)

      if (error) {
        rowErrors[mapping.fieldName!] = error
      } else if (value !== null) {
        typedRow[mapping.fieldName!] = value
      }
    }

    // Validate row (required fields, etc.)
    const validation = validateRow(typedRow, fields)
    const allErrors = { ...rowErrors, ...validation.errors }

    if (Object.keys(allErrors).length === 0) {
      validRows.push(typedRow)
    } else {
      invalidRows.push({
        rowIndex: i + 1, // 1-based for user display
        data: rawRow,
        errors: allErrors,
      })
    }
  }

  return {
    validRows,
    invalidRows,
    totalRows: rows.length,
  }
}
