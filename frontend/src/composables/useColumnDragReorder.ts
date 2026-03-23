import { ref, readonly, type Ref } from 'vue'

export interface UseColumnDragReorderOptions {
  /** Callback when columns are reordered */
  onReorder: (fromIndex: number, toIndex: number) => void
}

export interface DropIndicator {
  /** Index where the drop indicator should be shown */
  index: number
  /** Which side of the column: 'left' or 'right' */
  side: 'left' | 'right'
}

export interface UseColumnDragReorderReturn {
  /** Whether a drag is currently in progress */
  isDragging: Readonly<Ref<boolean>>
  /** Whether a drag just finished (suppresses click-after-drag) */
  justDragged: Readonly<Ref<boolean>>
  /** The field currently being dragged */
  draggingField: Readonly<Ref<string | null>>
  /** Where the drop indicator should be shown */
  dropIndicator: Readonly<Ref<DropIndicator | null>>
  /** Call on dragstart for a column header */
  handleDragStart: (field: string, index: number, event: DragEvent) => void
  /** Call on dragover for a column header */
  handleDragOver: (index: number, event: DragEvent) => void
  /** Call on dragleave for a column header */
  handleDragLeave: () => void
  /** Call on drop for a column header */
  handleDrop: (index: number, event: DragEvent) => void
  /** Call on dragend (cleanup) */
  handleDragEnd: () => void
}

export function useColumnDragReorder(options: UseColumnDragReorderOptions): UseColumnDragReorderReturn {
  const { onReorder } = options

  const isDragging = ref(false)
  const justDragged = ref(false)
  const draggingField = ref<string | null>(null)
  const dropIndicator = ref<DropIndicator | null>(null)

  let dragFromIndex = -1
  let justDraggedTimer: ReturnType<typeof setTimeout> | null = null

  function handleDragStart(field: string, index: number, event: DragEvent) {
    isDragging.value = true
    draggingField.value = field
    dragFromIndex = index

    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move'
      event.dataTransfer.setData('text/plain', String(index))
    }
  }

  function handleDragOver(index: number, event: DragEvent) {
    event.preventDefault()
    if (!isDragging.value || dragFromIndex === index) {
      dropIndicator.value = null
      return
    }

    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move'
    }

    // Determine which half of the target the cursor is over
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
    const midX = rect.left + rect.width / 2
    const side: 'left' | 'right' = event.clientX < midX ? 'left' : 'right'

    dropIndicator.value = { index, side }
  }

  function handleDragLeave() {
    dropIndicator.value = null
  }

  function handleDrop(index: number, event: DragEvent) {
    event.preventDefault()

    if (dragFromIndex >= 0 && dragFromIndex !== index) {
      const side = dropIndicator.value?.side ?? 'right'
      let targetIndex = index

      if (dragFromIndex < index) {
        // Dragging forward: drop on left means insert before target (shift back by 1)
        targetIndex = side === 'left' ? index - 1 : index
      } else {
        // Dragging backward: drop on right means insert after target (shift forward by 1)
        targetIndex = side === 'right' ? index + 1 : index
      }

      onReorder(dragFromIndex, targetIndex)
    }

    cleanup()
  }

  function handleDragEnd() {
    cleanup()
  }

  function cleanup() {
    const wasDragging = isDragging.value
    isDragging.value = false
    draggingField.value = null
    dropIndicator.value = null
    dragFromIndex = -1

    // Flag to suppress click-after-drag (browser fires click after drop)
    if (wasDragging) {
      justDragged.value = true
      if (justDraggedTimer) clearTimeout(justDraggedTimer)
      justDraggedTimer = setTimeout(() => {
        justDragged.value = false
      }, 100)
    }
  }

  return {
    isDragging: readonly(isDragging),
    justDragged: readonly(justDragged),
    draggingField: readonly(draggingField),
    dropIndicator: readonly(dropIndicator),
    handleDragStart,
    handleDragOver,
    handleDragLeave,
    handleDrop,
    handleDragEnd,
  }
}
