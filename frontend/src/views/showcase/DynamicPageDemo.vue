<script setup lang="ts">
import { ref, reactive, computed } from 'vue'
import { useRouter } from 'vue-router'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import DynamicPage from '@/components/layout/DynamicPage.vue'
import DynamicPageTitle from '@/components/layout/DynamicPageTitle.vue'
import DynamicPageHeader from '@/components/layout/DynamicPageHeader.vue'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { ConfirmDialog } from '@/components/common'
import {
  ArrowLeft,
  FileText,
  Printer,
  CheckCircle,
  Package,
  StickyNote,
  Pencil,
  X,
  Save,
} from 'lucide-vue-next'

const router = useRouter()
const confirmDialog = useConfirmDialog()

// ── Demo controls ───────────────────────────────────────────────────

const headerCollapsible = ref(true)
const headerPinnable = ref(true)
const showFooter = ref(true)
const footerAlign = ref<'left' | 'center' | 'right' | 'between'>('right')

const footerAlignOptions: { label: string; value: 'left' | 'center' | 'right' | 'between' }[] = [
  { label: 'Left', value: 'left' },
  { label: 'Center', value: 'center' },
  { label: 'Right', value: 'right' },
  { label: 'Between', value: 'between' },
]

// ── Edit mode ────────────────────────────────────────────────────────

const isEditing = ref(false)
const saveDraftLabel = ref('Save Draft')
const approveLabel = ref('Approve')

function handlePrint() {
  window.print()
}
function handleSaveDraft() {
  saveDraftLabel.value = 'Saved!'
  setTimeout(() => { saveDraftLabel.value = 'Save Draft' }, 2000)
}
async function handleApprove() {
  const confirmed = await confirmDialog.confirm({
    title: 'Submit for Approval',
    description: 'Submit PO-2026-042 for approval? (Demo only)',
    confirmLabel: 'Submit',
  })
  if (confirmed) {
    approveLabel.value = 'Submitted!'
    setTimeout(() => { approveLabel.value = 'Approve' }, 2500)
  }
}

// Saved (committed) data
const saved = reactive({
  vendor: 'Staples Business Advantage',
  department: 'Facilities — Office Management',
  requestedBy: 'Maria Chen',
  costCenter: 'CC-4200 (Office Operations)',
  shippingAddress: 'Building B, Floor 3, Loading Dock',
  paymentTerms: 'Net 45',
  deliveryDate: 'Feb 24, 2026',
})

// Draft copy mutated while editing
const draft = reactive({ ...saved })

// Line items with editable qty and unit price
const savedLineItems = ref([
  { item: 10, description: 'Ergonomic Office Chair — Mesh Back', qty: 12, unit: 'EA', unitPrice: 289.00 },
  { item: 20, description: 'Standing Desk Converter — 36"',      qty:  6, unit: 'EA', unitPrice: 159.50 },
  { item: 30, description: 'Monitor Arm — Dual Mount',           qty:  6, unit: 'EA', unitPrice:  54.75 },
  { item: 40, description: 'Cable Management Kit',               qty: 12, unit: 'EA', unitPrice:   6.17 },
])
const draftLineItems = ref(savedLineItems.value.map(r => ({ ...r })))

const lineItems = computed(() => isEditing.value ? draftLineItems.value : savedLineItems.value)

const grandTotal = computed(() =>
  lineItems.value.reduce((sum, r) => sum + r.qty * r.unitPrice, 0)
)

function startEdit() {
  Object.assign(draft, saved)
  draftLineItems.value = savedLineItems.value.map(r => ({ ...r }))
  isEditing.value = true
}

function cancelEdit() {
  isEditing.value = false
}

function saveEdit() {
  Object.assign(saved, draft)
  savedLineItems.value = draftLineItems.value.map(r => ({
    ...r,
    qty: Number(r.qty),
    unitPrice: Number(r.unitPrice),
  }))
  isEditing.value = false
}

// ── Header attributes (derived from saved data) ──────────────────────

