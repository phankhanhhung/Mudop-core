import { ref, readonly, type Ref } from 'vue'

export interface UseColumnResizeOptions {
  /** Callback to persist the new column width */
  onResize: (field: string, width: number) => void
  /** Minimum column width in pixels */
  minWidth?: number
}

export interface UseColumnResizeReturn {
  /** Whether a resize is currently in progress */
  isResizing: Readonly<Ref<boolean>>
  /** The field being resized (null if not resizing) */
  resizingField: Readonly<Ref<string | null>>
  /** Start a resize operation - call from mousedown on the resize handle */
  startResize: (field: string, startWidth: number, event: MouseEvent) => void
  /** Auto-fit column width by measuring content - call from dblclick on resize handle */
  autoFitColumn: (field: string, tableEl: HTMLElement) => void
}

export function useColumnResize(options: UseColumnResizeOptions): UseColumnResizeReturn {
  const { onResize, minWidth = 60 } = options
  const isResizing = ref(false)
  const resizingField = ref<string | null>(null)

  let startX = 0
  let startWidth = 0
  let currentField = ''

  function onMouseMove(e: MouseEvent) {
    const delta = e.clientX - startX
    const newWidth = Math.max(startWidth + delta, minWidth)
    onResize(currentField, newWidth)
  }

  function onMouseUp() {
    document.removeEventListener('mousemove', onMouseMove)
    document.removeEventListener('mouseup', onMouseUp)
    document.body.style.cursor = ''
    document.body.style.userSelect = ''
    isResizing.value = false
    resizingField.value = null
  }

  function startResize(field: string, width: number, event: MouseEvent) {
    event.preventDefault()
    event.stopPropagation()
    currentField = field
    startX = event.clientX
    startWidth = width
    isResizing.value = true
    resizingField.value = field

    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'

    document.addEventListener('mousemove', onMouseMove)
    document.addEventListener('mouseup', onMouseUp)
  }

  function autoFitColumn(field: string, tableEl: HTMLElement) {
    // Measure max content width for this column
    const cells = tableEl.querySelectorAll(`[data-field="${field}"]`)
    let maxWidth = minWidth
    cells.forEach((cell) => {
      const contentWidth = (cell as HTMLElement).scrollWidth + 16 // padding
      if (contentWidth > maxWidth) maxWidth = contentWidth
    })
    // Also measure header
    const header = tableEl.querySelector(`[data-header-field="${field}"]`)
    if (header) {
      const headerWidth = (header as HTMLElement).scrollWidth + 32 // padding + sort/filter icons
      if (headerWidth > maxWidth) maxWidth = headerWidth
    }
    onResize(field, Math.min(maxWidth, 500)) // cap at 500px
  }

  return {
    isResizing: readonly(isResizing),
    resizingField: readonly(resizingField),
    startResize,
    autoFitColumn,
  }
}
