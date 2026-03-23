<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { ArrowRight, Database, CheckCircle2, XCircle, Loader2, RefreshCw } from 'lucide-vue-next'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import Wizard from '@/components/smart/Wizard.vue'
import type { WizardStep } from '@/composables/useWizard'
import { Button } from '@/components/ui/button'
import { tenantService } from '@/services/tenantService'
import { metadataService } from '@/services/metadataService'
import { migrationService } from '@/services/migrationService'
import type { MigrationEntityResult } from '@/services/migrationService'
import { useUiStore } from '@/stores/ui'
import type { Tenant } from '@/types/tenant'
import type { EntityMetadata } from '@/types/metadata'

const uiStore = useUiStore()

// ── Data ──────────────────────────────────────────────────────────────────────

const tenants = ref<Tenant[]>([])
const loadingTenants = ref(false)
const sourceTenantId = ref('')
const targetTenantId = ref('')

const modules = ref<string[]>([])
const selectedModule = ref('')
const entities = ref<EntityMetadata[]>([])
const selectedEntities = ref<Set<string>>(new Set())
const loadingEntities = ref(false)

const maxRowsPerEntity = ref(5000)

// Migration state
type MigrationPhase = 'idle' | 'running' | 'done' | 'error'
const migrationPhase = ref<MigrationPhase>('idle')
const migrationProgress = ref({
  currentEntity: '',
  entitiesCompleted: 0,
  totalEntities: 0,
  rowsCopied: 0,
})
const migrationResults = ref<MigrationEntityResult[]>([])
const migrationError = ref<string | null>(null)

const wizardKey = ref(0)

// ── Computed ──────────────────────────────────────────────────────────────────

const sourceTenant = computed(() => tenants.value.find((t) => t.id === sourceTenantId.value))
const targetTenant = computed(() => tenants.value.find((t) => t.id === targetTenantId.value))
const selectedCount = computed(() => selectedEntities.value.size)

const tenantsValid = computed(
  () =>
    !!sourceTenantId.value &&
    !!targetTenantId.value &&
    sourceTenantId.value !== targetTenantId.value,
)

const totalRowsCopied = computed(() =>
  migrationResults.value.reduce((s, r) => s + r.rowsCopied, 0),
)
const totalErrors = computed(() =>
  migrationResults.value.reduce((s, r) => s + Math.max(0, r.errors), 0),
)

const wizardSteps = computed<WizardStep[]>(() => [
  {
    key: 'tenants',
    title: 'Select Tenants',
    subtitle: tenantsValid.value
      ? `${sourceTenant.value?.name} → ${targetTenant.value?.name}`
      : 'Choose source & target',
    validate: () => tenantsValid.value,
  },
  {
    key: 'entities',
    title: 'Select Data',
    subtitle: selectedCount.value > 0 ? `${selectedCount.value} entities` : 'Choose entities',
    validate: () => selectedCount.value > 0 && !!selectedModule.value,
  },
  {
    key: 'preview',
    title: 'Preview',
    subtitle: 'Confirm migration',
  },
  {
    key: 'migrate',
    title: 'Migrate',
    subtitle:
      migrationPhase.value === 'done' ? `${totalRowsCopied.value} rows copied` : '',
    optional: true,
  },
])

// ── Lifecycle ─────────────────────────────────────────────────────────────────

async function loadTenants() {
  loadingTenants.value = true
  try {
    tenants.value = await tenantService.getAll()
  } catch {
    uiStore.error('Failed to load tenants')
  } finally {
    loadingTenants.value = false
  }
}

watch(selectedModule, async (mod) => {
  if (!mod) {
    entities.value = []
    return
  }
  loadingEntities.value = true
  try {
    const result = await metadataService.getEntities(mod)
    entities.value = result.filter((e) => !e.isAbstract)
    selectedEntities.value = new Set(entities.value.map((e) => e.name))
  } catch {
    entities.value = []
  } finally {
    loadingEntities.value = false
  }
})