const headerAttributes = computed(() => [
  { label: 'Vendor',        value: saved.vendor },
  { label: 'Status',        value: 'Awaiting Approval', variant: 'warning' as const },
  { label: 'Total Amount',  value: `$${grandTotal.value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` },
  { label: 'Currency',      value: 'USD' },
  { label: 'Created By',    value: saved.requestedBy },
  { label: 'Created Date',  value: 'Feb 3, 2026' },
  { label: 'Payment Terms', value: saved.paymentTerms },
  { label: 'Delivery Date', value: saved.deliveryDate },
])

// ── Notes ────────────────────────────────────────────────────────────

const notes = [
  { date: 'Feb 3, 2026', author: 'Maria Chen', text: 'PO created for Q1 office refresh. Items selected from approved vendor catalog.' },
  { date: 'Feb 4, 2026', author: 'System', text: 'Budget check passed. Remaining department budget: $12,340.00.' },
  { date: 'Feb 5, 2026', author: 'James Park', text: 'Please confirm delivery address is Building B, Floor 3 loading dock.' },
]
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6 pb-12">
      <!-- Page Header -->
      <div class="flex items-center justify-between">
        <div>
          <div class="flex items-center gap-3 mb-1">
            <Button variant="ghost" size="icon" @click="router.push('/showcase')">
              <ArrowLeft class="h-4 w-4" />
            </Button>
            <h1 class="text-3xl font-bold text-foreground">Dynamic Page</h1>
          </div>
          <p class="text-muted-foreground ml-11">
            General-purpose page layout with collapsible and pinnable header, floating footer bar
          </p>
        </div>
      </div>

      <!-- Demo Controls -->
      <Card>
        <CardHeader>
          <CardTitle>Controls</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="flex flex-wrap items-center gap-4">
            <label class="flex items-center gap-2 text-sm">
              <input v-model="headerCollapsible" type="checkbox" class="rounded border-border" />
              Collapsible
            </label>
            <label class="flex items-center gap-2 text-sm">
              <input v-model="headerPinnable" type="checkbox" class="rounded border-border" />
              Pinnable
            </label>
            <label class="flex items-center gap-2 text-sm">
              <input v-model="showFooter" type="checkbox" class="rounded border-border" />
              Show Footer
            </label>
            <div class="flex items-center gap-2">
              <span class="text-sm text-muted-foreground">Footer align:</span>
              <div class="flex gap-1">
                <Button
                  v-for="opt in footerAlignOptions"
                  :key="opt.value"
                  :variant="footerAlign === opt.value ? 'default' : 'outline'"
                  size="sm"
                  @click="footerAlign = opt.value"
                >
                  {{ opt.label }}
                </Button>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Dynamic Page Container -->
      <div class="border rounded-lg overflow-hidden" style="height: 700px">
        <DynamicPage
          :header-collapsible="headerCollapsible"
          :header-pinnable="headerPinnable"
          :show-footer="showFooter"
          :footer-align="footerAlign"
          :show-back-button="true"
          class="h-full"
          @back="router.push('/showcase')"
        >
          <!-- Breadcrumb -->
          <template #breadcrumb>
            <nav class="flex items-center gap-1.5 text-sm text-muted-foreground">
              <span class="hover:text-foreground cursor-pointer">Procurement</span>
              <span>/</span>
              <span class="hover:text-foreground cursor-pointer">Purchase Orders</span>
              <span>/</span>
              <span class="text-foreground font-medium">PO-2026-042</span>
            </nav>
          </template>

          <!-- Title -->
          <template #title>
            <DynamicPageTitle
              title="Purchase Order PO-2026-042"
              :subtitle="isEditing ? 'Editing — unsaved changes' : 'Procurement — Office Supplies'"
            />
          </template>

          <!-- Header Actions -->
          <template #headerActions>
            <template v-if="!isEditing">
              <Button variant="outline" size="sm" @click="startEdit">
                <Pencil class="mr-2 h-4 w-4" />
                Edit
              </Button>
              <Button variant="outline" size="sm" @click="handlePrint">
                <Printer class="mr-2 h-4 w-4" />
                Print
              </Button>
              <Button size="sm" @click="handleApprove">
                <CheckCircle class="mr-2 h-4 w-4" />
                {{ approveLabel }}
              </Button>
            </template>
            <template v-else>
              <Badge variant="outline" class="text-amber-600 border-amber-400 bg-amber-50 dark:bg-amber-950/30 mr-1">
                Editing
              </Badge>
              <Button variant="outline" size="sm" @click="cancelEdit">
                <X class="mr-2 h-4 w-4" />
                Cancel
              </Button>
              <Button size="sm" @click="saveEdit">
                <Save class="mr-2 h-4 w-4" />
                Save
              </Button>
            </template>
          </template>

          <!-- Collapsible Header Content -->
          <template #header>
            <DynamicPageHeader>
              <div v-for="attr in headerAttributes" :key="attr.label" class="space-y-1">
                <p class="text-xs text-muted-foreground">{{ attr.label }}</p>
                <p v-if="!attr.variant" class="text-sm font-medium text-foreground">{{ attr.value }}</p>
                <Badge v-else variant="outline" class="text-amber-600 border-amber-300 bg-amber-50 dark:text-amber-400 dark:border-amber-700 dark:bg-amber-950/30">
                  {{ attr.value }}
                </Badge>
              </div>
            </DynamicPageHeader>
          </template>

          <!-- Main Content -->
          <div class="space-y-6">
            <!-- General Information -->
            <Card :class="isEditing ? 'ring-2 ring-primary/20' : ''">
              <CardHeader>
                <CardTitle class="flex items-center gap-2">
                  <FileText class="h-4 w-4" />
                  General Information
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div class="grid grid-cols-2 md:grid-cols-3 gap-y-4 gap-x-8">
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">PO Number</p>
                    <p class="text-sm font-medium">PO-2026-042</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Vendor</p>
                    <Input v-if="isEditing" v-model="draft.vendor" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.vendor }}</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Department</p>
                    <Input v-if="isEditing" v-model="draft.department" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.department }}</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Requested By</p>
                    <Input v-if="isEditing" v-model="draft.requestedBy" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.requestedBy }}</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Cost Center</p>
                    <Input v-if="isEditing" v-model="draft.costCenter" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.costCenter }}</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Shipping Address</p>
                    <Input v-if="isEditing" v-model="draft.shippingAddress" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.shippingAddress }}</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Payment Terms</p>
                    <Input v-if="isEditing" v-model="draft.paymentTerms" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.paymentTerms }}</p>
                  </div>
                  <div class="space-y-1">
                    <p class="text-xs text-muted-foreground">Delivery Date</p>
                    <Input v-if="isEditing" v-model="draft.deliveryDate" class="h-8 text-sm" />
                    <p v-else class="text-sm font-medium">{{ saved.deliveryDate }}</p>
                  </div>
                </div>
              </CardContent>
            </Card>

            <!-- Line Items -->
            <Card :class="isEditing ? 'ring-2 ring-primary/20' : ''">
              <CardHeader>
                <CardTitle class="flex items-center gap-2">
                  <Package class="h-4 w-4" />
                  Line Items
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div class="overflow-x-auto">
                  <table class="w-full text-sm">
                    <thead>
                      <tr class="border-b text-left text-muted-foreground">
                        <th class="py-2 pr-4 font-medium">Item</th>
                        <th class="py-2 pr-4 font-medium">Description</th>
                        <th class="py-2 pr-4 font-medium text-right">Qty</th>
                        <th class="py-2 pr-4 font-medium">Unit</th>
                        <th class="py-2 pr-4 font-medium text-right">Unit Price</th>
                        <th class="py-2 font-medium text-right">Line Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="row in lineItems" :key="row.item" class="border-b last:border-b-0">
                        <td class="py-2.5 pr-4 text-muted-foreground">{{ row.item }}</td>
                        <td class="py-2.5 pr-4 font-medium">{{ row.description }}</td>
                        <td class="py-2.5 pr-4 text-right">
                          <Input
                            v-if="isEditing"
                            v-model.number="row.qty"
                            type="number"
                            min="1"
                            class="h-7 w-16 text-right text-sm ml-auto"
                          />
                          <span v-else>{{ row.qty }}</span>
                        </td>
                        <td class="py-2.5 pr-4 text-muted-foreground">{{ row.unit }}</td>
                        <td class="py-2.5 pr-4 text-right">
                          <Input
                            v-if="isEditing"
                            v-model.number="row.unitPrice"
                            type="number"
                            min="0"
                            step="0.01"
                            class="h-7 w-24 text-right text-sm ml-auto"
                          />
                          <span v-else>${{ row.unitPrice.toFixed(2) }}</span>
                        </td>
                        <td class="py-2.5 text-right font-medium">
                          ${{ (row.qty * row.unitPrice).toFixed(2) }}
                        </td>
                      </tr>
                    </tbody>
                    <tfoot>
                      <tr class="border-t font-semibold">
                        <td colspan="5" class="py-2.5 pr-4 text-right">Total</td>
                        <td class="py-2.5 text-right">
                          ${{ grandTotal.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }}
                        </td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </CardContent>
            </Card>

            <!-- Notes & Activity -->
            <Card>
              <CardHeader>
                <CardTitle class="flex items-center gap-2">
                  <StickyNote class="h-4 w-4" />
                  Notes & Activity
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div class="space-y-4">
                  <div v-for="(note, index) in notes" :key="index" class="flex gap-3">
                    <div class="flex flex-col items-center">
                      <div class="h-7 w-7 rounded-full bg-muted flex items-center justify-center shrink-0">
                        <span class="text-xs font-medium text-muted-foreground">
                          {{ note.author.charAt(0) }}
                        </span>
                      </div>
                      <div v-if="index < notes.length - 1" class="w-px flex-1 bg-border mt-1" />
                    </div>
                    <div class="pb-4">
                      <div class="flex items-baseline gap-2">
                        <span class="text-sm font-medium">{{ note.author }}</span>
                        <span class="text-xs text-muted-foreground">{{ note.date }}</span>
                      </div>
                      <p class="text-sm text-muted-foreground mt-0.5">{{ note.text }}</p>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <!-- Approval History -->
            <Card>
              <CardHeader>
                <CardTitle>Approval History</CardTitle>
              </CardHeader>
              <CardContent>
                <div class="space-y-3">
                  <div class="flex items-center justify-between py-2 border-b last:border-b-0">
                    <div>
                      <p class="text-sm font-medium">Budget Verification</p>
                      <p class="text-xs text-muted-foreground">Automatic check — Feb 4, 2026</p>
                    </div>
                    <Badge variant="default" class="bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-400">Passed</Badge>
                  </div>
                  <div class="flex items-center justify-between py-2 border-b last:border-b-0">
                    <div>
                      <p class="text-sm font-medium">Manager Approval</p>
                      <p class="text-xs text-muted-foreground">James Park — Pending</p>
                    </div>
                    <Badge variant="outline" class="text-amber-600 border-amber-300 dark:text-amber-400 dark:border-amber-700">Pending</Badge>
                  </div>
                  <div class="flex items-center justify-between py-2 border-b last:border-b-0">
                    <div>
                      <p class="text-sm font-medium">Finance Review</p>
                      <p class="text-xs text-muted-foreground">Not started</p>
                    </div>
                    <Badge variant="secondary">Waiting</Badge>
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>

          <!-- Footer -->
          <template #footer>
            <template v-if="!isEditing">
              <Button variant="outline" @click="handleSaveDraft">{{ saveDraftLabel }}</Button>
              <Button class="ml-2" @click="handleApprove">
                <CheckCircle class="mr-2 h-4 w-4" />
                Submit for Approval
              </Button>
            </template>
            <template v-else>
              <Button variant="outline" @click="cancelEdit">
                <X class="mr-2 h-4 w-4" />
                Cancel
              </Button>
              <Button class="ml-2" @click="saveEdit">
                <Save class="mr-2 h-4 w-4" />
                Save Changes
              </Button>
            </template>
          </template>
        </DynamicPage>
      </div>
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
