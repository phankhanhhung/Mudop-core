<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import WebhookDialog from '@/components/admin/WebhookDialog.vue'
import OutboxMonitor from '@/components/admin/OutboxMonitor.vue'
import { integrationService } from '@/services/integrationService'
import type { WebhookConfig, OutboxEntry, IntegrationHealth, TestDeliveryResult } from '@/services/integrationService'
import { useUiStore } from '@/stores/ui'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Spinner } from '@/components/ui/spinner'
import {
  Plug,
  Plus,
  Pencil,
  Trash2,
  Play,
  CheckCircle,
  XCircle,
  Lock,
  Loader2,
  RefreshCw
} from 'lucide-vue-next'

const { t } = useI18n()
const uiStore = useUiStore()

const webhooks = ref<WebhookConfig[]>([])
const outboxEntries = ref<OutboxEntry[]>([])
const health = ref<IntegrationHealth | null>(null)
const loading = ref(false)
const outboxLoading = ref(false)
const selectedOutboxStatus = ref<'all' | 'pending' | 'dead_letter'>('all')

// Dialog state
const showWebhookDialog = ref(false)
const editingWebhook = ref<WebhookConfig | null>(null)

// Test state: Map<webhookId, TestDeliveryResult | 'testing'>
const testResults = ref<Record<string, TestDeliveryResult | 'testing'>>({})

async function loadData() {
  loading.value = true
  try {
    const [wh, h] = await Promise.all([
      integrationService.listWebhooks(),
      integrationService.getHealth()
    ])
    webhooks.value = wh
    health.value = h
  } catch {
    // silently ignore — the UI will show empty state
  } finally {
    loading.value = false
  }
}

async function loadOutbox() {
  outboxLoading.value = true
  try {
    const statusParam = selectedOutboxStatus.value === 'all' ? 'all' : selectedOutboxStatus.value
    outboxEntries.value = await integrationService.listOutbox(statusParam)
  } catch {
    // silently ignore
  } finally {
    outboxLoading.value = false
  }
}

async function handleWebhookSaved(_webhook: WebhookConfig) {
  showWebhookDialog.value = false
  editingWebhook.value = null
  await loadData()
  uiStore.success(t('integration.webhookSaved'))
}

async function deleteWebhook(id: string) {
  if (!confirm(t('integration.deleteConfirm'))) return
  try {
    await integrationService.deleteWebhook(id)
    uiStore.success(t('integration.webhookDeleted'))
    await loadData()
  } catch {
    // ignore
  }
}

async function testWebhook(webhook: WebhookConfig) {
  testResults.value[webhook.id] = 'testing'
  try {
    const result = await integrationService.testWebhook(webhook.id)
    testResults.value[webhook.id] = result
  } catch {
    testResults.value[webhook.id] = { success: false, statusCode: 0, durationMs: 0, error: 'Request failed' }
  }
}

async function retryOutboxEntry(id: string) {
  await integrationService.retryOutboxEntry(id)
  await loadOutbox()
  uiStore.success(t('integration.outboxRetried'))
}

async function dismissOutboxEntry(id: string) {
  await integrationService.dismissOutboxEntry(id)
  await loadOutbox()
}

onMounted(() => {
  loadData()
  loadOutbox()
})

watch(selectedOutboxStatus, loadOutbox)

// Computed helpers for stats
function totalWebhooks() {
  return webhooks.value.length
}

function activeWebhooks() {
  return webhooks.value.filter(w => w.isActive).length
}

function pendingOutbox() {
  return health.value?.pendingOutboxCount ?? outboxEntries.value.filter(e => e.status === 'pending').length
}

