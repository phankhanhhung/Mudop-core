<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { usePluginRegistry, type PluginInfo } from '@/plugins'
import { useConfirmDialog } from '@/composables/useConfirmDialog'
import { useUiStore } from '@/stores/ui'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Spinner } from '@/components/ui/spinner'
import { Input } from '@/components/ui/input'
import { ConfirmDialog } from '@/components/common'
import PluginSettingsForm from '@/plugins/components/PluginSettingsForm.vue'
import { pluginService, type PluginStagingResponse } from '@/services/pluginService'
import {
  Plug,
  RefreshCw,
  Search,
  Download,
  Power,
  PowerOff,
  Trash2,
  Settings,
  AlertCircle,
  CheckCircle,
  XCircle,
  X,
  Package,
  Link2,
  Upload,
  Shield,
  ShieldCheck,
  ShieldAlert,
  Clock,
  FileText,
  ChevronDown,
  ChevronRight,
} from 'lucide-vue-next'

const uiStore = useUiStore()
const confirmDialog = useConfirmDialog()

const {
  loadPlugins,
  installPlugin,
  enablePlugin,
  disablePlugin,
  uninstallPlugin,
} = usePluginRegistry()

const plugins = ref<PluginInfo[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)
const actionLoading = ref<string | null>(null)
const searchQuery = ref('')

// Settings panel
const settingsPlugin = ref<PluginInfo | null>(null)

// Staging
const stagedPlugins = ref<PluginStagingResponse[]>([])
const isUploading = ref(false)
const stagingLoading = ref<number | null>(null)
const expandedStaging = ref<number | null>(null)
const fileInputRef = ref<HTMLInputElement | null>(null)

// Stats
const stats = computed(() => ({
  total: plugins.value.length,
  enabled: plugins.value.filter(p => p.status === 'enabled').length,
  installed: plugins.value.filter(p => p.status === 'installed').length,
  disabled: plugins.value.filter(p => p.status === 'disabled').length,
  available: plugins.value.filter(p => p.status === 'available').length,
}))

// Filtered plugins
const filteredPlugins = computed(() => {
  if (!searchQuery.value.trim()) return plugins.value
  const q = searchQuery.value.toLowerCase()
  return plugins.value.filter(p =>
    p.name.toLowerCase().includes(q) ||
    (p.description?.toLowerCase().includes(q)) ||
    p.capabilities.some(c => c.toLowerCase().includes(q))
  )
})

// Status badge styling
function getStatusVariant(status: PluginInfo['status']): string {
  switch (status) {
    case 'enabled': return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400'
    case 'installed': return 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
    case 'disabled': return 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400'
    case 'available': return 'border border-dashed border-gray-300 text-gray-500 dark:border-gray-600 dark:text-gray-400'
    default: return ''
  }
}

function getStatusLabel(status: PluginInfo['status']): string {
  switch (status) {
    case 'enabled': return 'Enabled'
    case 'installed': return 'Installed'
    case 'disabled': return 'Disabled'
    case 'available': return 'Available'
    default: return status
  }
}

function getPluginColor(plugin: PluginInfo): string {
  const colors = [
    'bg-blue-500', 'bg-emerald-500', 'bg-violet-500', 'bg-amber-500',
    'bg-rose-500', 'bg-cyan-500', 'bg-indigo-500', 'bg-teal-500',
    'bg-pink-500', 'bg-orange-500',
  ]
  let hash = 0
  for (const char of plugin.name) {
    hash = char.charCodeAt(0) + ((hash << 5) - hash)
  }
  return colors[Math.abs(hash) % colors.length]
}

function getPluginInitials(plugin: PluginInfo): string {
  const words = plugin.name.replace(/([A-Z])/g, ' $1').trim().split(/\s+/)
  if (words.length >= 2) {
    return (words[0][0] + words[1][0]).toUpperCase()
  }
  return plugin.name.substring(0, 2).toUpperCase()
}

