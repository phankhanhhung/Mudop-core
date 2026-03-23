<script setup lang="ts">
import { ref, computed } from 'vue'
import { userPreferenceService, type UserPreference } from '@/services/userPreferenceService'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Bookmark, Loader2, Star, Pencil, Check, X, Trash2, Filter, ArrowUpDown } from 'lucide-vue-next'

const savedViews = ref<UserPreference[]>([])
const savedViewsLoading = ref(false)
const savedViewsLoaded = ref(false)
const renamingViewId = ref<string | null>(null)
const renameValue = ref('')
const deletingViewId = ref<string | null>(null)

interface SavedViewGroup {
  entityKey: string
  entityName: string
  moduleName: string
  views: UserPreference[]
}

const groupedSavedViews = computed<SavedViewGroup[]>(() => {
  const groups = new Map<string, UserPreference[]>()
  for (const v of savedViews.value) {
    const list = groups.get(v.entityKey) || []
    list.push(v)
    groups.set(v.entityKey, list)
  }
  return Array.from(groups.entries()).map(([key, views]) => {
    const parts = key.split('.')
    const entityName = parts[parts.length - 1]
    const moduleName = parts.length > 1 ? parts.slice(0, -1).join('.') : ''
    return { entityKey: key, entityName, moduleName, views }
  })
})

async function loadSavedViews() {
  if (savedViewsLoaded.value) return
  savedViewsLoading.value = true
  try {
    savedViews.value = await userPreferenceService.listByCategory('entity_list_view')
    savedViewsLoaded.value = true
  } catch (err) {
    console.error('Failed to load saved views', err)
  } finally {
    savedViewsLoading.value = false
  }
}

async function toggleDefault(view: UserPreference) {
  try {
    if (view.isDefault) {
      await userPreferenceService.update(view.id, { isDefault: false })
      view.isDefault = false
    } else {
      await userPreferenceService.setDefault(view.id)
      for (const v of savedViews.value) {
        if (v.entityKey === view.entityKey) {
          v.isDefault = v.id === view.id
        }
      }
    }
  } catch (err) {
    console.error('Failed to toggle default', err)
  }
}

function startRename(view: UserPreference) {
  renamingViewId.value = view.id
  renameValue.value = view.name
}

function cancelRename() {
  renamingViewId.value = null
  renameValue.value = ''
}

async function saveRename(view: UserPreference) {
  const newName = renameValue.value.trim()
  if (!newName || newName === view.name) {
    cancelRename()
    return
  }
  try {
    await userPreferenceService.update(view.id, { name: newName })
    view.name = newName
    cancelRename()
  } catch (err) {
    console.error('Failed to rename view', err)
  }
}

async function deleteView(view: UserPreference) {
  deletingViewId.value = null
  try {
    await userPreferenceService.remove(view.id)
    savedViews.value = savedViews.value.filter(v => v.id !== view.id)
  } catch (err) {
    console.error('Failed to delete view', err)
  }
}

function getFilterCount(view: UserPreference): number {
  const settings = view.settings as Record<string, unknown>
  const filters = settings?.filters
  if (Array.isArray(filters)) return filters.length
  return 0
}

function hasSorting(view: UserPreference): boolean {
  const settings = view.settings as Record<string, unknown>
  return !!(settings?.sortField || settings?.sortBy)
}