async function loadModules() {
  try {
    const mods = await metadataService.getModules()
    modules.value = mods.map((m) => m.name)
    if (modules.value.length === 1) selectedModule.value = modules.value[0]
  } catch {
    modules.value = []
  }
}

loadTenants()
loadModules()

// ── Actions ───────────────────────────────────────────────────────────────────

function toggleEntity(name: string) {
  const s = new Set(selectedEntities.value)
  if (s.has(name)) s.delete(name)
  else s.add(name)
  selectedEntities.value = s
}

function toggleAll() {
  if (selectedCount.value === entities.value.length) {
    selectedEntities.value = new Set()
  } else {
    selectedEntities.value = new Set(entities.value.map((e) => e.name))
  }
}

async function executeMigration() {
  if (migrationPhase.value !== 'idle') return
  migrationPhase.value = 'running'
  migrationError.value = null
  migrationResults.value = []

  try {
    const results = await migrationService.migrate(
      sourceTenantId.value,
      targetTenantId.value,
      selectedModule.value,
      [...selectedEntities.value],
      (progress) => {
        migrationProgress.value = progress
      },
      maxRowsPerEntity.value,
    )
    migrationResults.value = results
    migrationPhase.value = 'done'

    if (totalErrors.value === 0) {
      uiStore.success(`Migration complete: ${totalRowsCopied.value} rows copied`)
    }
  } catch (err) {
    migrationError.value = err instanceof Error ? err.message : 'Migration failed'
    migrationPhase.value = 'error'
    uiStore.error('Migration failed')
  }
}

function resetWizard() {
  sourceTenantId.value = ''
  targetTenantId.value = ''
  selectedModule.value = ''
  selectedEntities.value = new Set()
  migrationPhase.value = 'idle'
  migrationProgress.value = {
    currentEntity: '',
    entitiesCompleted: 0,
    totalEntities: 0,
    rowsCopied: 0,
  }
  migrationResults.value = []
  migrationError.value = null
  wizardKey.value++
}
</script>

