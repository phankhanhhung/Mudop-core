<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import OverflowToolbar, { type ToolbarItem } from '@/components/smart/OverflowToolbar.vue'
import { ArrowLeft, PanelTop, Plus, Save, Trash2, Copy, Scissors, ClipboardPaste, Undo, Redo, Search, Filter, Download, Upload, Settings, Share2 } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Standard Toolbar ──────────────────────────────────────────
const lastClicked = ref<string>('')

const standardItems: ToolbarItem[] = [
  { id: 'new', label: 'New', icon: Plus },
  { id: 'save', label: 'Save', icon: Save },
  { id: 'delete', label: 'Delete', icon: Trash2 },
  { id: 'copy', label: 'Copy', icon: Copy },
  { id: 'cut', label: 'Cut', icon: Scissors },
  { id: 'paste', label: 'Paste', icon: ClipboardPaste },
  { id: 'undo', label: 'Undo', icon: Undo },
  { id: 'redo', label: 'Redo', icon: Redo },
]

// ─── Demo 2: With Priorities ───────────────────────────────────────────
const priorityItems: ToolbarItem[] = [
  { id: 'new', label: 'New', icon: Plus, priority: 10 },
  { id: 'save', label: 'Save', icon: Save, priority: 10 },
  { id: 'delete', label: 'Delete', icon: Trash2, priority: 10 },
  { id: 'copy', label: 'Copy', icon: Copy, priority: 0 },
  { id: 'cut', label: 'Cut', icon: Scissors, priority: 0 },
  { id: 'paste', label: 'Paste', icon: ClipboardPaste, priority: 0 },
  { id: 'undo', label: 'Undo', icon: Undo, priority: 0 },
  { id: 'redo', label: 'Redo', icon: Redo, priority: 0 },
]

// ─── Demo 3: With Separators ───────────────────────────────────────────
const separatorItems: ToolbarItem[] = [
  { id: 'new', label: 'New', icon: Plus },
  { id: 'save', label: 'Save', icon: Save },
  { id: 'sep1', label: '', separator: true },
  { id: 'copy', label: 'Copy', icon: Copy },
  { id: 'cut', label: 'Cut', icon: Scissors },
  { id: 'paste', label: 'Paste', icon: ClipboardPaste },
  { id: 'sep2', label: '', separator: true },
  { id: 'undo', label: 'Undo', icon: Undo },
  { id: 'redo', label: 'Redo', icon: Redo },
]

// ─── Demo 4: Narrow Container ──────────────────────────────────────────
const narrowItems: ToolbarItem[] = [
  { id: 'search', label: 'Search', icon: Search },
  { id: 'filter', label: 'Filter', icon: Filter },
  { id: 'download', label: 'Download', icon: Download },
  { id: 'upload', label: 'Upload', icon: Upload },
  { id: 'settings', label: 'Settings', icon: Settings },
  { id: 'share', label: 'Share', icon: Share2 },
]

// ─── Demo 5: Mixed Variants ────────────────────────────────────────────
const variantItems: ToolbarItem[] = [
  { id: 'create', label: 'Create', icon: Plus, variant: 'default' },
  { id: 'save', label: 'Save', icon: Save, variant: 'outline' },
  { id: 'search', label: 'Search', icon: Search, variant: 'ghost' },
  { id: 'delete', label: 'Delete', icon: Trash2, variant: 'destructive' },
  { id: 'settings', label: 'Settings', icon: Settings, variant: 'outline', disabled: true },
  { id: 'download', label: 'Download', icon: Download, variant: 'outline' },
]

function handleClick(id: string) {
  lastClicked.value = id
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
            <PanelTop class="h-6 w-6" />
            Overflow Toolbar
          </h1>
          <p class="text-muted-foreground mt-1">
            Responsive toolbar that collapses items into an overflow menu when space is limited.
          </p>
        </div>
      </div>

      <!-- Demo 1: Standard Toolbar -->
      <Card>
        <CardHeader>
          <CardTitle>Standard Toolbar</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            A toolbar with 8 items. Resize the browser window to see items collapse into the overflow menu.
          </p>
          <OverflowToolbar
            :items="standardItems"
            class="rounded-md border bg-muted/30 p-2"
            @item-click="handleClick"
          />
          <div v-if="lastClicked" class="mt-3 text-sm text-muted-foreground">
            Last clicked: <Badge variant="secondary">{{ lastClicked }}</Badge>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: With Priorities -->
      <Card>
        <CardHeader>
          <CardTitle>With Priorities</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            New, Save, and Delete have <code class="text-xs bg-muted px-1 py-0.5 rounded">priority: 10</code> and will
            stay visible longer. Lower priority items overflow first.
          </p>
          <OverflowToolbar
            :items="priorityItems"
            class="rounded-md border bg-muted/30 p-2"
            @item-click="handleClick"
          />
        </CardContent>
      </Card>

      <!-- Demo 3: With Separators -->
      <Card>
        <CardHeader>
          <CardTitle>With Separators</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Toolbar items grouped by separators. Separators render as vertical dividers inline and horizontal dividers in the overflow menu.
          </p>
          <OverflowToolbar
            :items="separatorItems"
            class="rounded-md border bg-muted/30 p-2"
            @item-click="handleClick"
          />
        </CardContent>
      </Card>

      <!-- Demo 4: Narrow Container -->
      <Card>
        <CardHeader>
          <CardTitle>Narrow Container</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            A fixed-width container that forces most items into the overflow menu.
          </p>
          <div class="w-64 border border-dashed border-muted-foreground/30 rounded-md">
            <OverflowToolbar
              :items="narrowItems"
              class="rounded-md bg-muted/30 p-2"
              @item-click="handleClick"
            />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Mixed Variants -->
      <Card>
        <CardHeader>
          <CardTitle>Mixed Variants</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Different button variants (default, outline, ghost, destructive) and a disabled item. Variants are preserved in the visible toolbar buttons.
          </p>
          <OverflowToolbar
            :items="variantItems"
            class="rounded-md border bg-muted/30 p-2"
            @item-click="handleClick"
          />
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
