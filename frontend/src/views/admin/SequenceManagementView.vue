<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useTenantStore } from '@/stores/tenant'
import { useMetadataStore } from '@/stores/metadata'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { sequenceService } from '@/services/sequenceService'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Spinner } from '@/components/ui/spinner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell
} from '@/components/ui/table'
import { ConfirmDialog } from '@/components/common'
import {
  Hash,
  SkipForward,
  RotateCcw,
  Search,
  AlertCircle,
  RefreshCw,
  Globe,
  Package,
  X,
  Eye
} from 'lucide-vue-next'
import type { SequenceDefinition, SequenceValue } from '@/types/sequence'

const { t } = useI18n()
const tenantStore = useTenantStore()
const metadataStore = useMetadataStore()
const confirmDialog = useConfirmDialog()

// State
const sequences = ref<SequenceDefinition[]>([])
const isLoading = ref(false)
const loadError = ref<string | null>(null)
const selectedModuleName = ref<string | number>('')
const searchFilter = ref('')

// Track current values and next values per sequence
const currentValues = ref<Map<string, SequenceValue>>(new Map())
const loadingCurrentValues = ref<Set<string>>(new Set())
const nextValueResults = ref<Map<string, SequenceValue>>(new Map())
const loadingNextValues = ref<Set<string>>(new Set())
const actionErrors = ref<Map<string, string>>(new Map())
const resettingSequences = ref<Set<string>>(new Set())

// Detail panel
const selectedSequence = ref<SequenceDefinition | null>(null)

const tenantId = computed(() => tenantStore.currentTenant?.id ?? '')

// Stats
const stats = computed(() => {
  const total = sequences.value.length
  const globalScope = sequences.value.filter(s => s.scope === 'Global').length
  const withReset = sequences.value.filter(s => s.resetOn !== 'Never').length
  return { total, globalScope, withReset }
})

const filteredSequences = computed(() => {
  if (!searchFilter.value) return sequences.value
  const filter = searchFilter.value.toLowerCase()
  return sequences.value.filter(
    (s) =>
      s.name.toLowerCase().includes(filter) ||
      s.forEntity.toLowerCase().includes(filter) ||
      s.forField.toLowerCase().includes(filter)
  )
})

// Reset sequences when module changes
watch(selectedModuleName, () => {
  sequences.value = []
  currentValues.value = new Map()
  nextValueResults.value = new Map()
  actionErrors.value = new Map()
  selectedSequence.value = null
  if (selectedModuleName.value) {
    loadSequences()
  }
})

onMounted(async () => {
  if (!metadataStore.hasModules) {
    await metadataStore.fetchModules()
  }
})

async function loadSequences() {
  if (!tenantId.value || !selectedModuleName.value) return

  isLoading.value = true
  loadError.value = null
  try {
    sequences.value = await sequenceService.listSequences(
      tenantId.value,
      String(selectedModuleName.value)
    )
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : t('admin.sequences.failedToLoad')
  } finally {
    isLoading.value = false
  }
}

async function loadCurrentValue(seq: SequenceDefinition) {
  if (!tenantId.value || !selectedModuleName.value) return

  loadingCurrentValues.value.add(seq.name)
  actionErrors.value.delete(seq.name)
  try {
    const val = await sequenceService.getCurrentValue(
      tenantId.value,
      String(selectedModuleName.value),
      seq.name
    )
    currentValues.value.set(seq.name, val)
  } catch (e) {
    actionErrors.value.set(
      seq.name,
      e instanceof Error ? e.message : t('admin.sequences.failedCurrentValue')
    )
  } finally {
    loadingCurrentValues.value.delete(seq.name)
    // Force reactivity
    loadingCurrentValues.value = new Set(loadingCurrentValues.value)
  }
}

async function getNextValue(seq: SequenceDefinition) {
  if (!tenantId.value || !selectedModuleName.value) return

  loadingNextValues.value.add(seq.name)
  actionErrors.value.delete(seq.name)
  try {
    const val = await sequenceService.getNextValue(
      tenantId.value,
      String(selectedModuleName.value),
      seq.name
    )
    nextValueResults.value.set(seq.name, val)
    // Also update current value display
    currentValues.value.set(seq.name, val)
  } catch (e) {
    actionErrors.value.set(
      seq.name,
      e instanceof Error ? e.message : t('admin.sequences.failedNextValue')
    )
  } finally {
    loadingNextValues.value.delete(seq.name)
    loadingNextValues.value = new Set(loadingNextValues.value)
  }
}

