<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import ReportEditor from '@/components/admin/ReportEditor.vue'
import ReportPreview from '@/components/admin/ReportPreview.vue'
import { reportService } from '@/services/reportService'
import type { ReportTemplate, CreateReportRequest } from '@/services/reportService'
import { metadataService } from '@/services/metadataService'
import type { FieldMetadata } from '@/types/metadata'
import { useUiStore } from '@/stores/ui'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { generatePdf, printReport } from '@/utils/pdfGenerator'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  FileText,
  Plus,
  Trash2,
  Share2,
  Copy,
  Check,
  Clock,
  RefreshCw,
  Download,
  Printer,
  Link2,
  LinkIcon,
  Send
} from 'lucide-vue-next'

const { t } = useI18n()
const uiStore = useUiStore()
const { isOpen: confirmOpen, title: confirmTitle, description: confirmDesc, confirmLabel, cancelLabel, variant: confirmVariant, confirm, handleConfirm, handleCancel } = useConfirmDialog()

// ---- State ----
const templates = ref<ReportTemplate[]>([])
const selectedTemplate = ref<ReportTemplate | null>(null)
const editingTemplate = ref<CreateReportRequest | ReportTemplate | null>(null)
const showCreateForm = ref(false)
const activeTab = ref<'design' | 'preview' | 'share'>('design')
const availableFields = ref<FieldMetadata[]>([])
const reportData = ref<Record<string, unknown>[]>([])
const reportDataLoading = ref(false)
const reportDataError = ref<string | null>(null)
const searchQuery = ref('')
const loading = ref(false)
const saving = ref(false)
const copiedShareUrl = ref(false)

// ---- Computed ----
const filteredTemplates = computed(() => {
  const q = searchQuery.value.toLowerCase().trim()
  if (!q) return templates.value
  return templates.value.filter(
    (t) =>
      t.name.toLowerCase().includes(q) ||
      t.entityType.toLowerCase().includes(q) ||
      t.module.toLowerCase().includes(q)
  )
})

const isEditing = computed(() => showCreateForm.value || selectedTemplate.value !== null)

const shareUrl = computed(() => {
  const token = (selectedTemplate.value as ReportTemplate | null)?.shareToken
  if (!token) return null
  return `${window.location.origin}/reports/${token}`
})

// ---- Data loading ----
async function loadTemplates() {
  loading.value = true
  try {
    templates.value = await reportService.listTemplates()
  } catch {
    // silently ignore — empty state shown
  } finally {
    loading.value = false
  }
}

async function loadEntityMetadata(module: string, entityType: string) {
  try {
    const entity = await metadataService.getEntity(module, entityType)
    availableFields.value = entity.fields
  } catch {
    availableFields.value = []
  }
}

async function loadReportData() {
  const tmpl = selectedTemplate.value
  if (!tmpl) return
  reportDataLoading.value = true
  reportDataError.value = null
  try {
    const result = await reportService.getReportData(tmpl.id)
    reportData.value = result.rows
  } catch (err) {
    reportDataError.value = err instanceof Error ? err.message : t('report.loadError')
    reportData.value = []
  } finally {
    reportDataLoading.value = false
  }
}

// ---- Template selection / creation ----
async function selectTemplate(tmpl: ReportTemplate) {
  selectedTemplate.value = tmpl
  showCreateForm.value = false
  editingTemplate.value = { ...tmpl }
  activeTab.value = 'design'
  availableFields.value = []
  reportData.value = []
  reportDataError.value = null
  await loadEntityMetadata(tmpl.module, tmpl.entityType)
  await loadReportData()
}

function newTemplate() {
  selectedTemplate.value = null
  showCreateForm.value = true
  activeTab.value = 'design'
  availableFields.value = []
  reportData.value = []
  reportDataError.value = null
  editingTemplate.value = {
    name: '',
    description: '',
    module: '',
    entityType: '',
    layoutType: 'list',
    fields: [],
    sortBy: [],
    isPublic: false,
    scheduleRecipients: []
  } satisfies CreateReportRequest
}

