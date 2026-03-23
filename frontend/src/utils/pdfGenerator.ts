import { jsPDF } from 'jspdf'
import autoTable from 'jspdf-autotable'
import type { ReportField, ReportTemplate } from '../services/reportService'

// ---- Types ----

export interface PdfOptions {
  title: string
  header?: string
  footer?: string
  orientation?: 'portrait' | 'landscape'
  fontSize?: number
}

// ---- Helpers ----

/**
 * Format a cell value for display in the report.
 * Handles date/datetime/currency/percent formatting based on field.format.
 */
export function formatValue(value: unknown, format?: string): string {
  if (value === null || value === undefined) {
    return ''
  }
  switch (format) {
    case 'date':
      try {
        return new Date(String(value)).toLocaleDateString()
      } catch {
        return String(value)
      }
    case 'datetime':
      try {
        return new Date(String(value)).toLocaleString()
      } catch {
        return String(value)
      }
    case 'currency':
      try {
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(Number(value))
      } catch {
        return String(value)
      }
    case 'percent':
      try {
        return `${(Number(value) * 100).toFixed(1)}%`
      } catch {
        return String(value)
      }
    default:
      return String(value)
  }
}

/**
 * Apply aggregate functions to a column of values.
 * Returns a map of field name -> formatted aggregate label + value string.
 */
export function computeAggregates(
  fields: ReportField[],
  rows: Record<string, unknown>[]
): Record<string, string> {
  const result: Record<string, string> = {}

  for (const field of fields) {
    if (!field.aggregate) continue

    const values = rows
      .map(r => r[field.name])
      .filter(v => v !== null && v !== undefined && !isNaN(Number(v)))
      .map(v => Number(v))

    if (values.length === 0) {
      result[field.name] = `${field.aggregate.toUpperCase()}: -`
      continue
    }

    let aggregated: number
    switch (field.aggregate) {
      case 'sum':
        aggregated = values.reduce((acc, v) => acc + v, 0)
        break
      case 'count':
        aggregated = values.length
        break
      case 'avg':
        aggregated = values.reduce((acc, v) => acc + v, 0) / values.length
        break
      case 'min':
        aggregated = Math.min(...values)
        break
      case 'max':
        aggregated = Math.max(...values)
        break
      default:
        aggregated = 0
    }

    const formatted = formatValue(aggregated, field.format)
    result[field.name] = `${field.aggregate.toUpperCase()}: ${formatted}`
  }

  return result
}

// ---- PDF page footer helper ----

function addPageFooters(doc: jsPDF, footerText?: string, fontSize = 9): void {
  const pageCount = doc.getNumberOfPages()
  for (let i = 1; i <= pageCount; i++) {
    doc.setPage(i)
    const pageWidth = doc.internal.pageSize.getWidth()
    const pageHeight = doc.internal.pageSize.getHeight()

    doc.setFontSize(fontSize - 1)
    doc.setTextColor(150)

    // Page number -- bottom right
    const pageLabel = `Page ${i} of ${pageCount}`
    doc.text(pageLabel, pageWidth - 40, pageHeight - 16, { align: 'right' })

    // Optional footer text -- bottom left
    if (footerText) {
      doc.text(footerText, 40, pageHeight - 16)
    }

    doc.setTextColor(0)
  }
}

// ---- List layout ----

function renderListLayout(
  doc: jsPDF,
  template: ReportTemplate,
  rows: Record<string, unknown>[],
  startY: number,
  options?: PdfOptions
): void {
  const aggregates = computeAggregates(template.fields, rows)
  const hasAggregates = Object.keys(aggregates).length > 0

  const body = rows.map(row =>
    template.fields.map(f => formatValue(row[f.name], f.format))
  )

  const foot: string[][] = hasAggregates
    ? [template.fields.map(f => aggregates[f.name] ?? '')]
    : []

  autoTable(doc, {
    startY,
    head: [template.fields.map(f => f.label)],
    body,
    foot: foot.length > 0 ? foot : undefined,
    columnStyles: Object.fromEntries(
      template.fields.map((f, i) => [i, { cellWidth: f.width * 0.5 }])
    ),
    styles: { fontSize: options?.fontSize ?? 9, cellPadding: 4 },
    headStyles: { fillColor: [59, 130, 246], textColor: 255, fontStyle: 'bold' },
    footStyles: { fillColor: [226, 232, 240], fontStyle: 'bold' },
    alternateRowStyles: { fillColor: [248, 250, 252] },
  })
}

// ---- Summary layout (grouped) ----

function renderSummaryLayout(
  doc: jsPDF,
  template: ReportTemplate,
  rows: Record<string, unknown>[],
  startY: number,
  options?: PdfOptions
): void {
  const groupByField = template.groupBy

  if (!groupByField) {
    // Fall back to list layout if no groupBy defined
    renderListLayout(doc, template, rows, startY, options)
    return
  }

  // Group rows by the groupBy field value
  const groups = new Map<string, Record<string, unknown>[]>()
  for (const row of rows) {
    const groupKey = formatValue(row[groupByField])
    if (!groups.has(groupKey)) groups.set(groupKey, [])
    groups.get(groupKey)!.push(row)
  }

  let currentY = startY
  const pageHeight = doc.internal.pageSize.getHeight()

  for (const [groupKey, groupRows] of groups) {
    // Group header
    doc.setFontSize((options?.fontSize ?? 9) + 1)
    doc.setFont('helvetica', 'bold')
    doc.text(`${groupByField}: ${groupKey}`, 40, currentY)
    doc.setFont('helvetica', 'normal')
    currentY += 14

    const aggregates = computeAggregates(template.fields, groupRows)
    const hasAggregates = Object.keys(aggregates).length > 0

    const body = groupRows.map(row =>
      template.fields.map(f => formatValue(row[f.name], f.format))
    )

    const foot: string[][] = hasAggregates
      ? [template.fields.map(f => aggregates[f.name] ?? '')]
      : []

    autoTable(doc, {
      startY: currentY,
      head: [template.fields.map(f => f.label)],
      body,
      foot: foot.length > 0 ? foot : undefined,
      columnStyles: Object.fromEntries(
        template.fields.map((f, i) => [i, { cellWidth: f.width * 0.5 }])
      ),
      styles: { fontSize: options?.fontSize ?? 9, cellPadding: 4 },
      headStyles: { fillColor: [99, 102, 241], textColor: 255, fontStyle: 'bold' },
      footStyles: { fillColor: [226, 232, 240], fontStyle: 'bold' },
      alternateRowStyles: { fillColor: [248, 250, 252] },
    })

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    currentY = (doc as any).lastAutoTable?.finalY ?? currentY
    currentY += 16

    // Add new page if close to bottom
    if (currentY > pageHeight - 60) {
      doc.addPage()
      currentY = 40
    }
  }
}