// Actions
async function fetchPlugins() {
  isLoading.value = true
  error.value = null
  try {
    plugins.value = await loadPlugins()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load plugins'
  } finally {
    isLoading.value = false
  }
}

async function handleInstall(plugin: PluginInfo) {
  actionLoading.value = plugin.name
  try {
    await installPlugin(plugin.name)
    uiStore.success('Plugin Installed', `${plugin.name} has been installed successfully.`)
    await fetchPlugins()
  } catch (e) {
    uiStore.error('Installation Failed', e instanceof Error ? e.message : 'Failed to install plugin')
  } finally {
    actionLoading.value = null
  }
}

async function handleEnable(plugin: PluginInfo) {
  actionLoading.value = plugin.name
  try {
    await enablePlugin(plugin.name)
    uiStore.success('Plugin Enabled', `${plugin.name} has been enabled.`)
    await fetchPlugins()
  } catch (e) {
    uiStore.error('Enable Failed', e instanceof Error ? e.message : 'Failed to enable plugin')
  } finally {
    actionLoading.value = null
  }
}

async function handleDisable(plugin: PluginInfo) {
  // Show dependency warning
  if (plugin.dependents.length > 0) {
    const confirmed = await confirmDialog.confirm({
      title: 'Disable Plugin',
      description: `Disabling "${plugin.name}" may affect dependent plugins: ${plugin.dependents.join(', ')}. Are you sure?`,
      confirmLabel: 'Disable',
      variant: 'destructive',
    })
    if (!confirmed) return
  }

  actionLoading.value = plugin.name
  try {
    await disablePlugin(plugin.name)
    uiStore.success('Plugin Disabled', `${plugin.name} has been disabled.`)
    await fetchPlugins()
  } catch (e) {
    uiStore.error('Disable Failed', e instanceof Error ? e.message : 'Failed to disable plugin')
  } finally {
    actionLoading.value = null
  }
}

async function handleUninstall(plugin: PluginInfo) {
  const confirmed = await confirmDialog.confirm({
    title: 'Uninstall Plugin',
    description: `Are you sure you want to uninstall "${plugin.name}"? This will remove all plugin data and cannot be undone.`,
    confirmLabel: 'Uninstall',
    variant: 'destructive',
  })
  if (!confirmed) return

  actionLoading.value = plugin.name
  try {
    await uninstallPlugin(plugin.name)
    uiStore.success('Plugin Uninstalled', `${plugin.name} has been uninstalled.`)
    await fetchPlugins()
  } catch (e) {
    uiStore.error('Uninstall Failed', e instanceof Error ? e.message : 'Failed to uninstall plugin')
  } finally {
    actionLoading.value = null
  }
}

function openSettings(plugin: PluginInfo) {
  settingsPlugin.value = plugin
}

function closeSettings() {
  settingsPlugin.value = null
}

async function handleSettingsSaved() {
  closeSettings()
  await fetchPlugins()
}

// Staging methods
async function fetchStagedPlugins() {
  try {
    stagedPlugins.value = await pluginService.listStagedPlugins()
  } catch (e) {
    // Silently fail if staging is not available
    stagedPlugins.value = []
  }
}

function triggerUpload() {
  fileInputRef.value?.click()
}

async function handleFileUpload(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return

  isUploading.value = true
  try {
    const staged = await pluginService.uploadPlugin(file)
    uiStore.success('Plugin Uploaded', `${staged.name} v${staged.version} uploaded and validated.`)
    await fetchStagedPlugins()
  } catch (e) {
    uiStore.error('Upload Failed', e instanceof Error ? e.message : 'Failed to upload plugin')
  } finally {
    isUploading.value = false
    target.value = '' // Reset file input
  }
}

async function handleRevalidate(staging: PluginStagingResponse) {
  stagingLoading.value = staging.id
  try {
    await pluginService.revalidateStagedPlugin(staging.id)
    uiStore.success('Revalidation Complete', `${staging.name} has been revalidated.`)
    await fetchStagedPlugins()
  } catch (e) {
    uiStore.error('Revalidation Failed', e instanceof Error ? e.message : 'Failed to revalidate')
  } finally {
    stagingLoading.value = null
  }
}

