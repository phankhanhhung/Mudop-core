<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import MessageBox from '@/components/smart/MessageBox.vue'
import { useMessageBox, MessageBoxActions } from '@/composables/useMessageBox'
import { ArrowLeft, MessageSquare } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()
const messageBox = useMessageBox()
const lastAction = ref<string>('')
const lastType = ref<string>('')

async function showInfo() {
  const action = await messageBox.info(
    'System Update Available',
    'A new version of the platform is available. You can update at your convenience from the Settings page.'
  )
  lastAction.value = action
  lastType.value = 'info'
}

async function showWarning() {
  const action = await messageBox.warning(
    'Unsaved Changes',
    'You have unsaved changes in the current form. Navigating away will discard all modifications.'
  )
  lastAction.value = action
  lastType.value = 'warning'
}

async function showError() {
  const action = await messageBox.error(
    'Operation Failed',
    'The server returned an unexpected error while processing your request. Please try again or contact support.'
  )
  lastAction.value = action
  lastType.value = 'error'
}

async function showSuccess() {
  const action = await messageBox.success(
    'Record Saved',
    'The sales order SO-2026-042 has been successfully created and submitted for processing.'
  )
  lastAction.value = action
  lastType.value = 'success'
}

async function showConfirm() {
  const action = await messageBox.confirm(
    'Delete Record',
    'Are you sure you want to delete this record? This action cannot be undone.'
  )
  lastAction.value = action
  lastType.value = 'confirm'
}

async function showCustomActions() {
  const action = await messageBox.show({
    type: 'warning',
    title: 'Unsaved Document',
    message: 'The document has been modified. What would you like to do with the changes?',
    actions: [
      { label: 'Save', key: 'save', variant: 'default', autoFocus: true },
      { label: 'Discard', key: 'discard', variant: 'destructive' },
      MessageBoxActions.CANCEL
    ]
  })
  lastAction.value = action
  lastType.value = 'custom'
}

async function showWithDetails() {
  const action = await messageBox.show({
    type: 'error',
    title: 'Compilation Failed',
    message: 'The BMMDL module failed to compile due to 3 validation errors.',
    details: `Error 1: SEM_ENTITY_NO_KEY at line 12, col 3
  Entity "OrderItem" must define at least one key field.

Error 2: SYM_UNRESOLVED_REF at line 28, col 15
  Type "CustomerStatus" is not defined in any imported namespace.

Error 3: DEP_CIRCULAR_ENTITY at line 45, col 1
  Circular dependency detected: Order -> Invoice -> Order`,
    actions: [
      MessageBoxActions.RETRY,
      MessageBoxActions.CLOSE
    ]
  })
  lastAction.value = action
  lastType.value = 'details'
}

function handleAction(key: string) {
  ;(messageBox as ReturnType<typeof useMessageBox> & { _handleAction: (k: string) => void })._handleAction(key)
}
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8">
      <!-- Header -->
      <div class="flex items-center gap-4">
        <Button variant="ghost" size="icon" @click="router.push('/showcase')">
          <ArrowLeft class="h-5 w-5" />
        </Button>
        <div>
          <h1 class="text-2xl font-bold tracking-tight flex items-center gap-2">
            <MessageSquare class="h-6 w-6" />
            Message Box
          </h1>
          <p class="text-muted-foreground mt-1">
            Advanced modal dialogs with message types, configurable actions, and expandable details.
          </p>
        </div>
      </div>

      <!-- Standard Message Types -->
      <Card>
        <CardHeader>
          <CardTitle>Standard Message Types</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Five built-in message types, each with a distinct icon, color scheme, and default action buttons.
          </p>
          <div class="flex flex-wrap gap-3">
            <Button variant="outline" @click="showInfo">Info</Button>
            <Button variant="outline" @click="showWarning">Warning</Button>
            <Button variant="outline" @click="showError">Error</Button>
            <Button variant="outline" @click="showSuccess">Success</Button>
            <Button variant="outline" @click="showConfirm">Confirm</Button>
          </div>
        </CardContent>
      </Card>

      <!-- Custom Actions -->
      <Card>
        <CardHeader>
          <CardTitle>Custom Actions</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Override the default action buttons with any combination of custom actions.
            Each action has a label, key, and button variant.
          </p>
          <Button variant="outline" @click="showCustomActions">
            Custom Actions (Save / Discard / Cancel)
          </Button>
        </CardContent>
      </Card>

      <!-- Expandable Details -->
      <Card>
        <CardHeader>
          <CardTitle>Expandable Details</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Optionally include a collapsible detail section for stack traces, validation errors,
            or other technical information that users can expand on demand.
          </p>
          <Button variant="outline" @click="showWithDetails">
            Error with Stack Trace
          </Button>
        </CardContent>
      </Card>

      <!-- Last Action Result -->
      <Card>
        <CardHeader>
          <CardTitle>Last Action Result</CardTitle>
        </CardHeader>
        <CardContent>
          <div v-if="lastAction || lastType" class="flex items-center gap-3">
            <span class="text-sm text-muted-foreground">Type:</span>
            <Badge variant="secondary">{{ lastType || 'none' }}</Badge>
            <span class="text-sm text-muted-foreground">Action:</span>
            <Badge :variant="lastAction ? 'default' : 'secondary'">
              {{ lastAction || '(dismissed)' }}
            </Badge>
          </div>
          <p v-else class="text-sm text-muted-foreground">
            Click any button above to see the returned action key here.
          </p>
        </CardContent>
      </Card>

      <!-- MessageBox instance -->
      <MessageBox
        :open="messageBox.isOpen.value"
        :type="messageBox.currentOptions.value?.type"
        :title="messageBox.currentOptions.value?.title ?? ''"
        :message="messageBox.currentOptions.value?.message ?? ''"
        :details="messageBox.currentOptions.value?.details"
        :actions="messageBox.currentOptions.value?.actions"
        :show-close-button="messageBox.currentOptions.value?.showCloseButton"
        @action="handleAction"
        @update:open="(v: boolean) => { if (!v) messageBox.close() }"
      />
    </div>
  </DefaultLayout>
</template>
