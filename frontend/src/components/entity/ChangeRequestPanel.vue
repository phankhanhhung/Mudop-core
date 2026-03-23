<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogClose,
} from 'radix-vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import { X, GitPullRequest, Check, XCircle } from 'lucide-vue-next'
import { commentService } from '@/services/commentService'
import type { ChangeRequest } from '@/services/commentService'
import { useUiStore } from '@/stores/ui'

const props = defineProps<{
  module: string
  entityType: string
  entityId: string
  currentUserId: string
  currentUserName: string
  entityData?: Record<string, unknown>
  canReview?: boolean
}>()

const emit = defineEmits<{
  'request-created': [req: ChangeRequest]
  'request-reviewed': [req: ChangeRequest]
}>()

const uiStore = useUiStore()

const changeRequests = ref<ChangeRequest[]>([])
const loading = ref(false)

// ---- Propose dialog ----
const showProposeDialog = ref(false)
const proposedChanges = ref<Record<string, unknown>>({})
const isSubmitting = ref(false)

// ---- Reject dialog ----
const rejectingRequestId = ref<string | null>(null)
const rejectComment = ref('')
const isRejecting = ref(false)

// ---- Load ----

async function loadChangeRequests(): Promise<void> {
  loading.value = true
  try {
    changeRequests.value = await commentService.listChangeRequests(
      props.module,
      props.entityType,
      props.entityId
    )
  } catch (e) {
    uiStore.error('Failed to load change requests', e instanceof Error ? e.message : undefined)
  } finally {
    loading.value = false
  }
}

onMounted(() => loadChangeRequests())

// ---- Propose dialog helpers ----

function openProposeDialog(): void {
  // Seed the form from current entity data
  proposedChanges.value = props.entityData ? { ...props.entityData } : {}
  // Remove OData annotations from the editable copy
  for (const key of Object.keys(proposedChanges.value)) {
    if (key.startsWith('@')) {
      delete proposedChanges.value[key]
    }
  }
  showProposeDialog.value = true
}

function closeProposeDialog(): void {
  if (!isSubmitting.value) {
    showProposeDialog.value = false
    proposedChanges.value = {}
  }
}

function getProposedKeys(): string[] {
  return Object.keys(proposedChanges.value)
}

async function submitProposal(): Promise<void> {
  isSubmitting.value = true
  try {
    const req = await commentService.createChangeRequest(
      props.module,
      props.entityType,
      props.entityId,
      proposedChanges.value
    )
    changeRequests.value.unshift(req)
    emit('request-created', req)
    showProposeDialog.value = false
    proposedChanges.value = {}
    uiStore.success('Change request submitted')
  } catch (e) {
    uiStore.error('Failed to submit change request', e instanceof Error ? e.message : undefined)
  } finally {
    isSubmitting.value = false
  }
}

// ---- Approve ----

async function approveRequest(req: ChangeRequest): Promise<void> {
  try {
    const updated = await commentService.reviewChangeRequest(
      props.module,
      props.entityType,
      props.entityId,
      req.id,
      'approve'
    )
    replaceInList(updated)
    emit('request-reviewed', updated)
    uiStore.success('Change request approved')
  } catch (e) {
    uiStore.error('Failed to approve', e instanceof Error ? e.message : undefined)
  }
}

// ---- Reject dialog ----

function openRejectDialog(requestId: string): void {
  rejectingRequestId.value = requestId
  rejectComment.value = ''
}

function closeRejectDialog(): void {
  if (!isRejecting.value) {
    rejectingRequestId.value = null
    rejectComment.value = ''
  }
}

async function submitRejection(): Promise<void> {
  if (!rejectingRequestId.value) return
  isRejecting.value = true
  try {
    const updated = await commentService.reviewChangeRequest(
      props.module,
      props.entityType,
      props.entityId,
      rejectingRequestId.value,
      'reject',
      rejectComment.value || undefined
    )
    replaceInList(updated)
    emit('request-reviewed', updated)
    rejectingRequestId.value = null
    rejectComment.value = ''
    uiStore.success('Change request rejected')
  } catch (e) {
    uiStore.error('Failed to reject', e instanceof Error ? e.message : undefined)
  } finally {
    isRejecting.value = false
  }
}

// ---- Helpers ----