function resetState() {
  selectedTemplate.value = null
  showCreateForm.value = false
  editingTemplate.value = null
  availableFields.value = []
  reportData.value = []
  reportDataError.value = null
}

// ---- Save ----
async function saveTemplate() {
  if (!editingTemplate.value) return
  saving.value = true
  try {
    if (selectedTemplate.value) {
      // Update existing
      const updated = await reportService.updateTemplate(
        selectedTemplate.value.id,
        editingTemplate.value as CreateReportRequest
      )
      uiStore.success(t('report.saved'))
      await loadTemplates()
      // Re-select the updated template
      const refreshed = templates.value.find((t) => t.id === updated.id) ?? updated
      selectedTemplate.value = refreshed
      editingTemplate.value = { ...refreshed }
    } else {
      // Create new
      const created = await reportService.createTemplate(editingTemplate.value as CreateReportRequest)
      uiStore.success(t('report.saved'))
      await loadTemplates()
      showCreateForm.value = false
      const refreshed = templates.value.find((t) => t.id === created.id) ?? created
      selectedTemplate.value = refreshed
      editingTemplate.value = { ...refreshed }
      await loadEntityMetadata(refreshed.module, refreshed.entityType)
    }
  } catch (err) {
    uiStore.error(err instanceof Error ? err.message : t('common.error'))
  } finally {
    saving.value = false
  }
}

// ---- Delete ----
async function deleteTemplate(id: string) {
  const confirmed = await confirm({
    title: t('report.deleteReport'),
    description: t('report.deleteConfirmDesc'),
    confirmLabel: t('common.delete'),
    variant: 'destructive'
  })
  if (!confirmed) return
  try {
    await reportService.deleteTemplate(id)
    uiStore.success(t('report.deleted'))
    if (selectedTemplate.value?.id === id) {
      resetState()
    }
    await loadTemplates()
  } catch {
    // ignore
  }
}

// ---- PDF / Print ----
async function handleDownloadPdf() {
  const tmpl = editingTemplate.value
  if (!tmpl) return
  try {
    await generatePdf(tmpl as ReportTemplate, reportData.value)
  } catch {
    // ignore
  }
}

function handlePrint() {
  const tmpl = editingTemplate.value
  if (!tmpl) return
  try {
    printReport(tmpl as ReportTemplate, reportData.value, 'report-preview-container')
  } catch {
    // ignore
  }
}

// ---- Share ----
async function handleShare() {
  const tmpl = selectedTemplate.value
  if (!tmpl) return
  try {
    const result = await reportService.shareTemplate(tmpl.id)
    // Refresh template to get shareToken
    const refreshed = await reportService.getTemplate(tmpl.id)
    // Update both references
    selectedTemplate.value = { ...refreshed, shareToken: result.shareToken }
    editingTemplate.value = { ...selectedTemplate.value }
    // Also update in list
    const idx = templates.value.findIndex((t) => t.id === tmpl.id)
    if (idx !== -1) templates.value[idx] = selectedTemplate.value
  } catch {
    // ignore
  }
}

async function handleRevokeShare() {
  const tmpl = selectedTemplate.value
  if (!tmpl) return
  try {
    await reportService.revokeShare(tmpl.id)
    selectedTemplate.value = { ...tmpl, shareToken: undefined }
    editingTemplate.value = { ...selectedTemplate.value }
    const idx = templates.value.findIndex((t) => t.id === tmpl.id)
    if (idx !== -1) templates.value[idx] = selectedTemplate.value
  } catch {
    // ignore
  }
}

async function copyShareUrl(url: string) {
  try {
    await navigator.clipboard.writeText(url)
    copiedShareUrl.value = true
    uiStore.success(t('report.linkCopied'))
    setTimeout(() => { copiedShareUrl.value = false }, 2000)
  } catch {
    // ignore
  }
}

async function handleTriggerSend() {
  const tmpl = selectedTemplate.value
  if (!tmpl) return
  try {
    await reportService.triggerScheduledSend(tmpl.id)
    uiStore.success(t('report.sendTriggered'))
  } catch {
    // ignore
  }
}

