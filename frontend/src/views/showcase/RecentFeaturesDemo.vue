<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import SmartField from '@/components/smart/SmartField.vue'
import MessageStrip from '@/components/smart/MessageStrip.vue'
import { odataService } from '@/services'
import { ODataApiError } from '@/services/api'
import type { FieldMetadata } from '@/types/metadata'
import {
  ArrowLeft,
  Link2,
  ShieldCheck,
  Lock,
  Trash2,
  Shield,
  CheckCircle2,
  XCircle,
  Play,
  AlertTriangle,
} from 'lucide-vue-next'
import { RouterLink } from 'vue-router'

// ============================================================================
// Card 1: M:M Association $expand
// ============================================================================
const mmModule = ref('')
const mmEntity = ref('')
const mmNavProp = ref('')
const mmLoading = ref(false)
const mmResult = ref<unknown[] | null>(null)
const mmError = ref<string | null>(null)
const mmShowJson = ref(false)

async function tryMmExpand() {
  if (!mmModule.value || !mmEntity.value || !mmNavProp.value) return
  mmLoading.value = true
  mmResult.value = null
  mmError.value = null
  try {
    const resp = await odataService.query<Record<string, unknown>>(
      mmModule.value,
      mmEntity.value,
      { $expand: mmNavProp.value, $top: 5 },
      { skipCache: true }
    )
    mmResult.value = resp.value ?? []
  } catch (e) {
    mmError.value = e instanceof Error ? e.message : 'Request failed'
  } finally {
    mmLoading.value = false
  }
}

// ============================================================================
// Card 2: Required Association Validation (CardinalityViolation)
// ============================================================================
const cvModule = ref('')
const cvEntity = ref('')
const cvLoading = ref(false)
const cvResult = ref<{ success: boolean; code?: string; message?: string } | null>(null)

async function tryCardinalityViolation() {
  if (!cvModule.value || !cvEntity.value) return
  cvLoading.value = true
  cvResult.value = null
  try {
    await odataService.create(cvModule.value, cvEntity.value, {})
    cvResult.value = { success: true, message: 'Created successfully (no required associations enforced)' }
  } catch (e) {
    if (e instanceof ODataApiError) {
      cvResult.value = { success: false, code: e.code, message: e.message }
    } else {
      cvResult.value = { success: false, message: e instanceof Error ? e.message : 'Unknown error' }
    }
  } finally {
    cvLoading.value = false
  }
}

// ============================================================================
// Card 3: Field Protection (client-side demo)
// ============================================================================
const fpMode = ref<'create' | 'edit'>('create')

const fpFields: FieldMetadata[] = [
  {
    name: 'RegularField',
    type: 'String',
    displayName: 'Regular Field',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    maxLength: 100,
    annotations: {},
  },
  {
    name: 'ImmutableField',
    type: 'String',
    displayName: 'Immutable Field (set-once)',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    maxLength: 100,
    annotations: { 'Core.Immutable': true },
  },
  {
    name: 'ComputedField',
    type: 'String',
    displayName: 'Computed Field (server-generated)',
    isRequired: false,
    isReadOnly: true,
    isComputed: true,
    maxLength: 100,
    annotations: { 'Org.OData.Core.V1.Computed': true },
  },
  {
    name: 'ReadOnlyField',
    type: 'String',
    displayName: 'Read-Only Field',
    isRequired: false,
    isReadOnly: true,
    isComputed: false,
    maxLength: 100,
    annotations: {},
  },
]

const fpValues = ref<Record<string, unknown>>({
  RegularField: 'Editable in both modes',
  ImmutableField: 'Editable only on create',
  ComputedField: 'Auto-calculated by server',
  ReadOnlyField: 'Cannot be changed',
})

function fpUpdateValue(fieldName: string, value: unknown) {
  fpValues.value = { ...fpValues.value, [fieldName]: value }
}

function getFieldEditability(field: FieldMetadata): string {
  if (field.isComputed) return 'always-readonly'
  if (field.isReadOnly) return 'always-readonly'
  const annotations = field.annotations ?? {}
  if (annotations['Core.Immutable'] || annotations['Org.OData.Core.V1.Immutable']) {
    return fpMode.value === 'create' ? 'editable' : 'readonly-in-edit'
  }
  return 'editable'
}

