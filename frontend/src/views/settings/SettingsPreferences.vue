<script setup lang="ts">
import { usePreferences, type DateFormat, type NumberFormat, type ListViewMode, type AutoRefreshInterval } from '@/utils/preferences'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import { Checkbox } from '@/components/ui/checkbox'
import { SlidersHorizontal, RotateCcw } from 'lucide-vue-next'

const { preferences, updatePreference, resetPreferences } = usePreferences()

const pageSizeOptions = [10, 15, 25, 50, 100]
</script>

<template>
  <Card>
    <CardHeader>
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-2">
          <SlidersHorizontal class="h-5 w-5 text-primary" />
          <div>
            <CardTitle>{{ $t('settings.preferences.title') }}</CardTitle>
            <CardDescription>{{ $t('settings.preferences.subtitle') }}</CardDescription>
          </div>
        </div>
        <Button variant="outline" size="sm" @click="resetPreferences">
          <RotateCcw class="h-4 w-4 mr-2" />
          {{ $t('settings.preferences.reset') }}
        </Button>
      </div>
    </CardHeader>
    <CardContent class="space-y-6">
      <!-- Page Size -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.preferences.pageSize') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.preferences.pageSizeDescription') }}
          </p>
        </div>
        <Select
          :modelValue="String(preferences.pageSize)"
          @update:modelValue="updatePreference('pageSize', Number($event))"
          class="w-28"
        >
          <option v-for="size in pageSizeOptions" :key="size" :value="String(size)">
            {{ size }} {{ $t('settings.preferences.rows') }}
          </option>
        </Select>
      </div>

      <div class="border-t" />

      <!-- Date Format -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.preferences.dateFormat') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.preferences.dateFormatDescription') }}
          </p>
        </div>
        <Select
          :modelValue="preferences.dateFormat"
          @update:modelValue="updatePreference('dateFormat', $event as DateFormat)"
          class="w-44"
        >
          <option value="iso">ISO (2026-02-10)</option>
          <option value="us">US (02/10/2026)</option>
          <option value="eu">EU (10.02.2026)</option>
        </Select>
      </div>

      <div class="border-t" />

      <!-- Number Format -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.preferences.numberFormat') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.preferences.numberFormatDescription') }}
          </p>
        </div>
        <Select
          :modelValue="preferences.numberFormat"
          @update:modelValue="updatePreference('numberFormat', $event as NumberFormat)"
          class="w-44"
        >
          <option value="en">1,000.00</option>
          <option value="de">1.000,00</option>
        </Select>
      </div>

      <div class="border-t" />

      <!-- List View Mode -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.preferences.listViewMode') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.preferences.listViewModeDescription') }}
          </p>
        </div>
        <div class="flex gap-1 rounded-lg border p-1">
          <button
            class="px-3 py-1.5 rounded-md text-xs font-medium transition-colors"
            :class="preferences.listViewMode === 'compact'
              ? 'bg-primary text-primary-foreground shadow-sm'
              : 'text-muted-foreground hover:text-foreground'"
            @click="updatePreference('listViewMode', 'compact' as ListViewMode)"
          >
            {{ $t('settings.preferences.compact') }}
          </button>
          <button
            class="px-3 py-1.5 rounded-md text-xs font-medium transition-colors"
            :class="preferences.listViewMode === 'comfortable'
              ? 'bg-primary text-primary-foreground shadow-sm'
              : 'text-muted-foreground hover:text-foreground'"
            @click="updatePreference('listViewMode', 'comfortable' as ListViewMode)"
          >
            {{ $t('settings.preferences.comfortable') }}
          </button>
        </div>
      </div>

      <div class="border-t" />

      <!-- Keyboard Shortcuts -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.preferences.keyboardShortcuts') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.preferences.keyboardShortcutsDescription') }}
          </p>
        </div>
        <Checkbox
          :modelValue="preferences.keyboardShortcutsEnabled"
          @update:modelValue="updatePreference('keyboardShortcutsEnabled', $event)"
        />
      </div>

      <div class="border-t" />

      <!-- Auto Refresh -->
      <div class="flex items-center justify-between">
        <div>
          <Label class="font-medium">{{ $t('settings.preferences.autoRefresh') }}</Label>
          <p class="text-sm text-muted-foreground mt-0.5">
            {{ $t('settings.preferences.autoRefreshDescription') }}
          </p>
        </div>
        <Select
          :modelValue="preferences.autoRefreshInterval"
          @update:modelValue="updatePreference('autoRefreshInterval', $event as AutoRefreshInterval)"
          class="w-36"
        >
          <option value="off">{{ $t('settings.preferences.off') }}</option>
          <option value="30s">30 {{ $t('settings.preferences.seconds') }}</option>
          <option value="1min">1 {{ $t('settings.preferences.minute') }}</option>
          <option value="5min">5 {{ $t('settings.preferences.minutes') }}</option>
        </Select>
      </div>
    </CardContent>
  </Card>
</template>