// Expose loadSavedViews so parent can trigger it when switching to this section
defineExpose({ loadSavedViews })
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center gap-2">
        <Bookmark class="h-5 w-5 text-primary" />
        <div>
          <CardTitle>{{ $t('settings.savedViews.title') }}</CardTitle>
          <CardDescription>{{ $t('settings.savedViews.subtitle') }}</CardDescription>
        </div>
      </div>
    </CardHeader>
    <CardContent>
      <!-- Loading -->
      <div v-if="savedViewsLoading" class="flex items-center justify-center py-12">
        <Loader2 class="h-6 w-6 animate-spin text-muted-foreground" />
      </div>

      <!-- Empty state -->
      <div v-else-if="groupedSavedViews.length === 0" class="flex flex-col items-center justify-center py-12">
        <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
          <Bookmark class="h-7 w-7 text-muted-foreground" />
        </div>
        <h3 class="font-semibold mb-1">{{ $t('settings.savedViews.empty') }}</h3>
        <p class="text-muted-foreground text-sm text-center max-w-sm">
          {{ $t('settings.savedViews.emptyDescription') }}
        </p>
      </div>

      <!-- Grouped views -->
      <div v-else class="space-y-6">
        <div v-for="group in groupedSavedViews" :key="group.entityKey">
          <!-- Entity group header -->
          <div class="flex items-center gap-2 mb-3">
            <h3 class="text-sm font-semibold">{{ group.entityName }}</h3>
            <Badge v-if="group.moduleName" variant="secondary" class="text-xs">
              {{ group.moduleName }}
            </Badge>
            <span class="text-xs text-muted-foreground">({{ group.views.length }})</span>
          </div>

          <!-- Views table -->
          <div class="rounded-lg border overflow-hidden">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b bg-muted/50">
                  <th class="text-left font-medium text-muted-foreground px-4 py-2">{{ $t('settings.savedViews.name') }}</th>
                  <th class="text-center font-medium text-muted-foreground px-4 py-2 w-20">{{ $t('settings.savedViews.default') }}</th>
                  <th class="text-center font-medium text-muted-foreground px-4 py-2 w-24">{{ $t('settings.savedViews.filters') }}</th>
                  <th class="text-center font-medium text-muted-foreground px-4 py-2 w-20">{{ $t('settings.savedViews.sort') }}</th>
                  <th class="text-right font-medium text-muted-foreground px-4 py-2 w-28">{{ $t('settings.savedViews.actions') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="view in group.views" :key="view.id" class="border-b last:border-b-0 hover:bg-muted/30">
                  <!-- Name (with inline rename) -->
                  <td class="px-4 py-2.5">
                    <div v-if="renamingViewId === view.id" class="flex items-center gap-2">
                      <Input
                        v-model="renameValue"
                        class="h-7 text-sm"
                        @keyup.enter="saveRename(view)"
                        @keyup.escape="cancelRename()"
                      />
                      <button
                        class="text-primary hover:text-primary/80"
                        @click="saveRename(view)"
                      >
                        <Check class="h-4 w-4" />
                      </button>
                      <button
                        class="text-muted-foreground hover:text-foreground"
                        @click="cancelRename()"
                      >
                        <X class="h-4 w-4" />
                      </button>
                    </div>
                    <span v-else class="font-medium">{{ view.name }}</span>
                  </td>

                  <!-- Default star -->
                  <td class="px-4 py-2.5 text-center">
                    <button
                      class="inline-flex items-center justify-center"
                      :title="view.isDefault ? $t('settings.savedViews.clearDefault') : $t('settings.savedViews.setDefault')"
                      @click="toggleDefault(view)"
                    >
                      <Star
                        class="h-4 w-4 transition-colors"
                        :class="view.isDefault ? 'text-amber-500 fill-amber-500' : 'text-muted-foreground hover:text-amber-400'"
                      />
                    </button>
                  </td>

                  <!-- Filters count -->
                  <td class="px-4 py-2.5 text-center">
                    <span v-if="getFilterCount(view) > 0" class="inline-flex items-center gap-1 text-xs text-muted-foreground">
                      <Filter class="h-3 w-3" />
                      {{ getFilterCount(view) }}
                    </span>
                    <span v-else class="text-xs text-muted-foreground">&mdash;</span>
                  </td>

                  <!-- Sort -->
                  <td class="px-4 py-2.5 text-center">
                    <ArrowUpDown v-if="hasSorting(view)" class="h-3.5 w-3.5 text-muted-foreground mx-auto" />
                    <span v-else class="text-xs text-muted-foreground">&mdash;</span>
                  </td>

                  <!-- Actions -->
                  <td class="px-4 py-2.5 text-right">
                    <div class="flex items-center justify-end gap-1">
                      <button
                        class="p-1 rounded hover:bg-muted text-muted-foreground hover:text-foreground"
                        :title="$t('settings.savedViews.rename')"
                        @click="startRename(view)"
                      >
                        <Pencil class="h-3.5 w-3.5" />
                      </button>

                      <!-- Delete with confirm -->
                      <template v-if="deletingViewId === view.id">
                        <span class="text-xs text-red-600 mr-1">{{ $t('settings.savedViews.confirmDelete') }}</span>
                        <button
                          class="p-1 rounded hover:bg-red-100 dark:hover:bg-red-900/30 text-red-600"
                          @click="deleteView(view)"
                        >
                          <Check class="h-3.5 w-3.5" />
                        </button>
                        <button
                          class="p-1 rounded hover:bg-muted text-muted-foreground"
                          @click="deletingViewId = null"
                        >
                          <X class="h-3.5 w-3.5" />
                        </button>
                      </template>
                      <button
                        v-else
                        class="p-1 rounded hover:bg-red-100 dark:hover:bg-red-900/30 text-muted-foreground hover:text-red-600"
                        :title="$t('settings.savedViews.delete')"
                        @click="deletingViewId = view.id"
                      >
                        <Trash2 class="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </CardContent>
  </Card>
</template>