function replaceInList(updated: ChangeRequest): void {
  const idx = changeRequests.value.findIndex((r) => r.id === updated.id)
  if (idx !== -1) {
    changeRequests.value[idx] = updated
  }
}

function formatRelativeTime(iso: string): string {
  const date = new Date(iso)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMin = Math.floor(diffMs / 60000)
  if (diffMin < 1) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr}h ago`
  return `${Math.floor(diffHr / 24)}d ago`
}

function statusClass(status: ChangeRequest['status']): string {
  if (status === 'approved') return 'bg-green-100 text-green-700'
  if (status === 'rejected') return 'bg-red-100 text-red-700'
  return 'bg-amber-100 text-amber-700'
}

function getDiffRows(req: ChangeRequest): Array<{ field: string; current: unknown; proposed: unknown }> {
  return Object.entries(req.proposedChanges).map(([field, proposed]) => ({
    field,
    current: props.entityData?.[field] ?? '—',
    proposed,
  }))
}

function formatCellValue(val: unknown): string {
  if (val === null || val === undefined || val === '') return '—'
  if (typeof val === 'boolean') return val ? 'true' : 'false'
  if (typeof val === 'object') return JSON.stringify(val)
  return String(val)
}
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <div class="flex items-center justify-between">
        <CardTitle class="text-base">Change Requests</CardTitle>
        <Button size="sm" variant="outline" @click="openProposeDialog">
          <GitPullRequest class="h-4 w-4 mr-1.5" />
          Propose Edit
        </Button>
      </div>
    </CardHeader>

    <CardContent>
      <!-- Loading -->
      <div v-if="loading" class="flex justify-center py-8">
        <Spinner size="md" aria-label="Loading change requests" />
      </div>

      <!-- Empty state -->
      <div
        v-else-if="changeRequests.length === 0"
        class="flex flex-col items-center justify-center py-10 text-muted-foreground"
      >
        <GitPullRequest class="h-10 w-10 mb-3 opacity-40" />
        <p class="text-sm">No change requests yet</p>
      </div>

      <!-- Request list -->
      <div v-else class="space-y-4">
        <div
          v-for="req in changeRequests"
          :key="req.id"
          class="rounded-lg border p-4 space-y-3"
        >
          <!-- Header row -->
          <div class="flex items-start justify-between gap-2">
            <div>
              <span class="text-sm font-medium">{{ req.proposedByName }}</span>
              <span class="text-xs text-muted-foreground ml-2">{{ formatRelativeTime(req.createdAt) }}</span>
            </div>
            <span
              class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium capitalize"
              :class="statusClass(req.status)"
            >
              {{ req.status }}
            </span>
          </div>

          <!-- Diff table -->
          <div class="rounded-md border overflow-hidden text-sm">
            <table class="w-full">
              <thead>
                <tr class="bg-muted/50 text-muted-foreground text-xs">
                  <th class="px-3 py-2 text-left font-medium w-1/3">Field</th>
                  <th class="px-3 py-2 text-left font-medium w-1/3">Current</th>
                  <th class="px-3 py-2 text-left font-medium w-1/3">Proposed</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in getDiffRows(req)"
                  :key="row.field"
                  class="border-t"
                >
                  <td class="px-3 py-2 text-muted-foreground font-medium">{{ row.field }}</td>
                  <td class="px-3 py-2 text-muted-foreground line-through">
                    {{ formatCellValue(row.current) }}
                  </td>
                  <td class="px-3 py-2 font-medium text-foreground">
                    {{ formatCellValue(row.proposed) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Reviewer note (if reviewed) -->
          <div
            v-if="req.status !== 'pending' && (req.reviewerName || req.reviewComment)"
            class="rounded-md bg-muted/40 px-3 py-2 text-xs text-muted-foreground space-y-0.5"
          >
            <span v-if="req.reviewerName" class="font-medium text-foreground">
              {{ req.reviewerName }}
            </span>
            <span v-if="req.reviewedAt" class="ml-1">{{ formatRelativeTime(req.reviewedAt) }}</span>
            <p v-if="req.reviewComment" class="mt-1">{{ req.reviewComment }}</p>
          </div>

          <!-- Review actions (pending only, canReview guard) -->
          <div
            v-if="req.status === 'pending' && canReview"
            class="flex items-center gap-2 pt-1"
          >
            <Button
              size="sm"
              class="bg-green-600 hover:bg-green-700 text-white"
              @click="approveRequest(req)"
            >
              <Check class="h-3.5 w-3.5 mr-1" />
              Approve
            </Button>
            <Button
              size="sm"
              variant="destructive"
              @click="openRejectDialog(req.id)"
            >
              <XCircle class="h-3.5 w-3.5 mr-1" />
              Reject
            </Button>
          </div>
        </div>
      </div>
    </CardContent>
  </Card>

  <!-- ── Propose Edit Dialog ─────────────────────────────────────────── -->
  <DialogRoot :open="showProposeDialog" @update:open="(v) => { if (!v) closeProposeDialog() }">
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-[560px] -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
      >
        <!-- Header -->
        <div class="flex items-start justify-between p-6 pb-0">
          <div class="flex items-center gap-3">
            <div class="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center text-primary">
              <GitPullRequest class="h-5 w-5" />
            </div>
            <div>
              <DialogTitle class="text-lg font-semibold text-foreground">
                Propose Edit
              </DialogTitle>
              <p class="text-sm text-muted-foreground">
                Modify field values and submit for review
              </p>
            </div>
          </div>
          <DialogClose
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
            :disabled="isSubmitting"
          >
            <X class="h-4 w-4" />
          </DialogClose>
        </div>

        <div class="p-6 space-y-4">
          <!-- Editable fields -->
          <div
            v-if="getProposedKeys().length > 0"
            class="space-y-3 max-h-96 overflow-y-auto pr-1"
          >
            <div
              v-for="key in getProposedKeys()"
              :key="key"
              class="space-y-1"
            >
              <label :for="`propose-${key}`" class="text-sm font-medium text-foreground block">
                {{ key }}
              </label>
              <input
                :id="`propose-${key}`"
                v-model="(proposedChanges as Record<string, unknown>)[key] as string"
                type="text"
                class="flex w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
                :disabled="isSubmitting"
              />
            </div>
          </div>

          <p v-else class="text-sm text-muted-foreground py-4 text-center">
            No editable fields available.
          </p>

          <!-- Footer -->
          <div class="flex justify-end gap-3 pt-2 border-t">
            <DialogClose as-child>
              <Button variant="outline" type="button" :disabled="isSubmitting">
                Cancel
              </Button>
            </DialogClose>
            <Button
              type="button"
              :disabled="isSubmitting || getProposedKeys().length === 0"
              @click="submitProposal"
            >
              <Spinner v-if="isSubmitting" size="sm" class="mr-2" />
              Submit Proposal
            </Button>
          </div>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>

  <!-- ── Reject Dialog ───────────────────────────────────────────────── -->
  <DialogRoot
    :open="rejectingRequestId !== null"
    @update:open="(v) => { if (!v) closeRejectDialog() }"
  >
    <DialogPortal>
      <DialogOverlay
        class="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
      />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-full max-w-[400px] -translate-x-1/2 -translate-y-1/2 rounded-lg border bg-background shadow-lg duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
      >
        <div class="flex items-start justify-between p-6 pb-0">
          <DialogTitle class="text-base font-semibold text-foreground">
            Reject Change Request
          </DialogTitle>
          <DialogClose
            class="rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
            :disabled="isRejecting"
          >
            <X class="h-4 w-4" />
          </DialogClose>
        </div>

        <div class="p-6 space-y-4">
          <div class="space-y-2">
            <label for="reject-comment" class="text-sm font-medium text-foreground block">
              Reason (optional)
            </label>
            <textarea
              id="reject-comment"
              v-model="rejectComment"
              rows="3"
              placeholder="Explain why this change is being rejected..."
              class="flex w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 resize-none"
              :disabled="isRejecting"
            />
          </div>

          <div class="flex justify-end gap-3 pt-2 border-t">
            <DialogClose as-child>
              <Button variant="outline" type="button" :disabled="isRejecting">
                Cancel
              </Button>
            </DialogClose>
            <Button
              variant="destructive"
              type="button"
              :disabled="isRejecting"
              @click="submitRejection"
            >
              <Spinner v-if="isRejecting" size="sm" class="mr-2" />
              Confirm Rejection
            </Button>
          </div>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