// ---- Detail layout (one card per row) ----

function renderDetailLayout(
  doc: jsPDF,
  template: ReportTemplate,
  rows: Record<string, unknown>[],
  startY: number,
  options?: PdfOptions
): void {
  const pageHeight = doc.internal.pageSize.getHeight()
  const pageWidth = doc.internal.pageSize.getWidth()
  const fontSize = options?.fontSize ?? 9
  const labelX = 40
  const valueX = pageWidth / 2
  const rowHeight = fontSize + 6
  let currentY = startY

  for (let ri = 0; ri < rows.length; ri++) {
    const row = rows[ri]

    const cardHeight = template.fields.length * rowHeight + 16
    if (currentY + cardHeight > pageHeight - 40) {
      doc.addPage()
      currentY = 40
    }

    // Card background
    doc.setDrawColor(203, 213, 225)
    doc.setFillColor(248, 250, 252)
    doc.roundedRect(labelX - 6, currentY - 8, pageWidth - 68, cardHeight, 3, 3, 'FD')

    // Record number
    doc.setFontSize(fontSize)
    doc.setFont('helvetica', 'bold')
    doc.setTextColor(59, 130, 246)
    doc.text(`Record ${ri + 1}`, labelX, currentY)
    doc.setTextColor(0)
    currentY += rowHeight

    for (const field of template.fields) {
      const displayVal = formatValue(row[field.name], field.format)

      // Label (left column)
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(fontSize - 1)
      doc.setTextColor(100)
      doc.text(`${field.label}:`, labelX, currentY)

      // Value (right column)
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(fontSize)
      doc.setTextColor(0)
      doc.text(displayVal, valueX, currentY)

      currentY += rowHeight
    }

    currentY += 12
  }
}

// ---- Public API ----

/**
 * Generate a PDF from report data and download it.
 * Uses jsPDF + autotable for list layout.
 * For summary layout, groups rows by groupBy field and adds subtotals.
 */
export async function generatePdf(
  template: ReportTemplate,
  rows: Record<string, unknown>[],
  options?: PdfOptions
): Promise<void> {
  const doc = new jsPDF({
    orientation: options?.orientation ?? 'landscape',
    unit: 'pt',
    format: 'a4',
  })

  const title = options?.title ?? template.name
  const headerText = options?.header ?? template.header
  const footerText = options?.footer ?? template.footer

  // Title
  doc.setFontSize(16)
  doc.setFont('helvetica', 'bold')
  doc.text(title, 40, 40)

  // Sub-header
  let headerY = 58
  if (headerText) {
    doc.setFontSize(9)
    doc.setFont('helvetica', 'normal')
    doc.setTextColor(100)
    doc.text(headerText, 40, headerY)
    doc.setTextColor(0)
    headerY += 14
  }

  const tableStartY = headerY + 8

  switch (template.layoutType) {
    case 'list':
      renderListLayout(doc, template, rows, tableStartY, options)
      break
    case 'summary':
      renderSummaryLayout(doc, template, rows, tableStartY, options)
      break
    case 'detail':
      renderDetailLayout(doc, template, rows, tableStartY, options)
      break
    default:
      renderListLayout(doc, template, rows, tableStartY, options)
  }

  // Add page footers with page numbers (and optional footer text)
  addPageFooters(doc, footerText, options?.fontSize)

  doc.save(`${template.name}.pdf`)
}

/**
 * Open the browser print dialog with print-optimized rendering.
 * Injects a print stylesheet and triggers window.print().
 */
export function printReport(
  _template: ReportTemplate,
  _rows: Record<string, unknown>[],
  containerId: string
): void {
  // Remove any previously injected print style
  const existingStyle = document.getElementById('__report_print_style__')
  if (existingStyle) existingStyle.remove()

  const style = document.createElement('style')
  style.id = '__report_print_style__'
  style.setAttribute('media', 'print')
  style.textContent = `
    /* Hide everything except the report container */
    body > * {
      display: none !important;
    }
    #${containerId} {
      display: block !important;
    }
    /* Reset margins for print */
    @page {
      margin: 1.5cm;
    }
    /* Hide interactive controls within the report */
    #${containerId} button,
    #${containerId} [role="toolbar"],
    #${containerId} .no-print {
      display: none !important;
    }
    /* Ensure tables break across pages cleanly */
    table {
      border-collapse: collapse;
      width: 100%;
    }
    thead {
      display: table-header-group;
    }
    tr {
      page-break-inside: avoid;
    }
  `
  document.head.appendChild(style)
  window.print()
}
