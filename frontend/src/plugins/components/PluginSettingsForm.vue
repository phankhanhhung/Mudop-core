<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { usePluginRegistry, type PluginSettingsSchema } from '@/plugins'
import { useUiStore } from '@/stores/ui'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Label } from '@/components/ui/label'
import { Checkbox } from '@/components/ui/checkbox'
import { Select } from '@/components/ui/select'
import { Save, AlertCircle } from 'lucide-vue-next'

const props = defineProps<{
  pluginName: string
  schema: PluginSettingsSchema
  initialValues: Record<string, unknown>
}>()

const emit = defineEmits<{
  saved: []
}>()

const uiStore = useUiStore()
const { updatePluginSettings } = usePluginRegistry()

const formValues = ref<Record<string, unknown>>({})
const isSaving = ref(false)
const error = ref<string | null>(null)

onMounted(() => {
  // Initialize form with initial values, falling back to defaults
  const values: Record<string, unknown> = {}
  for (const setting of props.schema.settings) {
    values[setting.key] = props.initialValues[setting.key] ?? setting.defaultValue
  }
  formValues.value = values
})

async function saveSettings() {
  // Validate required fields
  for (const setting of props.schema.settings) {
    if (setting.required && (formValues.value[setting.key] === undefined || formValues.value[setting.key] === null || formValues.value[setting.key] === '')) {
      error.value = `"${setting.label}" is required.`
      return
    }
  }

  isSaving.value = true
  error.value = null

  try {
    await updatePluginSettings(props.pluginName, formValues.value)
    uiStore.success('Settings Saved', `Settings for ${props.pluginName} have been updated.`)
    emit('saved')
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to save settings'
  } finally {
    isSaving.value = false
  }
}
</script>

<template>
  <div class="space-y-5">
    <h3 class="text-lg font-semibold">{{ schema.groupLabel }}</h3>

    <Alert v-if="error" variant="destructive">
      <AlertCircle class="h-4 w-4" />
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-for="setting in schema.settings" :key="setting.key" class="space-y-1.5">
      <Label :for="`setting-${setting.key}`">
        {{ setting.label }}
        <span v-if="setting.required" class="text-red-500 ml-0.5">*</span>
      </Label>
      <p v-if="setting.description" class="text-xs text-muted-foreground">
        {{ setting.description }}
      </p>

      <!-- Boolean: checkbox -->
      <div v-if="setting.type === 'boolean'" class="flex items-center gap-3">
        <Checkbox
          :id="`setting-${setting.key}`"
          :model-value="!!formValues[setting.key]"
          @update:model-value="formValues[setting.key] = $event"
        />
        <label :for="`setting-${setting.key}`" class="text-sm cursor-pointer">
          {{ formValues[setting.key] ? 'Enabled' : 'Disabled' }}
        </label>
      </div>

      <!-- Integer: number input -->
      <Input
        v-else-if="setting.type === 'integer'"
        :id="`setting-${setting.key}`"
        type="number"
        :model-value="formValues[setting.key] as number"
        @update:model-value="formValues[setting.key] = Number($event)"
      />

      <!-- String: text input -->
      <Input
        v-else-if="setting.type === 'string'"
        :id="`setting-${setting.key}`"
        type="text"
        :model-value="formValues[setting.key] as string"
        @update:model-value="formValues[setting.key] = $event"
      />

      <!-- Select: dropdown -->
      <Select
        v-else-if="setting.type === 'select'"
        :id="`setting-${setting.key}`"
        :model-value="formValues[setting.key] as string"
        @update:model-value="formValues[setting.key] = $event"
      >
        <option v-for="opt in setting.options" :key="opt" :value="opt">
          {{ opt }}
        </option>
      </Select>
    </div>

    <div class="pt-2">
      <Button @click="saveSettings" :disabled="isSaving">
        <Spinner v-if="isSaving" size="sm" class="mr-2" />
        <Save v-else class="mr-2 h-4 w-4" />
        Save Settings
      </Button>
    </div>
  </div>
</template>