async function resetSequence(seq: SequenceDefinition) {
  const confirmed = await confirmDialog.confirm({
    title: t('admin.sequences.resetSequence'),
    description: t('admin.sequences.resetConfirm', { name: seq.name, value: seq.startValue }),
    confirmLabel: t('common.reset'),
    variant: 'destructive'
  })

  if (!confirmed) return
  if (!tenantId.value || !selectedModuleName.value) return

  resettingSequences.value.add(seq.name)
  actionErrors.value.delete(seq.name)
  try {
    await sequenceService.resetSequence(
      tenantId.value,
      String(selectedModuleName.value),
      seq.name
    )
    // Clear cached values after reset
    currentValues.value.delete(seq.name)
    nextValueResults.value.delete(seq.name)
  } catch (e) {
    actionErrors.value.set(
      seq.name,
      e instanceof Error ? e.message : t('admin.sequences.failedReset')
    )
  } finally {
    resettingSequences.value.delete(seq.name)
    resettingSequences.value = new Set(resettingSequences.value)
  }
}

function viewSequence(seq: SequenceDefinition) {
  selectedSequence.value = selectedSequence.value?.name === seq.name ? null : seq
}

function scopeBadgeClasses(scope: string): string {
  switch (scope) {
    case 'Global':
      return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400 border-emerald-200 dark:border-emerald-800'
    case 'Company':
      return 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400 border-blue-200 dark:border-blue-800'
    default:
      return 'bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-400 border-violet-200 dark:border-violet-800'
  }
}