function deadLetters() {
  return health.value?.deadLetterCount ?? outboxEntries.value.filter(e => e.status === 'dead_letter').length
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-3xl font-bold tracking-tight">{{ $t('integration.title') }}</h1>
          <p class="text-muted-foreground mt-1">{{ $t('integration.subtitle') }}</p>
        </div>
        <div class="flex items-center gap-3">
          <div class="h-10 w-10 rounded-full bg-violet-500/10 flex items-center justify-center">
            <Plug class="h-5 w-5 text-violet-500" />
          </div>
        </div>
      </div>

      <!-- Stats row (4 cards) -->
      <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('integration.totalWebhooks') }}</p>
                <p class="text-2xl font-bold mt-1">{{ totalWebhooks() }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center">
                <Plug class="h-5 w-5 text-primary" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('integration.activeWebhooks') }}</p>
                <p class="text-2xl font-bold mt-1 text-emerald-600">{{ activeWebhooks() }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-emerald-500/10 flex items-center justify-center">
                <CheckCircle class="h-5 w-5 text-emerald-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('integration.pendingOutbox') }}</p>
                <p class="text-2xl font-bold mt-1 text-amber-600">{{ pendingOutbox() }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-amber-500/10 flex items-center justify-center">
                <Loader2 class="h-5 w-5 text-amber-500" />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardContent class="p-4">
            <div class="flex items-center justify-between">
              <div>
                <p class="text-sm font-medium text-muted-foreground">{{ $t('integration.deadLetters') }}</p>
                <p class="text-2xl font-bold mt-1 text-rose-600">{{ deadLetters() }}</p>
              </div>
              <div class="h-10 w-10 rounded-full bg-rose-500/10 flex items-center justify-center">
                <XCircle class="h-5 w-5 text-rose-500" />
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      <!-- Webhooks section -->
      <div class="bg-background rounded-2xl border border-border p-6">
        <div class="flex items-center justify-between mb-4">
          <h2 class="text-lg font-semibold">{{ $t('integration.webhooks') }}</h2>
          <Button @click="showWebhookDialog = true; editingWebhook = null">
            <Plus class="mr-2 h-4 w-4" />
            {{ $t('integration.addWebhook') }}
          </Button>
        </div>

        <!-- Loading -->
        <div v-if="loading" class="flex flex-col items-center justify-center py-12">
          <Spinner size="lg" />
          <p class="text-muted-foreground mt-3 text-sm">{{ $t('common.loading') }}</p>
        </div>

        <!-- Empty -->
        <div v-else-if="webhooks.length === 0" class="flex flex-col items-center justify-center py-12 text-center">
          <div class="h-14 w-14 rounded-full bg-muted flex items-center justify-center mb-3">
            <Plug class="h-7 w-7 text-muted-foreground" />
          </div>
          <p class="text-muted-foreground text-sm max-w-sm">{{ $t('integration.noWebhooks') }}</p>
        </div>

        <!-- Webhook cards -->
        <div v-else class="space-y-3">
          <div
            v-for="wh in webhooks"
            :key="wh.id"
            class="flex items-start justify-between p-4 border border-border rounded-xl hover:bg-muted/30 transition-colors"
          >
            <!-- Left: info -->
            <div class="flex-1 min-w-0 pr-4">
              <div class="flex items-center gap-2 flex-wrap">
                <span class="font-medium text-sm truncate">{{ wh.name }}</span>
                <Badge
                  :class="wh.isActive
                    ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400'
                    : 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400'"
                  variant="secondary"
                  class="text-[10px] px-1.5 py-0"
                >
                  {{ wh.isActive ? $t('common.active') : $t('common.inactive') }}
                </Badge>
                <Lock v-if="wh.hasSecret" class="h-3.5 w-3.5 text-muted-foreground shrink-0" :title="$t('integration.webhook.hasSecret')" />
              </div>
              <p class="text-xs text-muted-foreground truncate mt-0.5">{{ wh.targetUrl }}</p>

              <!-- Event filter chips -->
              <div class="flex items-center flex-wrap gap-1 mt-2">
                <template v-if="wh.eventFilter && wh.eventFilter.length > 0">
                  <span
                    v-for="filter in wh.eventFilter"
                    :key="filter"
                    class="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-medium bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-400"
                  >
                    {{ filter }}
                  </span>
                </template>
                <span v-else class="text-xs text-muted-foreground italic">{{ $t('integration.allEvents') }}</span>
              </div>

              <!-- Test result -->
              <div v-if="testResults[wh.id]" class="mt-2 flex items-center gap-1.5 text-xs">
                <template v-if="testResults[wh.id] === 'testing'">
                  <Loader2 class="h-3.5 w-3.5 animate-spin text-muted-foreground" />
                  <span class="text-muted-foreground">Testing...</span>
                </template>
                <template v-else-if="(testResults[wh.id] as TestDeliveryResult).success">
                  <CheckCircle class="h-3.5 w-3.5 text-emerald-500" />
                  <span class="text-emerald-600">{{ $t('integration.testSuccess') }} ({{ (testResults[wh.id] as TestDeliveryResult).durationMs }}ms)</span>
                </template>
                <template v-else>
                  <XCircle class="h-3.5 w-3.5 text-rose-500" />
                  <span class="text-rose-600">{{ (testResults[wh.id] as TestDeliveryResult).error || $t('integration.testFailed') }}</span>
                </template>
              </div>
            </div>

            <!-- Right: actions -->
            <div class="flex items-center gap-1 shrink-0">
              <Button
                variant="ghost"
                size="sm"
                class="h-8 w-8 p-0"
                :title="$t('common.edit')"
                @click="editingWebhook = wh; showWebhookDialog = true"
              >
                <Pencil class="h-4 w-4" />
              </Button>
              <Button
                variant="ghost"
                size="sm"
                class="h-8 w-8 p-0"
                :title="'Test'"
                :disabled="testResults[wh.id] === 'testing'"
                @click="testWebhook(wh)"
              >
                <Play class="h-4 w-4" />
              </Button>
              <Button
                variant="ghost"
                size="sm"
                class="h-8 w-8 p-0 text-destructive hover:text-destructive"
                :title="$t('common.delete')"
                @click="deleteWebhook(wh.id)"
              >
                <Trash2 class="h-4 w-4" />
              </Button>
            </div>
          </div>
        </div>
      </div>

      <!-- Outbox section -->
      <div class="bg-background rounded-2xl border border-border p-6">
        <div class="flex items-center justify-between mb-4">
          <h2 class="text-lg font-semibold">{{ $t('integration.outbox') }}</h2>
          <div class="flex items-center gap-2">
            <button
              :class="[
                'px-3 py-1.5 rounded-lg text-sm font-medium transition-colors',
                selectedOutboxStatus === 'all'
                  ? 'bg-primary text-primary-foreground'
                  : 'hover:bg-muted text-muted-foreground'
              ]"
              @click="selectedOutboxStatus = 'all'"
            >
              {{ $t('integration.statusAll') }}
            </button>
            <button
              :class="[
                'px-3 py-1.5 rounded-lg text-sm font-medium transition-colors',
                selectedOutboxStatus === 'pending'
                  ? 'bg-amber-500 text-white'
                  : 'hover:bg-muted text-muted-foreground'
              ]"
              @click="selectedOutboxStatus = 'pending'"
            >
              {{ $t('integration.statusPending') }}
            </button>
            <button
              :class="[
                'px-3 py-1.5 rounded-lg text-sm font-medium transition-colors',
                selectedOutboxStatus === 'dead_letter'
                  ? 'bg-rose-600 text-white'
                  : 'hover:bg-muted text-muted-foreground'
              ]"
              @click="selectedOutboxStatus = 'dead_letter'"
            >
              {{ $t('integration.statusDeadLetter') }}
            </button>
            <Button
              variant="outline"
              size="sm"
              :disabled="outboxLoading"
              @click="loadOutbox"
            >
              <Spinner v-if="outboxLoading" size="sm" class="mr-1" />
              <RefreshCw v-else class="h-4 w-4 mr-1" />
              {{ $t('common.refresh') }}
            </Button>
          </div>
        </div>

        <OutboxMonitor
          :entries="outboxEntries"
          :loading="outboxLoading"
          @retry="retryOutboxEntry"
          @dismiss="dismissOutboxEntry"
          @refresh="loadOutbox"
        />
      </div>
    </div>

    <!-- WebhookDialog -->
    <WebhookDialog
      :open="showWebhookDialog"
      :webhook="editingWebhook"
      @close="showWebhookDialog = false; editingWebhook = null"
      @saved="handleWebhookSaved"
    />
  </DefaultLayout>
</template>