async function handleApprove(staging: PluginStagingResponse) {
  const confirmed = await confirmDialog.confirm({
    title: 'Approve Plugin',
    description: `Approve "${staging.name}" v${staging.version}? This will load the plugin into the system. You can then install and enable it.`,
    confirmLabel: 'Approve',
  })
  if (!confirmed) return

  stagingLoading.value = staging.id
  try {
    const result = await pluginService.approveStagedPlugin(staging.id)
    uiStore.success('Plugin Approved', `${result.name} loaded with ${result.featureCount} feature(s).`)
    await fetchStagedPlugins()
    await fetchPlugins()
  } catch (e) {
    uiStore.error('Approval Failed', e instanceof Error ? e.message : 'Failed to approve plugin')
  } finally {
    stagingLoading.value = null
  }
}

async function handleReject(staging: PluginStagingResponse) {
  const confirmed = await confirmDialog.confirm({
    title: 'Reject Plugin',
    description: `Reject "${staging.name}" v${staging.version}? This will remove the uploaded files.`,
    confirmLabel: 'Reject',
    variant: 'destructive',
  })
  if (!confirmed) return

  stagingLoading.value = staging.id
  try {
    await pluginService.rejectStagedPlugin(staging.id)
    uiStore.success('Plugin Rejected', `${staging.name} has been rejected and cleaned up.`)
    await fetchStagedPlugins()
  } catch (e) {
    uiStore.error('Rejection Failed', e instanceof Error ? e.message : 'Failed to reject plugin')
  } finally {
    stagingLoading.value = null
  }
}