<template>
  <DefaultLayout>
    <div class="max-w-3xl mx-auto px-4 py-8">
      <!-- Header -->
      <div class="mb-8">
        <div class="flex items-center gap-3 mb-2">
          <Database class="h-6 w-6 text-primary" />
          <h1 class="text-2xl font-bold">Cross-Tenant Data Migration</h1>
        </div>
        <p class="text-muted-foreground">Copy entity data from one tenant to another.</p>
      </div>

      <!-- Loading state -->
      <div
        v-if="loadingTenants"
        class="flex items-center justify-center py-12 text-muted-foreground"
      >
        <Loader2 class="h-6 w-6 animate-spin mr-2" />
        Loading...
      </div>

      <div
        v-else-if="tenants.length < 2"
        class="rounded-lg border border-dashed p-12 text-center text-muted-foreground"
      >
        <Database class="h-10 w-10 mx-auto mb-3 opacity-40" />
        <p>At least 2 tenants are required for data migration.</p>
      </div>

      <Wizard
        v-else
        :key="wizardKey"
        :steps="wizardSteps"
        show-progress-bar
        @complete="executeMigration"
      >
        <!-- Step 1: Tenant selection -->
        <template #step-tenants>
          <div class="space-y-6">
            <div>
              <label class="text-sm font-medium block mb-1.5">Source Tenant</label>
              <select
                v-model="sourceTenantId"
                class="w-full border rounded-lg px-3 py-2 text-sm bg-background"
              >
                <option value="">-- Select source tenant --</option>
                <option
                  v-for="tenant in tenants"
                  :key="tenant.id"
                  :value="tenant.id"
                  :disabled="tenant.id === targetTenantId"
                >
                  {{ tenant.name }} ({{ tenant.code }})
                </option>
              </select>
            </div>

            <div class="flex items-center gap-2 text-muted-foreground">
              <div class="flex-1 h-px bg-border" />
              <ArrowRight class="h-4 w-4" />
              <div class="flex-1 h-px bg-border" />
            </div>

            <div>
              <label class="text-sm font-medium block mb-1.5">Target Tenant</label>
              <select
                v-model="targetTenantId"
                class="w-full border rounded-lg px-3 py-2 text-sm bg-background"
              >
                <option value="">-- Select target tenant --</option>
                <option
                  v-for="tenant in tenants"
                  :key="tenant.id"
                  :value="tenant.id"
                  :disabled="tenant.id === sourceTenantId"
                >
                  {{ tenant.name }} ({{ tenant.code }})
                </option>
              </select>
            </div>

            <p
              v-if="sourceTenantId && targetTenantId && sourceTenantId === targetTenantId"
              class="text-sm text-destructive"
            >
              Source and target tenant must be different.
            </p>
          </div>
        </template>

        <!-- Step 2: Entity selection -->
        <template #step-entities>
          <div class="space-y-4">
            <div>
              <label class="text-sm font-medium block mb-1.5">Module</label>
              <select
                v-model="selectedModule"
                class="w-full border rounded-lg px-3 py-2 text-sm bg-background"
              >
                <option value="">-- Select module --</option>
                <option v-for="mod in modules" :key="mod" :value="mod">{{ mod }}</option>
              </select>
            </div>

            <div
              v-if="loadingEntities"
              class="flex items-center gap-2 text-muted-foreground text-sm"
            >
              <Loader2 class="h-4 w-4 animate-spin" />
              Loading entities...
            </div>

            <div v-else-if="selectedModule && entities.length > 0">
              <div class="flex items-center justify-between mb-2">
                <label class="text-sm font-medium">Entities to migrate</label>
                <Button variant="ghost" size="sm" @click="toggleAll">
                  {{ selectedCount === entities.length ? 'Deselect All' : 'Select All' }}
                </Button>
              </div>
              <div class="border rounded-lg divide-y max-h-[280px] overflow-y-auto">
                <label
                  v-for="entity in entities"
                  :key="entity.name"
                  class="flex items-center gap-3 px-4 py-2.5 hover:bg-muted/50 cursor-pointer"
                >
                  <input
                    type="checkbox"
                    :checked="selectedEntities.has(entity.name)"
                    class="h-4 w-4 rounded"
                    @change="toggleEntity(entity.name)"
                  />
                  <span class="text-sm">{{ entity.displayName || entity.name }}</span>
                </label>
              </div>
            </div>

            <div
              v-else-if="selectedModule && !loadingEntities && entities.length === 0"
              class="text-sm text-muted-foreground"
            >
              No entities found in this module.
            </div>

            <div class="mt-4">
              <label class="text-sm font-medium block mb-1.5">Max rows per entity</label>
              <input
                v-model.number="maxRowsPerEntity"
                type="number"
                min="100"
                max="50000"
                step="1000"
                class="w-36 border rounded-lg px-3 py-1.5 text-sm bg-background"
              />
            </div>
          </div>
        </template>

        <!-- Step 3: Preview -->
        <template #step-preview>
          <div class="space-y-4">
            <div class="rounded-lg border p-4">
              <h3 class="text-sm font-medium mb-3">Migration Summary</h3>
              <dl class="grid grid-cols-2 gap-3 text-sm">
                <div>
                  <dt class="text-muted-foreground">Source</dt>
                  <dd class="font-medium">{{ sourceTenant?.name }}</dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">Target</dt>
                  <dd class="font-medium">{{ targetTenant?.name }}</dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">Module</dt>
                  <dd class="font-medium">{{ selectedModule }}</dd>
                </div>
                <div>
                  <dt class="text-muted-foreground">Entities</dt>
                  <dd class="font-medium">{{ selectedCount }}</dd>
                </div>
              </dl>
            </div>

            <div class="border rounded-lg divide-y">
              <div
                v-for="name in [...selectedEntities]"
                :key="name"
                class="flex items-center justify-between px-4 py-2.5 text-sm"
              >
                <span>{{ name }}</span>
                <span class="text-muted-foreground text-xs">up to {{ maxRowsPerEntity }} rows</span>
              </div>
            </div>

            <div
              class="rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 p-3 text-sm text-amber-800 dark:text-amber-300"
            >
              Warning: This will INSERT records into the target tenant. Existing records will not be
              deleted or modified.
            </div>
          </div>
        </template>

        <!-- Step 4: Migration execution + results -->
        <template #step-migrate>
          <div class="space-y-4">
            <!-- Idle -->
            <div
              v-if="migrationPhase === 'idle'"
              class="text-center py-8 text-muted-foreground"
            >
              <Database class="h-10 w-10 mx-auto mb-3 opacity-40" />
              <p class="text-sm">Click "Complete" to start the migration.</p>
            </div>

            <!-- Running -->
            <div v-else-if="migrationPhase === 'running'" class="space-y-4">
              <div class="flex items-center gap-2 text-sm text-muted-foreground">
                <Loader2 class="h-4 w-4 animate-spin" />
                Migrating {{ migrationProgress.currentEntity }}...
              </div>
              <div class="w-full bg-muted rounded-full h-2">
                <div
                  class="bg-primary h-2 rounded-full transition-all duration-300"
                  :style="{
                    width:
                      migrationProgress.totalEntities > 0
                        ? Math.round(
                            (migrationProgress.entitiesCompleted /
                              migrationProgress.totalEntities) *
                              100,
                          ) + '%'
                        : '0%',
                  }"
                />
              </div>
              <p class="text-xs text-center text-muted-foreground">
                {{ migrationProgress.entitiesCompleted }} / {{ migrationProgress.totalEntities }}
                entities &mdash; {{ migrationProgress.rowsCopied }} rows copied
              </p>
            </div>

            <!-- Done -->
            <div v-else-if="migrationPhase === 'done'" class="space-y-4">
              <div class="text-center py-2">
                <div
                  class="h-12 w-12 rounded-full flex items-center justify-center mx-auto mb-3"
                  :class="
                    totalErrors > 0
                      ? 'bg-amber-100 dark:bg-amber-900/30'
                      : 'bg-green-100 dark:bg-green-900/30'
                  "
                >
                  <CheckCircle2 v-if="totalErrors === 0" class="h-6 w-6 text-green-600" />
                  <XCircle v-else class="h-6 w-6 text-amber-600" />
                </div>
                <p class="font-medium text-sm">Migration complete</p>
                <p class="text-xs text-muted-foreground mt-1">
                  {{ totalRowsCopied }} rows copied, {{ totalErrors }} errors
                </p>
              </div>

              <div class="border rounded-lg divide-y">
                <div
                  v-for="result in migrationResults"
                  :key="result.entityType"
                  class="flex items-center justify-between px-4 py-2.5 text-sm"
                >
                  <span>{{ result.entityType }}</span>
                  <div class="flex items-center gap-3 text-xs">
                    <span class="text-green-600">{{ result.rowsCopied }} copied</span>
                    <span v-if="result.errors > 0" class="text-destructive">
                      {{ result.errors }} failed
                    </span>
                    <span v-if="result.errors === -1" class="text-destructive">fetch failed</span>
                  </div>
                </div>
              </div>

              <Button variant="outline" size="sm" @click="resetWizard">
                <RefreshCw class="mr-2 h-4 w-4" />
                New Migration
              </Button>
            </div>

            <!-- Error -->
            <div
              v-else-if="migrationPhase === 'error'"
              class="rounded-lg bg-destructive/10 border border-destructive/30 p-4 text-destructive text-sm"
            >
              {{ migrationError }}
              <div class="mt-3">
                <Button variant="outline" size="sm" @click="resetWizard">Retry</Button>
              </div>
            </div>
          </div>
        </template>
      </Wizard>
    </div>
  </DefaultLayout>
</template>