function resetBadgeClasses(resetOn: string): string {
  switch (resetOn) {
    case 'Yearly':
      return 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400 border-amber-200 dark:border-amber-800'
    case 'Monthly':
      return 'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-400 border-orange-200 dark:border-orange-800'
    default:
      return 'bg-gray-100 text-gray-500 dark:bg-gray-800/50 dark:text-gray-400 border-gray-200 dark:border-gray-700'
  }
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('admin.sequences.title') }}</h1>
          <p class="text-muted-foreground mt-1">
            {{ $t('admin.sequences.subtitle') }}
          </p>
        </div>
        <Button variant="outline" size="sm" @click="loadSequences" :disabled="isLoading || !selectedModuleName">
          <Spinner v-if="isLoading" size="sm" class="mr-2" />
          <RefreshCw v-else class="mr-2 h-4 w-4" />
          {{ $t('common.refresh') }}
        </Button>
      </div>

      <!-- No tenant selected -->
      <Alert v-if="!tenantId" variant="default">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>
          {{ $t('admin.sequences.selectTenantFirst') }}
        </AlertDescription>
      </Alert>

      <!-- Error -->
      <Alert v-if="loadError" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ loadError }}</AlertDescription>
      </Alert>

      <template v-if="tenantId">
        <!-- Module Selector -->
        <Card>
          <CardContent class="p-4">
            <div class="flex items-center gap-4">
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
                <Package class="h-5 w-5 text-primary" />
              </div>
              <div class="flex-1 min-w-0">
                <Label class="text-sm font-medium">{{ $t('admin.sequences.selectModule') }}</Label>
                <p class="text-xs text-muted-foreground">{{ $t('admin.sequences.selectModuleSubtitle') }}</p>
              </div>
              <div class="w-64 shrink-0">
                <Select
                  v-model="selectedModuleName"
                  placeholder="Select a module..."
                >
                  <option v-for="mod in metadataStore.modules" :key="mod.name" :value="mod.name">
                    {{ mod.name }} (v{{ mod.version }})
                  </option>
                </Select>
              </div>
            </div>
          </CardContent>
        </Card>

        <!-- Stats Cards (visible when sequences are loaded) -->
        <div v-if="selectedModuleName && sequences.length > 0" class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.sequences.stats.total') }}</p>
                  <p class="text-2xl font-bold mt-1">{{ stats.total }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                  <Hash class="h-5 w-5 text-primary" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.sequences.stats.globalScope') }}</p>
                  <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.globalScope }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                  <Globe class="h-5 w-5 text-emerald-500" />
                </div>
              </div>
            </CardContent>
          </Card>

          <Card class="transition-all hover:shadow-md">
            <CardContent class="p-4">
              <div class="flex items-center justify-between">
                <div>
                  <p class="text-sm font-medium text-muted-foreground">{{ $t('admin.sequences.stats.withReset') }}</p>
                  <p class="text-2xl font-bold mt-1 text-amber-600">{{ stats.withReset }}</p>
                </div>
                <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                  <RotateCcw class="h-5 w-5 text-amber-500" />
                </div>
              </div>
            </CardContent>
          </Card>
        </div>

        <!-- Search Bar (visible when sequences are loaded) -->
        <div v-if="selectedModuleName && sequences.length > 0" class="flex items-center gap-3">
          <div class="relative flex-1 max-w-sm">
            <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              v-model="searchFilter"
              :placeholder="$t('admin.sequences.filterPlaceholder')"
              class="pl-9"
            />
          </div>
          <p class="text-sm text-muted-foreground">
            {{ $t('admin.sequences.showingCount', { count: filteredSequences.length, total: sequences.length }) }}
          </p>
        </div>

        <!-- Loading -->
        <div v-if="isLoading" class="flex flex-col items-center justify-center py-16">
          <Spinner size="lg" />
          <p class="text-muted-foreground mt-3 text-sm">{{ $t('admin.sequences.loading') }}</p>
        </div>

        <!-- No module selected -->
        <Card
          v-else-if="!selectedModuleName"
          class="border-dashed"
        >
          <CardContent class="flex flex-col items-center justify-center py-16">
            <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
              <Hash class="h-8 w-8 text-muted-foreground" />
            </div>
            <h3 class="text-lg font-semibold mb-1">{{ $t('admin.sequences.selectModuleHint') }}</h3>
            <p class="text-muted-foreground text-sm text-center max-w-sm">
              {{ $t('admin.sequences.selectModuleDescription') }}
            </p>
          </CardContent>
        </Card>

        <!-- Empty state: no sequences -->
        <Card
          v-else-if="sequences.length === 0 && !isLoading"
          class="border-dashed"
        >
          <CardContent class="flex flex-col items-center justify-center py-16">
            <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
              <Hash class="h-8 w-8 text-muted-foreground" />
            </div>
            <h3 class="text-lg font-semibold mb-1">{{ $t('admin.sequences.noSequences') }}</h3>
            <p class="text-muted-foreground text-sm text-center max-w-sm">
              {{ $t('admin.sequences.noSequencesDescription') }}
            </p>
          </CardContent>
        </Card>

        <!-- No search results -->
        <Card
          v-else-if="filteredSequences.length === 0 && searchFilter"
          class="border-dashed"
        >
          <CardContent class="flex flex-col items-center justify-center py-12">
            <Search class="h-10 w-10 text-muted-foreground mb-3" />
            <h3 class="text-lg font-semibold mb-1">{{ $t('admin.sequences.noResults') }}</h3>
            <p class="text-muted-foreground text-sm">
              {{ $t('admin.sequences.noMatch', { filter: searchFilter }) }}
            </p>
            <Button variant="outline" class="mt-3" @click="searchFilter = ''">
              {{ $t('admin.sequences.clearFilter') }}
            </Button>
          </CardContent>
        </Card>

        <!-- Sequences Table + Detail Panel -->
        <div v-else-if="filteredSequences.length > 0" class="flex gap-6">
          <!-- Table Card -->
          <Card class="flex-1 min-w-0">
            <CardContent class="p-0">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ $t('admin.sequences.nameCol') }}</TableHead>
                    <TableHead>{{ $t('admin.sequences.entityCol') }}</TableHead>
                    <TableHead>{{ $t('admin.sequences.fieldCol') }}</TableHead>
                    <TableHead>{{ $t('admin.sequences.patternCol') }}</TableHead>
                    <TableHead>{{ $t('admin.sequences.scopeCol') }}</TableHead>
                    <TableHead>{{ $t('admin.sequences.resetPolicyCol') }}</TableHead>
                    <TableHead>{{ $t('admin.sequences.currentValueCol') }}</TableHead>
                    <TableHead class="w-[180px]">{{ $t('admin.sequences.actionsCol') }}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow
                    v-for="seq in filteredSequences"
                    :key="seq.name"
                    class="hover:bg-muted/50 transition-colors cursor-pointer"
                    :class="selectedSequence?.name === seq.name ? 'bg-muted/50' : ''"
                    @click="viewSequence(seq)"
                  >
                    <TableCell class="font-medium">{{ seq.name }}</TableCell>
                    <TableCell class="text-muted-foreground">{{ seq.forEntity }}</TableCell>
                    <TableCell class="text-muted-foreground">{{ seq.forField }}</TableCell>
                    <TableCell>
                      <code class="text-xs bg-muted px-1.5 py-0.5 rounded font-mono">{{ seq.pattern }}</code>
                    </TableCell>
                    <TableCell>
                      <Badge variant="outline" class="text-[11px] font-medium" :class="scopeBadgeClasses(seq.scope)">
                        {{ seq.scope }}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Badge variant="outline" class="text-[11px] font-medium" :class="resetBadgeClasses(seq.resetOn)">
                        {{ seq.resetOn }}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div v-if="loadingCurrentValues.has(seq.name)" class="flex items-center gap-2">
                        <Spinner size="sm" />
                      </div>
                      <div v-else-if="currentValues.has(seq.name)" class="flex items-center gap-1.5">
                        <code class="text-xs bg-muted px-1.5 py-0.5 rounded font-mono">
                          {{ currentValues.get(seq.name)!.formattedValue }}
                        </code>
                        <span class="text-xs text-muted-foreground">
                          (#{{ currentValues.get(seq.name)!.currentValue }})
                        </span>
                      </div>
                      <Button v-else size="sm" variant="ghost" class="h-7 text-xs" @click.stop="loadCurrentValue(seq)">
                        {{ $t('common.load') }}
                      </Button>
                      <p v-if="actionErrors.has(seq.name)" class="text-xs text-destructive mt-1">
                        {{ actionErrors.get(seq.name) }}
                      </p>
                    </TableCell>
                    <TableCell>
                      <div class="flex items-center gap-1" @click.stop>
                        <Button
                          size="sm"
                          variant="ghost"
                          class="h-8 w-8 p-0"
                          :title="$t('admin.sequences.viewDetails')"
                          @click="viewSequence(seq)"
                        >
                          <Eye class="h-4 w-4" />
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          class="h-8 w-8 p-0"
                          :disabled="loadingNextValues.has(seq.name)"
                          :title="$t('admin.sequences.getNext')"
                          @click="getNextValue(seq)"
                        >
                          <Spinner v-if="loadingNextValues.has(seq.name)" size="sm" />
                          <SkipForward v-else class="h-4 w-4" />
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          class="h-8 w-8 p-0 text-destructive hover:text-destructive"
                          :disabled="resettingSequences.has(seq.name)"
                          :title="$t('admin.sequences.resetSequence')"
                          @click="resetSequence(seq)"
                        >
                          <Spinner v-if="resettingSequences.has(seq.name)" size="sm" />
                          <RotateCcw v-else class="h-4 w-4" />
                        </Button>
                      </div>
                      <!-- Show next value result inline -->
                      <div v-if="nextValueResults.has(seq.name)" class="mt-1">
                        <span class="text-xs text-muted-foreground">{{ $t('admin.sequences.generated') }} </span>
                        <code class="text-xs bg-primary/10 text-primary px-1.5 py-0.5 rounded font-medium font-mono">
                          {{ nextValueResults.get(seq.name)!.formattedValue }}
                        </code>
                      </div>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <!-- Detail Panel -->
          <transition
            enter-active-class="transition-all duration-200 ease-out"
            leave-active-class="transition-all duration-150 ease-in"
            enter-from-class="opacity-0 translate-x-4"
            enter-to-class="opacity-100 translate-x-0"
            leave-from-class="opacity-100 translate-x-0"
            leave-to-class="opacity-0 translate-x-4"
          >
            <Card v-if="selectedSequence" class="w-80 shrink-0 self-start sticky top-20">
              <CardContent class="p-5">
                <!-- Header -->
                <div class="flex items-center justify-between pb-4 border-b">
                  <div class="flex items-center gap-3 min-w-0">
                    <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center shrink-0">
                      <Hash class="h-5 w-5 text-primary" />
                    </div>
                    <div class="min-w-0">
                      <h3 class="font-semibold text-sm truncate">{{ selectedSequence.name }}</h3>
                      <p class="text-xs text-muted-foreground truncate">{{ selectedSequence.forEntity }}.{{ selectedSequence.forField }}</p>
                    </div>
                  </div>
                  <Button
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0 shrink-0"
                    @click="selectedSequence = null"
                  >
                    <X class="h-4 w-4" />
                  </Button>
                </div>

                <!-- Details -->
                <div class="space-y-3 py-4 border-b">
                  <div>
                    <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.patternCol') }}</p>
                    <code class="text-sm mt-0.5 block bg-muted px-2 py-1 rounded font-mono">{{ selectedSequence.pattern }}</code>
                  </div>
                  <div class="grid grid-cols-2 gap-3">
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.scopeCol') }}</p>
                      <Badge variant="outline" class="mt-1 text-[11px] font-medium" :class="scopeBadgeClasses(selectedSequence.scope)">
                        {{ selectedSequence.scope }}
                      </Badge>
                    </div>
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.resetPolicyCol') }}</p>
                      <Badge variant="outline" class="mt-1 text-[11px] font-medium" :class="resetBadgeClasses(selectedSequence.resetOn)">
                        {{ selectedSequence.resetOn }}
                      </Badge>
                    </div>
                  </div>
                  <div class="grid grid-cols-2 gap-3">
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.startValue') }}</p>
                      <p class="text-sm mt-0.5 font-medium">{{ selectedSequence.startValue }}</p>
                    </div>
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.increment') }}</p>
                      <p class="text-sm mt-0.5 font-medium">{{ selectedSequence.increment }}</p>
                    </div>
                  </div>
                  <div class="grid grid-cols-2 gap-3">
                    <div>
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.padding') }}</p>
                      <p class="text-sm mt-0.5 font-medium">{{ selectedSequence.padding }}</p>
                    </div>
                    <div v-if="selectedSequence.maxValue">
                      <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider">{{ $t('admin.sequences.maxValue') }}</p>
                      <p class="text-sm mt-0.5 font-medium">{{ selectedSequence.maxValue }}</p>
                    </div>
                  </div>
                </div>

                <!-- Current Value -->
                <div class="py-4 border-b">
                  <p class="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-2">{{ $t('admin.sequences.currentValueCol') }}</p>
                  <div v-if="loadingCurrentValues.has(selectedSequence.name)" class="flex items-center gap-2">
                    <Spinner size="sm" />
                    <span class="text-sm text-muted-foreground">{{ $t('common.loading') }}</span>
                  </div>
                  <div v-else-if="currentValues.has(selectedSequence.name)">
                    <code class="text-lg font-bold font-mono text-primary">
                      {{ currentValues.get(selectedSequence.name)!.formattedValue }}
                    </code>
                    <span class="text-sm text-muted-foreground ml-2">
                      (#{{ currentValues.get(selectedSequence.name)!.currentValue }})
                    </span>
                  </div>
                  <Button v-else size="sm" variant="outline" class="w-full" @click="loadCurrentValue(selectedSequence)">
                    {{ $t('common.load') }} {{ $t('admin.sequences.currentValueCol') }}
                  </Button>
                  <p v-if="actionErrors.has(selectedSequence.name)" class="text-xs text-destructive mt-2">
                    {{ actionErrors.get(selectedSequence.name) }}
                  </p>
                  <!-- Next value result -->
                  <div v-if="nextValueResults.has(selectedSequence.name)" class="mt-2 p-2 bg-primary/5 rounded-md">
                    <span class="text-xs text-muted-foreground">{{ $t('admin.sequences.generated') }}</span>
                    <code class="text-sm font-bold font-mono text-primary ml-1">
                      {{ nextValueResults.get(selectedSequence.name)!.formattedValue }}
                    </code>
                  </div>
                </div>

                <!-- Actions -->
                <div class="flex flex-col gap-2 pt-4">
                  <Button
                    variant="outline"
                    size="sm"
                    class="w-full justify-start"
                    :disabled="loadingNextValues.has(selectedSequence.name)"
                    @click="getNextValue(selectedSequence)"
                  >
                    <Spinner v-if="loadingNextValues.has(selectedSequence.name)" size="sm" class="mr-2" />
                    <SkipForward v-else class="mr-2 h-4 w-4" />
                    {{ $t('admin.sequences.getNext') }}
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    class="w-full justify-start text-destructive hover:text-destructive"
                    :disabled="resettingSequences.has(selectedSequence.name)"
                    @click="resetSequence(selectedSequence)"
                  >
                    <Spinner v-if="resettingSequences.has(selectedSequence.name)" size="sm" class="mr-2" />
                    <RotateCcw v-else class="mr-2 h-4 w-4" />
                    {{ $t('admin.sequences.resetSequence') }}
                  </Button>
                </div>
              </CardContent>
            </Card>
          </transition>
        </div>
      </template>
    </div>

    <ConfirmDialog
      :open="confirmDialog.isOpen.value"
      :title="confirmDialog.title.value"
      :description="confirmDialog.description.value"
      :confirm-label="confirmDialog.confirmLabel.value"
      :cancel-label="confirmDialog.cancelLabel.value"
      :variant="confirmDialog.variant.value"
      @confirm="confirmDialog.handleConfirm"
      @cancel="confirmDialog.handleCancel"
      @update:open="confirmDialog.isOpen.value = $event"
    />
  </DefaultLayout>
</template>
