<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import InfoLabel from '@/components/smart/InfoLabel.vue'
import {
  ArrowLeft,
  Tag,
  CheckCircle,
  AlertTriangle,
  XCircle,
  Info,
  Zap,
  Shield,
} from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1 & 2: Color scheme labels ─────────────────────────────────
const schemeNames = [
  'Blue (Info)',
  'Teal',
  'Green (Success)',
  'Amber (Warning)',
  'Red (Error)',
  'Indigo',
  'Pink',
  'Cyan',
  'Orange',
  'Violet',
]

// ─── Demo 5: Interactive click counter ────────────────────────────────
const clickCounts = ref<Record<string, number>>({
  action1: 0,
  action2: 0,
  action3: 0,
})

function handleClick(key: string) {
  clickCounts.value[key]++
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
            <Tag class="h-6 w-6" />
            Info Label
          </h1>
          <p class="text-muted-foreground mt-1">
            Compact, semantically colored label/tag with optional icon for status indicators and metadata.
          </p>
        </div>
      </div>

      <!-- Demo 1: All Color Schemes (Filled) -->
      <Card>
        <CardHeader>
          <CardTitle>All Color Schemes (Filled)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            10 predefined color schemes with filled backgrounds. Each scheme conveys different semantic meaning.
          </p>
          <div class="flex flex-wrap gap-2">
            <InfoLabel
              v-for="i in 10"
              :key="'filled-' + i"
              :text="i + ' - ' + schemeNames[i - 1]"
              :color-scheme="(i as 1|2|3|4|5|6|7|8|9|10)"
              render-mode="filled"
              display-only
            />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: All Color Schemes (Outlined) -->
      <Card>
        <CardHeader>
          <CardTitle>All Color Schemes (Outlined)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            The same 10 schemes in outlined mode: colored border and text, transparent background.
          </p>
          <div class="flex flex-wrap gap-2">
            <InfoLabel
              v-for="i in 10"
              :key="'outlined-' + i"
              :text="i + ' - ' + schemeNames[i - 1]"
              :color-scheme="(i as 1|2|3|4|5|6|7|8|9|10)"
              render-mode="outlined"
              display-only
            />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 3: With Icons -->
      <Card>
        <CardHeader>
          <CardTitle>With Icons</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Labels with leading icons to reinforce the semantic meaning visually.
          </p>
          <div class="flex flex-wrap gap-2">
            <InfoLabel text="Information" :color-scheme="1" :icon="Info" display-only />
            <InfoLabel text="Success" :color-scheme="3" :icon="CheckCircle" display-only />
            <InfoLabel text="Warning" :color-scheme="4" :icon="AlertTriangle" display-only />
            <InfoLabel text="Critical" :color-scheme="5" :icon="XCircle" display-only />
            <InfoLabel text="Performance" :color-scheme="9" :icon="Zap" display-only />
            <InfoLabel text="Secure" :color-scheme="6" :icon="Shield" display-only />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 4: Sizes -->
      <Card>
        <CardHeader>
          <CardTitle>Sizes</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Three sizes: small, medium (default), and large. Suitable for different UI contexts.
          </p>
          <div class="flex items-center gap-4">
            <div class="flex items-center gap-2">
              <span class="text-sm text-muted-foreground w-16">Small</span>
              <InfoLabel text="Label" :color-scheme="1" size="sm" display-only />
            </div>
            <div class="flex items-center gap-2">
              <span class="text-sm text-muted-foreground w-16">Medium</span>
              <InfoLabel text="Label" :color-scheme="1" size="md" display-only />
            </div>
            <div class="flex items-center gap-2">
              <span class="text-sm text-muted-foreground w-16">Large</span>
              <InfoLabel text="Label" :color-scheme="1" size="lg" display-only />
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Interactive vs Display-Only -->
      <Card>
        <CardHeader>
          <CardTitle>Interactive vs Display-Only</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Interactive labels respond to hover and click. Display-only labels have no interaction.
          </p>
          <div class="space-y-4">
            <div>
              <p class="text-sm font-medium mb-2">Interactive (clickable)</p>
              <div class="flex items-center gap-2">
                <InfoLabel
                  text="Approve"
                  :color-scheme="3"
                  :icon="CheckCircle"
                  @click="handleClick('action1')"
                />
                <InfoLabel
                  text="Review"
                  :color-scheme="4"
                  :icon="AlertTriangle"
                  @click="handleClick('action2')"
                />
                <InfoLabel
                  text="Reject"
                  :color-scheme="5"
                  :icon="XCircle"
                  @click="handleClick('action3')"
                />
                <span class="text-sm text-muted-foreground ml-2">
                  Clicks:
                  <Badge variant="secondary" class="ml-1">Approve {{ clickCounts.action1 }}</Badge>
                  <Badge variant="secondary" class="ml-1">Review {{ clickCounts.action2 }}</Badge>
                  <Badge variant="secondary" class="ml-1">Reject {{ clickCounts.action3 }}</Badge>
                </span>
              </div>
            </div>
            <div>
              <p class="text-sm font-medium mb-2">Display-only (static)</p>
              <div class="flex items-center gap-2">
                <InfoLabel text="Published" :color-scheme="3" display-only />
                <InfoLabel text="Archived" :color-scheme="8" display-only />
                <InfoLabel text="Deprecated" :color-scheme="5" render-mode="outlined" display-only />
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 6: In Context (Table Row) -->
      <Card>
        <CardHeader>
          <CardTitle>In Context</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            InfoLabels used within a simulated data table to show status, priority, and category.
          </p>
          <div class="border rounded-lg overflow-hidden">
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b bg-muted/50">
                  <th class="text-left px-4 py-2 font-medium">Order ID</th>
                  <th class="text-left px-4 py-2 font-medium">Customer</th>
                  <th class="text-left px-4 py-2 font-medium">Status</th>
                  <th class="text-left px-4 py-2 font-medium">Priority</th>
                  <th class="text-left px-4 py-2 font-medium">Category</th>
                </tr>
              </thead>
              <tbody>
                <tr class="border-b">
                  <td class="px-4 py-2 font-mono text-xs">ORD-001</td>
                  <td class="px-4 py-2">Acme Corp</td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Delivered" :color-scheme="3" :icon="CheckCircle" size="sm" display-only />
                  </td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Normal" :color-scheme="1" size="sm" render-mode="outlined" display-only />
                  </td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Electronics" :color-scheme="6" size="sm" display-only />
                  </td>
                </tr>
                <tr class="border-b">
                  <td class="px-4 py-2 font-mono text-xs">ORD-002</td>
                  <td class="px-4 py-2">Globex Inc</td>
                  <td class="px-4 py-2">
                    <InfoLabel text="In Transit" :color-scheme="1" :icon="Info" size="sm" display-only />
                  </td>
                  <td class="px-4 py-2">
                    <InfoLabel text="High" :color-scheme="9" size="sm" render-mode="outlined" display-only />
                  </td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Furniture" :color-scheme="2" size="sm" display-only />
                  </td>
                </tr>
                <tr>
                  <td class="px-4 py-2 font-mono text-xs">ORD-003</td>
                  <td class="px-4 py-2">Wayne Ent.</td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Delayed" :color-scheme="5" :icon="AlertTriangle" size="sm" display-only />
                  </td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Urgent" :color-scheme="5" size="sm" render-mode="outlined" display-only />
                  </td>
                  <td class="px-4 py-2">
                    <InfoLabel text="Automotive" :color-scheme="10" size="sm" display-only />
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
