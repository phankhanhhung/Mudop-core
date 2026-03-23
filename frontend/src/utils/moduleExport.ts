import JSZip from 'jszip'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import { formatCsvValue, generateFilename } from '@/utils/dataExport'

export { generateFilename }

/**
 * Escapes a value for safe inclusion in a CSV cell.
 * Per RFC 4180: wrap in double quotes if the value contains a comma, newline,
 * or double quote; escape internal double quotes by doubling them.
 *
 * Note: escapeCsvCell is not exported from dataExport.ts, so it is defined here.
 */
function escapeCsvCell(value: string): string {
  if (value.includes(',') || value.includes('\n') || value.includes('\r') || value.includes('"')) {
    return '"' + value.replace(/"/g, '""') + '"'
  }
  return value
}

export interface EntityExportResult {
  entityType: string
  rowCount: number
  csvContent: string
}

export interface ModuleExportProgress {
  currentEntity: string
  entitiesCompleted: number
  totalEntities: number
  rowsFetched: number
}

export type ProgressCallback = (progress: ModuleExportProgress) => void

/**
 * Build a CSV string from entity data rows and field metadata.
 * Skips computed fields. Adds a UTF-8 BOM prefix for Excel compatibility.
 */
export function buildCsvContent(
  fields: FieldMetadata[],
  rows: Record<string, unknown>[],
): string {
  const exportableFields = fields.filter((f) => !f.isComputed)

  const headerLine = exportableFields
    .map((f) => escapeCsvCell(f.displayName ?? f.name))
    .join(',')

  const dataLines = rows.map((row) => {
    return exportableFields
      .map((f) => {
        const val = formatCsvValue(row[f.name], f.type, f.enumValues)
        return escapeCsvCell(val)
      })
      .join(',')
  })

  const bom = '\uFEFF'
  return bom + [headerLine, ...dataLines].join('\r\n')
}

/**
 * Build a schema summary CSV listing all fields across entities.
 * Adds a UTF-8 BOM prefix for Excel compatibility.
 */
export function buildSchemaCsv(entities: EntityMetadata[]): string {
  const headerLine = 'Entity,Field,Type,Required,ReadOnly,Computed,MaxLength'
  const lines: string[] = [headerLine]

  for (const entity of entities) {
    for (const field of entity.fields ?? []) {
      lines.push(
        [
          escapeCsvCell(entity.name),
          escapeCsvCell(field.name),
          escapeCsvCell(field.type),
          field.isRequired ? 'Yes' : 'No',
          field.isReadOnly ? 'Yes' : 'No',
          field.isComputed ? 'Yes' : 'No',
          String(field.maxLength ?? ''),
        ].join(','),
      )
    }
  }

  return '\uFEFF' + lines.join('\r\n')
}

/**
 * Package exported entity CSVs and an optional schema summary into a ZIP archive.
 * If schemaContent is a non-empty string, it is included as _schema.csv.
 * Returns a Blob of the ZIP file.
 */
export async function packageAsZip(
  moduleName: string,
  schemaContent: string,
  entityResults: EntityExportResult[],
): Promise<Blob> {
  const zip = new JSZip()
  const folder = zip.folder(moduleName) ?? zip

  if (schemaContent) {
    folder.file('_schema.csv', schemaContent)
  }

  for (const result of entityResults) {
    folder.file(`${result.entityType}.csv`, result.csvContent)
  }

  return zip.generateAsync({
    type: 'blob',
    compression: 'DEFLATE',
    compressionOptions: { level: 6 },
  })
}

/**
 * Trigger a browser download for the given blob.
 */
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.style.display = 'none'
  document.body.appendChild(anchor)
  anchor.click()
  document.body.removeChild(anchor)
  URL.revokeObjectURL(url)
}
