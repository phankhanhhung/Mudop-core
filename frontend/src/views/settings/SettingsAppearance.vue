<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useUiStore, type Theme } from '@/stores/ui'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Checkbox } from '@/components/ui/checkbox'
import { SUPPORTED_LOCALES, applyLocaleDirection } from '@/i18n'
import { Palette, Globe, Monitor, Moon, Sun, Check } from 'lucide-vue-next'

const { locale } = useI18n()
const uiStore = useUiStore()

function setTheme(newTheme: Theme) {
  uiStore.setTheme(newTheme)
}

async function handleLocaleChange(event: Event) {
  const target = event.target as HTMLSelectElement
  const newLocale = target.value

  if (newLocale !== 'en') {
    try {
      const messages = await import(`@/locales/${newLocale}.json`)
      const { default: i18n } = await import('@/i18n')
      i18n.global.setLocaleMessage(newLocale, messages.default)
    } catch {
      // Fallback to English if locale file not found
    }
  }

  locale.value = newLocale
  localStorage.setItem('locale', newLocale)
  applyLocaleDirection(newLocale)
}
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center gap-2">
        <Palette class="h-5 w-5 text-primary" />
        <CardTitle>{{ $t('settings.appearance.title') }}</CardTitle>
      </div>
      <CardDescription>{{ $t('settings.appearance.subtitle') }}</CardDescription>
    </CardHeader>
    <CardContent class="space-y-6">
      <!-- Theme selection cards -->
      <div>
        <Label class="text-xs font-medium text-muted-foreground uppercase tracking-wider mb-3 block">
          {{ $t('settings.appearance.theme') }}
        </Label>
        <div class="grid grid-cols-3 gap-3">
          <!-- Light -->
          <button
            class="group relative rounded-xl border-2 p-4 transition-all hover:shadow-md text-left"
            :class="uiStore.theme === 'light' ? 'border-primary bg-primary/5 shadow-sm' : 'border-border hover:border-muted-foreground/30'"
            @click="setTheme('light')"
          >
            <div class="flex items-center gap-3 mb-3">
              <div class="h-10 w-10 rounded-lg bg-amber-100 flex items-center justify-center">
                <Sun class="h-5 w-5 text-amber-600" />
              </div>
              <span class="font-medium text-sm">{{ $t('settings.appearance.light') }}</span>
            </div>
            <!-- Theme preview -->
            <div class="rounded-lg border bg-white p-2 space-y-1.5">
              <div class="h-1.5 w-3/4 rounded bg-gray-200" />
              <div class="h-1.5 w-1/2 rounded bg-gray-200" />
              <div class="h-1.5 w-2/3 rounded bg-gray-100" />
            </div>
            <div v-if="uiStore.theme === 'light'" class="absolute top-2 right-2">
              <div class="h-5 w-5 rounded-full bg-primary flex items-center justify-center">
                <Check class="h-3 w-3 text-primary-foreground" />
              </div>
            </div>
          </button>

          <!-- Dark -->
          <button
            class="group relative rounded-xl border-2 p-4 transition-all hover:shadow-md text-left"
            :class="uiStore.theme === 'dark' ? 'border-primary bg-primary/5 shadow-sm' : 'border-border hover:border-muted-foreground/30'"
            @click="setTheme('dark')"
          >
            <div class="flex items-center gap-3 mb-3">
              <div class="h-10 w-10 rounded-lg bg-indigo-100 dark:bg-indigo-900/30 flex items-center justify-center">
                <Moon class="h-5 w-5 text-indigo-600 dark:text-indigo-400" />
              </div>
              <span class="font-medium text-sm">{{ $t('settings.appearance.dark') }}</span>
            </div>
            <div class="rounded-lg border border-gray-700 bg-gray-900 p-2 space-y-1.5">
              <div class="h-1.5 w-3/4 rounded bg-gray-700" />
              <div class="h-1.5 w-1/2 rounded bg-gray-700" />
              <div class="h-1.5 w-2/3 rounded bg-gray-800" />
            </div>
            <div v-if="uiStore.theme === 'dark'" class="absolute top-2 right-2">
              <div class="h-5 w-5 rounded-full bg-primary flex items-center justify-center">
                <Check class="h-3 w-3 text-primary-foreground" />
              </div>
            </div>
          </button>

          <!-- System -->
          <button
            class="group relative rounded-xl border-2 p-4 transition-all hover:shadow-md text-left"
            :class="uiStore.theme === 'system' ? 'border-primary bg-primary/5 shadow-sm' : 'border-border hover:border-muted-foreground/30'"
            @click="setTheme('system')"
          >
            <div class="flex items-center gap-3 mb-3">
              <div class="h-10 w-10 rounded-lg bg-slate-100 dark:bg-slate-800 flex items-center justify-center">
                <Monitor class="h-5 w-5 text-slate-600 dark:text-slate-400" />
              </div>
              <span class="font-medium text-sm">{{ $t('settings.appearance.system') }}</span>
            </div>
            <div class="rounded-lg border overflow-hidden flex">
              <div class="flex-1 bg-white p-2 space-y-1.5">
                <div class="h-1.5 w-3/4 rounded bg-gray-200" />
                <div class="h-1.5 w-1/2 rounded bg-gray-100" />
              </div>
              <div class="flex-1 bg-gray-900 p-2 space-y-1.5">
                <div class="h-1.5 w-3/4 rounded bg-gray-700" />
                <div class="h-1.5 w-1/2 rounded bg-gray-800" />
              </div>
            </div>
            <div v-if="uiStore.theme === 'system'" class="absolute top-2 right-2">
              <div class="h-5 w-5 rounded-full bg-primary flex items-center justify-center">
                <Check class="h-3 w-3 text-primary-foreground" />
              </div>
            </div>
          </button>
        </div>
      </div>

      <div class="border-t pt-5" />

      <!-- Sidebar preference -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.appearance.sidebarCollapsed') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.appearance.sidebarDescription') }}
          </p>
        </div>
        <Checkbox
          :modelValue="uiStore.sidebarCollapsed"
          @update:modelValue="uiStore.setSidebarCollapsed($event)"
        />
      </div>

      <div class="border-t pt-5" />

      <!-- Language -->
      <div class="flex items-center justify-between">
        <div>
          <Label for="locale" class="font-medium flex items-center gap-2">
            <Globe class="h-4 w-4 text-muted-foreground" />
            {{ $t('settings.language.label') }}
          </Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.language.description') }}
          </p>
        </div>
        <Select
          id="locale"
          :modelValue="locale"
          @change="handleLocaleChange"
          class="w-44"
        >
          <option v-for="loc in SUPPORTED_LOCALES" :key="loc.code" :value="loc.code">
            {{ loc.name }}
          </option>
        </Select>
      </div>
    </CardContent>
  </Card>
</template>