function toggleStagingDetails(id: number) {
  expandedStaging.value = expandedStaging.value === id ? null : id
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function getStagingStatusVariant(status: string): string {
  switch (status) {
    case 'valid': return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400'
    case 'invalid': return 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400'
    case 'pending': return 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400'
    case 'approved': return 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
    default: return 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400'
  }
}

onMounted(() => {
  fetchPlugins()
  fetchStagedPlugins()
})
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">Plugin Management</h1>
          <p class="text-muted-foreground mt-1">
            Install, configure, and manage platform plugins.
          </p>
        </div>
        <div class="flex gap-2">
          <input
            ref="fileInputRef"
            type="file"
            accept=".zip"
            class="hidden"
            @change="handleFileUpload"
          />
          <Button variant="default" size="sm" @click="triggerUpload" :disabled="isUploading">
            <Spinner v-if="isUploading" size="sm" class="mr-2" />
            <Upload v-else class="mr-2 h-4 w-4" />
            Upload Plugin
          </Button>
          <Button variant="outline" size="sm" @click="fetchPlugins(); fetchStagedPlugins()" :disabled="isLoading">
            <Spinner v-if="isLoading" size="sm" class="mr-2" />
            <RefreshCw v-else class="mr-2 h-4 w-4" />
            Refresh
          </Button>
        </div>
      </div>

      <!-- Error -->
      <Alert v-if="error" variant="destructive">
        <AlertCircle class="h-4 w-4" />
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <!-- Stats Cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">Total Plugins</p>
                <p class="text-2xl font-bold mt-1">{{ stats.total }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Package class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">Enabled</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ stats.enabled }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <CheckCircle class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">Disabled</p>
                <p class="text-2xl font-bold mt-1 text-gray-500">{{ stats.disabled }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-gray-500/10 flex items-center justify-center">
                <XCircle class="h-5 w-5 text-gray-400" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card class="transition-all hover:shadow-md">
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">Available</p>
                <p class="text-2xl font-bold mt-1 text-blue-600">{{ stats.available }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-blue-500/10 flex items-center justify-center">
                <Download class="h-5 w-5 text-blue-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Staging Area -->
      <Card v-if="stagedPlugins.length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <Shield class="h-5 w-5 text-amber-500" />
            <CardTitle>Staging Area</CardTitle>
            <Badge variant="secondary">{{ stagedPlugins.length }}</Badge>
          </div>
          <CardDescription>
            Uploaded plugins awaiting review and approval before installation.
          </CardDescription>
        </CardHeader>
        <CardContent class="p-0">
          <div class="divide-y">
            <div
              v-for="staging in stagedPlugins"
              :key="staging.id"
              class="px-5 py-4"
            >
              <!-- Staging item header -->
              <div class="flex items-center gap-4">
                <!-- Icon -->
                <div class="h-10 w-10 rounded-lg flex items-center justify-center shrink-0"
                  :class="staging.validationStatus === 'valid'
                    ? 'bg-emerald-100 dark:bg-emerald-900/30'
                    : staging.validationStatus === 'invalid'
                    ? 'bg-red-100 dark:bg-red-900/30'
                    : 'bg-amber-100 dark:bg-amber-900/30'"
                >
                  <ShieldCheck v-if="staging.validationStatus === 'valid'" class="h-5 w-5 text-emerald-600" />
                  <ShieldAlert v-else-if="staging.validationStatus === 'invalid'" class="h-5 w-5 text-red-600" />
                  <Clock v-else class="h-5 w-5 text-amber-600" />
                </div>

                <!-- Info -->
                <div class="flex-1 min-w-0">
                  <div class="flex items-center gap-2 flex-wrap">
                    <span class="font-medium">{{ staging.name }}</span>
                    <span
                      class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium"
                      :class="getStagingStatusVariant(staging.validationStatus)"
                    >
                      {{ staging.validationStatus }}
                    </span>
                    <Badge variant="outline" class="shrink-0">v{{ staging.version }}</Badge>
                  </div>
                  <div class="flex items-center gap-3 mt-1 text-xs text-muted-foreground">
                    <span class="flex items-center gap-1">
                      <FileText class="h-3 w-3" />
                      {{ staging.fileName }}
                    </span>
                    <span>{{ formatFileSize(staging.fileSize) }}</span>
                    <span v-if="staging.author">by {{ staging.author }}</span>
                    <span>
                      {{ staging.validationResults.filter(r => r.passed).length }}/{{ staging.validationResults.length }} checks passed
                    </span>
                  </div>
                </div>

                <!-- Actions -->
                <div class="flex items-center gap-2 shrink-0">
                  <Spinner v-if="stagingLoading === staging.id" size="sm" />
                  <template v-else>
                    <!-- Expand details -->
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0"
                      title="View validation details"
                      @click="toggleStagingDetails(staging.id)"
                    >
                      <ChevronDown v-if="expandedStaging === staging.id" class="h-4 w-4" />
                      <ChevronRight v-else class="h-4 w-4" />
                    </Button>

                    <!-- Revalidate -->
                    <Button
                      variant="outline"
                      size="sm"
                      title="Re-run validation"
                      @click="handleRevalidate(staging)"
                    >
                      <RefreshCw class="mr-1.5 h-3.5 w-3.5" />
                      Validate
                    </Button>

                    <!-- Approve -->
                    <Button
                      v-if="staging.validationStatus === 'valid'"
                      variant="default"
                      size="sm"
                      @click="handleApprove(staging)"
                    >
                      <CheckCircle class="mr-1.5 h-3.5 w-3.5" />
                      Approve
                    </Button>

                    <!-- Reject -->
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8 w-8 p-0 text-destructive hover:text-destructive"
                      title="Reject"
                      @click="handleReject(staging)"
                    >
                      <Trash2 class="h-4 w-4" />
                    </Button>
                  </template>
                </div>
              </div>

              <!-- Expanded validation details -->
              <div v-if="expandedStaging === staging.id" class="mt-4 ml-14">
                <div class="rounded-lg border bg-muted/30 p-4 space-y-2">
                  <h4 class="text-sm font-semibold mb-2">Validation Results</h4>
                  <div
                    v-for="(result, idx) in staging.validationResults"
                    :key="idx"
                    class="flex items-start gap-2 text-sm"
                  >
                    <CheckCircle v-if="result.passed && result.severity === 'info'" class="h-4 w-4 text-emerald-500 mt-0.5 shrink-0" />
                    <AlertCircle v-else-if="result.passed && result.severity === 'warning'" class="h-4 w-4 text-amber-500 mt-0.5 shrink-0" />
                    <XCircle v-else class="h-4 w-4 text-red-500 mt-0.5 shrink-0" />
                    <div class="flex-1 min-w-0">
                      <span class="font-medium text-xs text-muted-foreground mr-2">{{ result.checkName }}</span>
                      <span>{{ result.message }}</span>
                      <p v-if="result.details" class="text-xs text-muted-foreground mt-0.5 font-mono whitespace-pre-wrap">{{ result.details }}</p>
                    </div>
                  </div>
                  <div class="mt-3 pt-2 border-t text-xs text-muted-foreground">
                    SHA-256: <code class="font-mono">{{ staging.fileHash }}</code>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Plugin List -->
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Plug class="h-5 w-5" />
              <CardTitle>Plugins</CardTitle>
            </div>
            <!-- Search -->
            <div class="relative w-64">
              <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                v-model="searchQuery"
                placeholder="Search plugins..."
                class="pl-9"
              />
            </div>
          </div>
          <CardDescription>
            Manage platform plugins, their lifecycle, and configuration.
          </CardDescription>
        </CardHeader>
        <CardContent class="p-0">
          <!-- Loading -->
          <div v-if="isLoading" class="flex flex-col items-center justify-center py-16" role="status" aria-label="Loading plugins">
            <Spinner size="lg" />
            <p class="text-muted-foreground mt-3 text-sm">Loading plugins...</p>
          </div>

          <!-- Empty state -->
          <div v-else-if="filteredPlugins.length === 0" class="px-6 pb-6">
            <Card class="border-dashed">
              <CardContent class="flex flex-col items-center justify-center py-16">
                <div class="h-16 w-16 rounded-full bg-muted flex items-center justify-center mb-4">
                  <Plug class="h-8 w-8 text-muted-foreground" />
                </div>
                <h3 class="text-lg font-semibold mb-1">No plugins found</h3>
                <p class="text-muted-foreground text-sm mb-4 text-center max-w-sm">
                  {{ searchQuery ? 'No plugins match your search.' : 'No plugins are available in the system.' }}
                </p>
              </CardContent>
            </Card>
          </div>

          <!-- Plugin cards -->
          <div v-else class="divide-y">
            <div
              v-for="plugin in filteredPlugins"
              :key="plugin.name"
              class="flex items-center gap-4 px-5 py-4 hover:bg-muted/50 transition-colors group"
            >
              <!-- Plugin icon -->
              <div
                class="h-12 w-12 rounded-lg flex items-center justify-center text-white text-sm font-medium shrink-0"
                :class="getPluginColor(plugin)"
              >
                {{ getPluginInitials(plugin) }}
              </div>

              <!-- Plugin info -->
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-2 flex-wrap">
                  <span class="font-medium">{{ plugin.name }}</span>
                  <!-- Status badge -->
                  <span
                    class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium"
                    :class="getStatusVariant(plugin.status)"
                  >
                    {{ getStatusLabel(plugin.status) }}
                  </span>
                  <Badge v-if="plugin.version" variant="outline" class="shrink-0">
                    v{{ plugin.version }}
                  </Badge>
                </div>
                <p v-if="plugin.description" class="text-sm text-muted-foreground mt-0.5 truncate">
                  {{ plugin.description }}
                </p>
                <div class="flex items-center gap-2 mt-1.5 flex-wrap">
                  <!-- Capabilities tags -->
                  <Badge
                    v-for="cap in plugin.capabilities.slice(0, 4)"
                    :key="cap"
                    variant="secondary"
                    class="text-xs"
                  >
                    {{ cap }}
                  </Badge>
                  <span v-if="plugin.capabilities.length > 4" class="text-xs text-muted-foreground">
                    +{{ plugin.capabilities.length - 4 }} more
                  </span>
                  <!-- Dependencies -->
                  <span v-if="plugin.dependsOn.length > 0" class="flex items-center gap-1 text-xs text-muted-foreground ml-2">
                    <Link2 class="h-3 w-3" />
                    Depends on: {{ plugin.dependsOn.join(', ') }}
                  </span>
                </div>
              </div>

              <!-- Action buttons -->
              <div class="flex items-center gap-2 shrink-0">
                <Spinner v-if="actionLoading === plugin.name" size="sm" />
                <template v-else>
                  <!-- Install (available) -->
                  <Button
                    v-if="plugin.status === 'available'"
                    variant="default"
                    size="sm"
                    @click="handleInstall(plugin)"
                  >
                    <Download class="mr-1.5 h-3.5 w-3.5" />
                    Install
                  </Button>

                  <!-- Enable (installed or disabled) -->
                  <Button
                    v-if="plugin.status === 'installed' || plugin.status === 'disabled'"
                    variant="default"
                    size="sm"
                    @click="handleEnable(plugin)"
                  >
                    <Power class="mr-1.5 h-3.5 w-3.5" />
                    Enable
                  </Button>

                  <!-- Disable (enabled) -->
                  <Button
                    v-if="plugin.status === 'enabled'"
                    variant="outline"
                    size="sm"
                    @click="handleDisable(plugin)"
                  >
                    <PowerOff class="mr-1.5 h-3.5 w-3.5" />
                    Disable
                  </Button>

                  <!-- Settings (if has settings) -->
                  <Button
                    v-if="plugin.settings && (plugin.status === 'enabled' || plugin.status === 'installed')"
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0"
                    title="Settings"
                    @click="openSettings(plugin)"
                  >
                    <Settings class="h-4 w-4" />
                  </Button>

                  <!-- Uninstall (disabled only) -->
                  <Button
                    v-if="plugin.status === 'disabled'"
                    variant="ghost"
                    size="sm"
                    class="h-8 w-8 p-0 text-destructive hover:text-destructive"
                    title="Uninstall"
                    @click="handleUninstall(plugin)"
                  >
                    <Trash2 class="h-4 w-4" />
                  </Button>
                </template>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Settings Panel (modal-style slide-over) -->
      <transition
        enter-active-class="transition-opacity duration-200 ease-out"
        leave-active-class="transition-opacity duration-150 ease-in"
        enter-from-class="opacity-0"
        enter-to-class="opacity-100"
        leave-from-class="opacity-100"
        leave-to-class="opacity-0"
      >
        <div v-if="settingsPlugin" class="fixed inset-0 z-50 flex items-start justify-center pt-20">
          <!-- Backdrop -->
          <div class="absolute inset-0 bg-black/50" @click="closeSettings" />
          <!-- Panel -->
          <Card class="relative z-10 w-full max-w-lg mx-4 max-h-[80vh] overflow-y-auto">
            <CardHeader>
              <div class="flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <Settings class="h-5 w-5" />
                  <CardTitle>{{ settingsPlugin.name }} Settings</CardTitle>
                </div>
                <Button variant="ghost" size="sm" class="h-8 w-8 p-0" @click="closeSettings">
                  <X class="h-4 w-4" />
                </Button>
              </div>
            </CardHeader>
            <CardContent>
              <PluginSettingsForm
                v-if="settingsPlugin.settings"
                :pluginName="settingsPlugin.name"
                :schema="settingsPlugin.settings.schema"
                :initialValues="settingsPlugin.settings.values"
                @saved="handleSettingsSaved"
              />
            </CardContent>
          </Card>
        </div>
      </transition>

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
    </div>
  </DefaultLayout>
</template>