function getSmartFieldMode(field: FieldMetadata): 'create' | 'edit' | 'display' {
  const editability = getFieldEditability(field)
  if (editability === 'always-readonly') return 'display'
  if (editability === 'readonly-in-edit') return 'display'
  return fpMode.value
}

// ============================================================================
// Card 4: Referential Integrity (409 on delete)
// ============================================================================
const riModule = ref('')
const riEntity = ref('')
const riId = ref('')
const riLoading = ref(false)
const riResult = ref<{ success: boolean; code?: string; message?: string; status?: number } | null>(null)

async function tryReferentialDelete() {
  if (!riModule.value || !riEntity.value || !riId.value) return
  riLoading.value = true
  riResult.value = null
  try {
    await odataService.delete(riModule.value, riEntity.value, riId.value)
    riResult.value = { success: true, message: 'Record was deleted successfully.' }
  } catch (e) {
    const axiosErr = e as { response?: { status?: number } }
    if (e instanceof ODataApiError) {
      riResult.value = { success: false, code: e.code, message: e.message, status: e.status }
    } else {
      riResult.value = {
        success: false,
        message: e instanceof Error ? e.message : 'Unknown error',
        status: axiosErr.response?.status,
      }
    }
  } finally {
    riLoading.value = false
  }
}

// ============================================================================
// Card 5: Access Control Scope (informational)
// ============================================================================
const scopeRows = [
  { scope: 'Global', filter: 'None', example: 'admin: full access everywhere', icon: Shield },
  { scope: 'Tenant', filter: 'tenant_id = current_tenant', example: 'Standard isolation per company', icon: ShieldCheck },
  { scope: 'Company', filter: 'company_id IN user_companies', example: 'User sees only their assigned companies', icon: Lock },
]
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8 pb-12">
      <!-- Page Header -->
      <div class="flex items-center gap-4">
        <RouterLink to="/showcase">
          <Button variant="ghost" size="sm">
            <ArrowLeft class="mr-2 h-4 w-4" />
            Showcase
          </Button>
        </RouterLink>
        <div>
          <h1 class="text-2xl font-bold text-foreground">Recent Backend Features</h1>
          <p class="text-sm text-muted-foreground mt-0.5">
            Interactive demos for M:M expand, cardinality validation, field protection, referential integrity, and access control scopes
          </p>
        </div>
      </div>

      <!-- ================================================================ -->
      <!-- Card 1: M:M Association $expand                                    -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="h-9 w-9 rounded-lg bg-primary/10 flex items-center justify-center">
              <Link2 class="h-5 w-5 text-primary" />
            </div>
            <div>
              <CardTitle>M:M Association $expand</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Many-to-many associations are resolved through junction tables transparently. The runtime
                detects the M:M pattern and performs a two-hop JOIN. Results appear as a nested array, just like 1:N.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent class="space-y-4">
          <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div>
              <label class="text-xs font-medium text-muted-foreground">Module</label>
              <Input v-model="mmModule" placeholder="e.g. hr" />
            </div>
            <div>
              <label class="text-xs font-medium text-muted-foreground">Entity</label>
              <Input v-model="mmEntity" placeholder="e.g. Employee" />
            </div>
            <div>
              <label class="text-xs font-medium text-muted-foreground">Navigation Property</label>
              <Input v-model="mmNavProp" placeholder="e.g. Skills" />
            </div>
          </div>
          <div class="flex items-center gap-3">
            <Button
              @click="tryMmExpand"
              :disabled="mmLoading || !mmModule || !mmEntity || !mmNavProp"
            >
              <Play class="mr-2 h-4 w-4" />
              Try $expand
            </Button>
            <Button
              v-if="mmResult"
              variant="outline"
              size="sm"
              @click="mmShowJson = !mmShowJson"
            >
              {{ mmShowJson ? 'Hide' : 'Show' }} JSON
            </Button>
          </div>

          <MessageStrip v-if="mmError" type="error" :title="mmError" :closable="true" @close="mmError = null" />

          <div v-if="mmResult !== null" class="space-y-2">
            <div class="flex items-center gap-2">
              <Badge variant="outline">{{ mmResult.length }} record(s) returned</Badge>
              <span class="text-xs text-muted-foreground">Each record includes nested "{{ mmNavProp }}" array</span>
            </div>
            <pre
              v-if="mmShowJson"
              class="bg-muted/50 rounded-lg p-4 text-xs font-mono overflow-auto max-h-64"
            >{{ JSON.stringify(mmResult, null, 2) }}</pre>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Card 2: Required Association Validation                            -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="h-9 w-9 rounded-lg bg-amber-500/10 flex items-center justify-center">
              <ShieldCheck class="h-5 w-5 text-amber-600" />
            </div>
            <div>
              <CardTitle>Required Association Validation</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Associations with cardinality <code>[1..1]</code> or <code>[1..*]</code> are enforced on create/update.
                Submitting an empty body triggers a <strong>400 CardinalityViolation</strong>.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent class="space-y-4">
          <div class="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label class="text-xs font-medium text-muted-foreground">Module</label>
              <Input v-model="cvModule" placeholder="e.g. sales" />
            </div>
            <div>
              <label class="text-xs font-medium text-muted-foreground">Entity</label>
              <Input v-model="cvEntity" placeholder="e.g. SalesOrder" />
            </div>
          </div>
          <Button
            @click="tryCardinalityViolation"
            :disabled="cvLoading || !cvModule || !cvEntity"
            variant="outline"
          >
            <Play class="mr-2 h-4 w-4" />
            Try Create Without FK
          </Button>

          <div v-if="cvResult" class="rounded-lg border p-4 space-y-2">
            <div class="flex items-center gap-2">
              <CheckCircle2 v-if="cvResult.success" class="h-5 w-5 text-emerald-500" />
              <XCircle v-else class="h-5 w-5 text-destructive" />
              <span class="font-medium text-sm">
                {{ cvResult.success ? 'Success' : 'Error' }}
              </span>
              <Badge v-if="cvResult.code" variant="destructive">{{ cvResult.code }}</Badge>
            </div>
            <p class="text-sm text-muted-foreground">{{ cvResult.message }}</p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Card 3: Field Protection                                           -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="h-9 w-9 rounded-lg bg-violet-500/10 flex items-center justify-center">
              <Lock class="h-5 w-5 text-violet-600" />
            </div>
            <div>
              <CardTitle>Field Protection</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Fields annotated with <code>Core.Immutable</code> are editable on create but locked in edit mode.
                <code>Computed</code> and <code>ReadOnly</code> fields are always display-only. Toggle the mode to see the difference.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent class="space-y-4">
          <div class="flex items-center gap-3">
            <span class="text-sm font-medium text-muted-foreground">Mode:</span>
            <Button
              :variant="fpMode === 'create' ? 'default' : 'outline'"
              size="sm"
              @click="fpMode = 'create'"
            >
              Create
            </Button>
            <Button
              :variant="fpMode === 'edit' ? 'default' : 'outline'"
              size="sm"
              @click="fpMode = 'edit'"
            >
              Edit
            </Button>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <div v-for="field in fpFields" :key="field.name" class="space-y-1">
              <div class="flex items-center gap-2">
                <Badge
                  :variant="getFieldEditability(field) === 'editable' ? 'default' : 'secondary'"
                  class="text-xs"
                >
                  {{ getFieldEditability(field) === 'editable' ? 'Editable' :
                     getFieldEditability(field) === 'readonly-in-edit' ? 'Locked (immutable)' :
                     'Read-Only' }}
                </Badge>
              </div>
              <SmartField
                :field="field"
                :modelValue="fpValues[field.name]"
                :mode="getSmartFieldMode(field)"
                module="demo"
                entitySet="Demo"
                @update:modelValue="fpUpdateValue(field.name, $event)"
              />
            </div>
          </div>

          <MessageStrip
            type="info"
            title="How it works"
            description="useSmartForm.getSubmitData() automatically strips immutable fields in edit mode, computed fields always, and readonly fields in edit mode. The server also enforces protection and returns 400 if a protected field is included."
          />
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Card 4: Referential Integrity (409 on delete)                      -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="h-9 w-9 rounded-lg bg-destructive/10 flex items-center justify-center">
              <Trash2 class="h-5 w-5 text-destructive" />
            </div>
            <div>
              <CardTitle>Referential Integrity</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Deleting a record that is referenced by other entities via associations returns
                <strong>409 ReferentialConstraintViolation</strong>. Composition children are cascade-deleted automatically.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent class="space-y-4">
          <MessageStrip
            type="warning"
            title="Caution"
            description="If the record has no references, it WILL be deleted. Use a record you know is referenced, or create a test record first."
          />
          <div class="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div>
              <label class="text-xs font-medium text-muted-foreground">Module</label>
              <Input v-model="riModule" placeholder="e.g. sales" />
            </div>
            <div>
              <label class="text-xs font-medium text-muted-foreground">Entity</label>
              <Input v-model="riEntity" placeholder="e.g. Customer" />
            </div>
            <div>
              <label class="text-xs font-medium text-muted-foreground">Record ID</label>
              <Input v-model="riId" placeholder="UUID of the record" />
            </div>
          </div>
          <Button
            @click="tryReferentialDelete"
            :disabled="riLoading || !riModule || !riEntity || !riId"
            variant="destructive"
          >
            <AlertTriangle class="mr-2 h-4 w-4" />
            Try Delete
          </Button>

          <div v-if="riResult" class="rounded-lg border p-4 space-y-2">
            <div class="flex items-center gap-2">
              <CheckCircle2 v-if="riResult.success" class="h-5 w-5 text-emerald-500" />
              <XCircle v-else class="h-5 w-5 text-destructive" />
              <span class="font-medium text-sm">
                {{ riResult.success ? 'Deleted' : `Error (HTTP ${riResult.status ?? '?'})` }}
              </span>
              <Badge v-if="riResult.code" variant="destructive">{{ riResult.code }}</Badge>
            </div>
            <p class="text-sm text-muted-foreground">{{ riResult.message }}</p>
          </div>

          <div class="text-sm text-muted-foreground space-y-1 border-t pt-3">
            <p><strong>Association references:</strong> Returns 409 — the parent record cannot be deleted while children reference it.</p>
            <p><strong>Composition children:</strong> Cascade-deleted automatically — deleting a parent removes all contained children.</p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Card 5: Access Control Scope                                       -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="h-9 w-9 rounded-lg bg-emerald-500/10 flex items-center justify-center">
              <Shield class="h-5 w-5 text-emerald-600" />
            </div>
            <div>
              <CardTitle>Access Control Scope Enforcement</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Access control rules now support three scope levels. If a user doesn't match the required scope,
                the runtime returns <strong>403 Forbidden</strong> with a descriptive error.
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b">
                  <th class="text-left py-2 pr-4 font-medium text-muted-foreground">Scope</th>
                  <th class="text-left py-2 pr-4 font-medium text-muted-foreground">Runtime Filter</th>
                  <th class="text-left py-2 font-medium text-muted-foreground">Example</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in scopeRows" :key="row.scope" class="border-b last:border-0">
                  <td class="py-3 pr-4">
                    <div class="flex items-center gap-2">
                      <component :is="row.icon" class="h-4 w-4 text-muted-foreground" />
                      <Badge variant="outline">{{ row.scope }}</Badge>
                    </div>
                  </td>
                  <td class="py-3 pr-4 font-mono text-xs">{{ row.filter }}</td>
                  <td class="py-3 text-muted-foreground">{{ row.example }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <MessageStrip
            type="info"
            title="Enforcement behavior"
            description="When a user lacks permission for an entity (no matching access control rule for their role + scope), the runtime returns HTTP 403. This applies to all CRUD operations. The CSDL $metadata does not expose scope details — enforcement is purely server-side."
            class="mt-4"
          />
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
