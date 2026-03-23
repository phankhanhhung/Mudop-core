<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { PopoverRoot, PopoverTrigger, PopoverContent, PopoverPortal } from 'radix-vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Bookmark,
  BookmarkCheck,
  Star,
  StarOff,
  Pencil,
  Trash2,
  Save,
  ChevronDown,
  Check
} from 'lucide-vue-next'
import type { SavedView } from '@/composables/useSavedViews'

interface Props {
  views: SavedView[]
  currentViewId: string | null
  defaultViewId: string | null
}

const props = defineProps<Props>()

const emit = defineEmits<{
  select: [id: string | null]
  save: [name: string]
  update: [id: string]
  delete: [id: string]
  rename: [id: string, name: string]
  'set-default': [id: string | null]
}>()

const newViewName = ref('')
const renamingId = ref<string | null>(null)
const renameValue = ref('')
const renameInputRef = ref<HTMLInputElement | null>(null)
const popoverOpen = ref(false)

const currentViewName = computed(() => {
  if (props.currentViewId == null) return 'All items'
  const view = props.views.find((v) => v.id === props.currentViewId)
  return view?.name ?? 'All items'
})

const hasViews = computed(() => props.views.length > 0)

function handleSelect(id: string | null): void {
  emit('select', id)
  popoverOpen.value = false
}

function handleSave(): void {
  const name = newViewName.value.trim()
  if (!name) return
  emit('save', name)
  newViewName.value = ''
}

function handleUpdate(id: string): void {
  emit('update', id)
}

function handleDelete(id: string): void {
  emit('delete', id)
}

function handleToggleDefault(id: string): void {
  if (props.defaultViewId === id) {
    emit('set-default', null)
  } else {
    emit('set-default', id)
  }
}

function startRename(view: SavedView): void {
  renamingId.value = view.id
  renameValue.value = view.name
  nextTick(() => {
    renameInputRef.value?.focus()
    renameInputRef.value?.select()
  })
}

function confirmRename(): void {
  if (renamingId.value == null) return
  const name = renameValue.value.trim()
  if (name) {
    emit('rename', renamingId.value, name)
  }
  renamingId.value = null
  renameValue.value = ''
}

function cancelRename(): void {
  renamingId.value = null
  renameValue.value = ''
}

function handleRenameKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter') {
    event.preventDefault()
    confirmRename()
  } else if (event.key === 'Escape') {
    event.preventDefault()
    cancelRename()
  }
}
</script>

<template>
  <PopoverRoot v-model:open="popoverOpen">
    <PopoverTrigger as-child>
      <Button variant="outline" size="sm" class="gap-1.5">
        <component
          :is="currentViewId != null ? BookmarkCheck : Bookmark"
          class="h-4 w-4"
        />
        <span class="max-w-[140px] truncate">{{ currentViewName }}</span>
        <ChevronDown class="h-3.5 w-3.5 opacity-50" />
      </Button>
    </PopoverTrigger>
    <PopoverPortal>
      <PopoverContent
        :side-offset="4"
        align="start"
        class="z-50 w-72 rounded-md border bg-background p-0 shadow-md outline-none data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
      >
        <!-- "All items" option -->
        <div class="p-1">
          <button
            class="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer"
            @click="handleSelect(null)"
          >
            <Check
              class="h-4 w-4"
              :class="currentViewId == null ? 'opacity-100' : 'opacity-0'"
            />
            <span>All items</span>
          </button>
        </div>

        <!-- View list -->
        <template v-if="hasViews">
          <div class="border-t" />

          <div class="max-h-48 overflow-y-auto p-1">
            <div
              v-for="view in views"
              :key="view.id"
              class="group flex w-full items-center gap-1 rounded-sm px-1 py-0.5"
            >
              <!-- Rename mode -->
              <template v-if="renamingId === view.id">
                <div class="flex flex-1 items-center gap-1 px-1 py-0.5">
                  <input
                    ref="renameInputRef"
                    v-model="renameValue"
                    class="h-7 flex-1 rounded border bg-background px-2 text-sm outline-none focus:ring-1 focus:ring-ring"
                    @keydown="handleRenameKeydown"
                    @blur="confirmRename"
                  />
                </div>
              </template>

              <!-- Normal mode -->
              <template v-else>
                <!-- Select button (view name) -->
                <button
                  class="flex flex-1 items-center gap-2 rounded-sm px-1 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground cursor-pointer truncate"
                  @click="handleSelect(view.id)"
                >
                  <Check
                    class="h-4 w-4 shrink-0"
                    :class="currentViewId === view.id ? 'opacity-100' : 'opacity-0'"
                  />
                  <span class="truncate">{{ view.name }}</span>
                </button>

                <!-- Default star toggle -->
                <button
                  class="shrink-0 rounded-sm p-1 hover:bg-accent hover:text-accent-foreground cursor-pointer"
                  :title="defaultViewId === view.id ? 'Remove as default' : 'Set as default'"
                  :aria-label="defaultViewId === view.id ? 'Remove as default' : 'Set as default'"
                  @click.stop="handleToggleDefault(view.id)"
                >
                  <component
                    :is="defaultViewId === view.id ? Star : StarOff"
                    class="h-3.5 w-3.5"
                    :class="defaultViewId === view.id ? 'text-yellow-500' : 'opacity-40'"
                    aria-hidden="true"
                  />
                </button>

                <!-- Action buttons (visible on hover) -->
                <div class="flex shrink-0 items-center opacity-0 group-hover:opacity-100 transition-opacity">
                  <button
                    class="rounded-sm p-1 hover:bg-accent hover:text-accent-foreground cursor-pointer"
                    title="Update with current state"
                    aria-label="Update view"
                    @click.stop="handleUpdate(view.id)"
                  >
                    <Save class="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                  <button
                    class="rounded-sm p-1 hover:bg-accent hover:text-accent-foreground cursor-pointer"
                    title="Rename"
                    aria-label="Rename view"
                    @click.stop="startRename(view)"
                  >
                    <Pencil class="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                  <button
                    class="rounded-sm p-1 hover:bg-accent text-destructive cursor-pointer"
                    title="Delete"
                    aria-label="Delete view"
                    @click.stop="handleDelete(view.id)"
                  >
                    <Trash2 class="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                </div>
              </template>
            </div>
          </div>
        </template>

        <!-- Save new view -->
        <div class="border-t p-2">
          <div class="flex items-center gap-1.5">
            <Input
              v-model="newViewName"
              placeholder="Save current view..."
              class="h-8 text-sm"
              @keydown.enter="handleSave"
            />
            <Button
              variant="ghost"
              size="sm"
              class="h-8 shrink-0 px-2"
              :disabled="!newViewName.trim()"
              aria-label="Save view"
              @click="handleSave"
            >
              <Save class="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        </div>
      </PopoverContent>
    </PopoverPortal>
  </PopoverRoot>
</template>