// ---- Layout badge color ----
function layoutBadgeClass(layoutType: ReportTemplate['layoutType']): string {
  switch (layoutType) {
    case 'list':
      return 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
    case 'detail':
      return 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400'
    case 'summary':
      return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400'
    default:
      return 'bg-gray-100 text-gray-600'
  }
}

function layoutLabel(layoutType: ReportTemplate['layoutType']): string {
  switch (layoutType) {
    case 'list':
      return t('report.layoutList')
    case 'detail':
      return t('report.layoutDetail')
    case 'summary':
      return t('report.layoutSummary')
    default:
      return layoutType
  }
}

// ---- Template patch handler ----
function onTemplateUpdate(patch: Partial<CreateReportRequest>) {
  if (editingTemplate.value) {
    editingTemplate.value = { ...editingTemplate.value, ...patch }
  }
}

onMounted(() => {
  loadTemplates()
})
</script>

<template>
  <DefaultLayout>
    <div class="flex h-full gap-0">
      <!-- Left panel: template list -->
      <div class="w-80 flex-shrink-0 flex flex-col border-r border-border bg-background">
        <!-- Header -->
        <div class="flex items-center justify-between p-4 border-b border-border">
          <h1 class="text-base font-semibold">{{ $t('report.title') }}</h1>
          <Button size="sm" @click="newTemplate">
            <Plus class="h-4 w-4 mr-1" />
            {{ $t('report.newReport') }}
          </Button>
        </div>

        <!-- Search -->
        <div class="p-3 border-b border-border">
          <input
            v-model="searchQuery"
            type="text"
            :placeholder="$t('common.search')"
            class="w-full rounded-lg border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring"
          />
        </div>

        <!-- Template list -->
        <div class="flex-1 overflow-y-auto">
          <!-- Loading skeleton -->
          <div v-if="loading" class="p-3 space-y-2">
            <div v-for="i in 4" :key="i" class="h-16 rounded-xl bg-muted animate-pulse" />
          </div>

          <!-- Empty state -->
          <div
            v-else-if="filteredTemplates.length === 0"
            class="flex flex-col items-center justify-center py-12 px-4 text-center"
          >
            <div class="h-12 w-12 rounded-full bg-muted flex items-center justify-center mb-3">
              <FileText class="h-6 w-6 text-muted-foreground" />
            </div>
            <p class="text-sm font-medium">{{ $t('report.noTemplates') }}</p>
            <p class="text-xs text-muted-foreground mt-1">{{ $t('report.noTemplatesHint') }}</p>
          </div>

          <!-- Items -->
          <div v-else class="p-2 space-y-1">
            <div
              v-for="tmpl in filteredTemplates"
              :key="tmpl.id"
              :class="[
                'group flex items-start gap-2 p-3 rounded-xl cursor-pointer transition-colors',
                selectedTemplate?.id === tmpl.id
                  ? 'bg-primary/10 border border-primary/30'
                  : 'hover:bg-muted/50 border border-transparent'
              ]"
              @click="selectTemplate(tmpl)"
            >
              <!-- Main info -->
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-1.5 flex-wrap">
                  <span class="text-sm font-medium truncate">{{ tmpl.name }}</span>
                  <Badge
                    variant="secondary"
                    class="text-[10px] px-1.5 py-0 shrink-0"
                    :class="layoutBadgeClass(tmpl.layoutType)"
                  >
                    {{ layoutLabel(tmpl.layoutType) }}
                  </Badge>
                  <Share2
                    v-if="tmpl.isPublic"
                    class="h-3 w-3 text-muted-foreground shrink-0"
                    :title="$t('report.isPublic')"
                  />
                </div>
                <p class="text-xs text-muted-foreground mt-0.5 truncate">
                  {{ tmpl.entityType }} &middot; {{ tmpl.module }}
                </p>
              </div>

              <!-- Delete button -->
              <Button
                variant="ghost"
                size="sm"
                class="h-7 w-7 p-0 opacity-0 group-hover:opacity-100 text-destructive hover:text-destructive shrink-0"
                :title="$t('report.deleteReport')"
                @click.stop="deleteTemplate(tmpl.id)"
              >
                <Trash2 class="h-3.5 w-3.5" />
              </Button>
            </div>
          </div>
        </div>
      </div>

      <!-- Right panel -->
      <div class="flex-1 flex flex-col min-w-0 bg-muted/20">
        <!-- Empty state: nothing selected -->
        <div
          v-if="!isEditing"
          class="flex-1 flex flex-col items-center justify-center text-center"
        >
          <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
            <FileText class="h-8 w-8 text-muted-foreground" />
          </div>
          <p class="text-base font-medium text-muted-foreground">{{ $t('report.selectOrCreate') }}</p>
        </div>

        <!-- Editor area: create or edit -->
        <template v-else>
          <!-- Tab bar -->
          <div class="flex items-center gap-0 border-b border-border bg-background px-4">
            <button
              v-for="tab in (['design', 'preview', 'share'] as const)"
              :key="tab"
              :class="[
                'px-4 py-3 text-sm font-medium border-b-2 transition-colors',
                activeTab === tab
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              ]"
              @click="activeTab = tab"
            >
              <template v-if="tab === 'design'">{{ $t('report.design') }}</template>
              <template v-else-if="tab === 'preview'">{{ $t('report.preview') }}</template>
              <template v-else>{{ $t('report.shareSchedule') }}</template>
            </button>
          </div>

          <!-- Tab content -->
          <div class="flex-1 overflow-auto p-6">
            <!-- Design tab -->
            <div v-if="activeTab === 'design'">
              <ReportEditor
                v-if="editingTemplate"
                :template="editingTemplate as CreateReportRequest"
                :available-fields="availableFields"
                :loading="saving"
                @update:template="onTemplateUpdate"
                @save="saveTemplate"
                @cancel="resetState"
              />
            </div>

            <!-- Preview tab -->
            <div v-else-if="activeTab === 'preview'">
              <ReportPreview
                v-if="editingTemplate"
                :template="editingTemplate as ReportTemplate"
                :rows="reportData"
                :loading="reportDataLoading"
                :error="reportDataError"
                @refresh="loadReportData"
                @download-pdf="handleDownloadPdf"
                @print="handlePrint"
              />
            </div>

            <!-- Share & Schedule tab -->
            <div v-else-if="activeTab === 'share'" class="max-w-2xl space-y-6">
              <!-- Share link card -->
              <Card>
                <CardContent class="p-6">
                  <div class="flex items-center gap-2 mb-4">
                    <LinkIcon class="h-5 w-5 text-primary" />
                    <h3 class="text-base font-semibold">{{ $t('report.shareLink') }}</h3>
                  </div>

                  <template v-if="shareUrl">
                    <!-- Share URL display -->
                    <div class="flex items-center gap-2">
                      <input
                        type="text"
                        :value="shareUrl"
                        readonly
                        class="flex-1 rounded-lg border border-input bg-muted px-3 py-2 text-sm font-mono select-all focus:outline-none"
                      />
                      <Button
                        variant="outline"
                        size="sm"
                        :title="$t('report.copyLink')"
                        @click="copyShareUrl(shareUrl!)"
                      >
                        <Check v-if="copiedShareUrl" class="h-4 w-4 text-emerald-500" />
                        <Copy v-else class="h-4 w-4" />
                        <span class="ml-1.5">{{ $t('report.copyLink') }}</span>
                      </Button>
                    </div>
                    <div class="mt-3">
                      <Button
                        variant="outline"
                        size="sm"
                        class="text-destructive hover:text-destructive"
                        @click="handleRevokeShare"
                      >
                        <Link2 class="h-4 w-4 mr-1.5" />
                        {{ $t('report.revokeLink') }}
                      </Button>
                    </div>
                  </template>

                  <template v-else>
                    <p class="text-sm text-muted-foreground mb-3">
                      {{ $t('report.isPublic') }}
                    </p>
                    <Button @click="handleShare" :disabled="!selectedTemplate">
                      <Share2 class="h-4 w-4 mr-1.5" />
                      {{ $t('report.generateLink') }}
                    </Button>
                  </template>
                </CardContent>
              </Card>

              <!-- Schedule card -->
              <Card v-if="selectedTemplate">
                <CardContent class="p-6">
                  <div class="flex items-center gap-2 mb-4">
                    <Clock class="h-5 w-5 text-primary" />
                    <h3 class="text-base font-semibold">{{ $t('report.scheduleShare') }}</h3>
                  </div>

                  <template v-if="selectedTemplate.scheduleCron">
                    <div class="flex items-center justify-between p-3 rounded-xl bg-muted/50 border border-border">
                      <div>
                        <p class="text-sm font-medium">{{ $t('report.cronExpression') }}</p>
                        <p class="text-xs text-muted-foreground font-mono mt-0.5">
                          {{ selectedTemplate.scheduleCron }}
                        </p>
                      </div>
                      <div class="flex items-center gap-2">
                        <Button
                          variant="outline"
                          size="sm"
                          @click="handleTriggerSend"
                        >
                          <Send class="h-3.5 w-3.5 mr-1" />
                          {{ $t('report.triggerSend') }}
                        </Button>
                      </div>
                    </div>
                    <div v-if="selectedTemplate.scheduleRecipients?.length" class="mt-3">
                      <p class="text-xs font-medium text-muted-foreground mb-1.5">{{ $t('report.recipients') }}</p>
                      <div class="flex flex-wrap gap-1.5">
                        <span
                          v-for="r in selectedTemplate.scheduleRecipients"
                          :key="r"
                          class="text-xs px-2 py-0.5 rounded-full bg-primary/10 text-primary"
                        >
                          {{ r }}
                        </span>
                      </div>
                    </div>
                  </template>

                  <template v-else>
                    <p class="text-sm text-muted-foreground">
                      {{ $t('report.cronHint') }}
                    </p>
                    <p class="text-xs text-muted-foreground mt-1">
                      {{ $t('report.cronExpression') }}: {{ $t('report.cronHint') }}
                    </p>
                  </template>
                </CardContent>
              </Card>

              <!-- Download / Print actions -->
              <Card>
                <CardContent class="p-6">
                  <div class="flex items-center gap-2 mb-4">
                    <Download class="h-5 w-5 text-primary" />
                    <h3 class="text-base font-semibold">{{ $t('report.downloadPdf') }}</h3>
                  </div>
                  <div class="flex items-center gap-3">
                    <Button variant="outline" @click="handleDownloadPdf">
                      <Download class="h-4 w-4 mr-1.5" />
                      {{ $t('report.downloadPdf') }}
                    </Button>
                    <Button variant="outline" @click="handlePrint">
                      <Printer class="h-4 w-4 mr-1.5" />
                      {{ $t('report.print') }}
                    </Button>
                    <Button
                      variant="outline"
                      :disabled="reportDataLoading"
                      @click="loadReportData"
                    >
                      <Spinner v-if="reportDataLoading" size="sm" class="mr-1" />
                      <RefreshCw v-else class="h-4 w-4 mr-1.5" />
                      {{ $t('report.refresh') }}
                    </Button>
                  </div>
                </CardContent>
              </Card>
            </div>
          </div>
        </template>
      </div>
    </div>

    <!-- Confirm dialog (inline) -->
    <div
      v-if="confirmOpen"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      @click.self="handleCancel"
    >
      <div class="bg-background rounded-2xl border border-border shadow-xl p-6 max-w-sm w-full mx-4">
        <h2 class="text-base font-semibold mb-2">{{ confirmTitle }}</h2>
        <p class="text-sm text-muted-foreground mb-6">{{ confirmDesc }}</p>
        <div class="flex items-center justify-end gap-3">
          <Button variant="outline" @click="handleCancel">{{ cancelLabel }}</Button>
          <Button
            :variant="confirmVariant === 'destructive' ? 'destructive' : 'default'"
            @click="handleConfirm"
          >
            {{ confirmLabel }}
          </Button>
        </div>
      </div>
    </div>
  </DefaultLayout>
</template>
