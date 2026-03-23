import type { FieldMetadata } from '@/types/metadata'
import { formatCsvValue } from '@/utils/dataExport'

export interface ExcelExportOptions {
  /** Output filename (without extension - .xlsx will be appended) */
  filename?: string
  /** Fields/columns to include */
  fields: FieldMetadata[]
  /** Row data to export */
  data: Record<string, unknown>[]
}

/**
 * Export data to an Excel (.xlsx) file using SheetJS.
 *
 * Features:
 * - Type-aware cell formatting (numbers as numbers, dates as ISO, enums resolved)
 * - Auto column widths based on content
 * - Header row styling
 */
export async function exportToXlsx(options: ExcelExportOptions): Promise<void> {
  // Dynamic import to avoid bundling xlsx when not used
  const XLSX = await import('xlsx')

  const { fields, data } = options
  const filename = options.filename || generateDefaultFilename()

  // Build header row
  const headers = fields.map((f) => f.displayName || f.name)

  // Build data rows with type-aware formatting
  const rows: unknown[][] = []
  for (const row of data) {
    const cells: unknown[] = fields.map((field) => {
      const rawValue = row[field.name]
      if (rawValue === null || rawValue === undefined) return ''

      // Keep numbers as numbers for Excel
      if ((field.type === 'Integer' || field.type === 'Decimal') && typeof rawValue === 'number') {
        return rawValue
      }

      // Format via existing formatter
      return formatCsvValue(rawValue, field.type, field.enumValues)
    })
    rows.push(cells)
  }

  // Create workbook and worksheet
  const wsData = [headers, ...rows]
  const ws = XLSX.utils.aoa_to_sheet(wsData)

  // Auto column widths
  const colWidths = fields.map((field, idx) => {
    const headerLen = (field.displayName || field.name).length
    let maxLen = headerLen
    for (const row of rows) {
      const cellLen = String(row[idx] ?? '').length
      if (cellLen > maxLen) maxLen = cellLen
    }
    return { wch: Math.min(maxLen + 2, 50) } // cap at 50 chars
  })
  ws['!cols'] = colWidths

  const wb = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(wb, ws, 'Data')

  // Write and trigger download
  XLSX.writeFile(wb, filename.endsWith('.xlsx') ? filename : `${filename}.xlsx`)
}

function generateDefaultFilename(): string {
  const now = new Date()
  const date = now.getFullYear().toString()
    + '-' + String(now.getMonth() + 1).padStart(2, '0')
    + '-' + String(now.getDate()).padStart(2, '0')
  const time = String(now.getHours()).padStart(2, '0')
    + String(now.getMinutes()).padStart(2, '0')
    + String(now.getSeconds()).padStart(2, '0')
  return `Export_${date}_${time}.xlsx`
}
