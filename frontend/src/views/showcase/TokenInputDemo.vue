<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import TokenInput from '@/components/smart/TokenInput.vue'
import { ArrowLeft, Tags } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Interactive ───────────────────────────────────────────────

const interactiveTokens = ref<string[]>([])

// ─── Demo 2: Pre-filled ────────────────────────────────────────────────

const prefilledTokens = ref(['Design', 'Engineering', 'Marketing'])

// ─── Demo 3: Max Tokens ────────────────────────────────────────────────

const maxTokens = ref(['Frontend', 'Backend'])

// ─── Demo 4: With Label ────────────────────────────────────────────────

const skillTokens = ref<string[]>(['Vue', 'TypeScript'])
const tagTokens = ref<string[]>([])

// ─── Demo 5: Readonly ──────────────────────────────────────────────────

const readonlyTokens = ref(['Approved', 'Reviewed', 'Published'])

// ─── Demo 6: Disabled ──────────────────────────────────────────────────

const disabledTokens = ref(['Locked', 'Archived'])
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
            <Tags class="h-6 w-6" />
            Token Input
          </h1>
          <p class="text-muted-foreground mt-1">
            Multi-value text input where values appear as removable chip/pill tokens.
          </p>
        </div>
      </div>

      <!-- Demo 1: Interactive -->
      <Card>
        <CardHeader>
          <CardTitle>Interactive</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Type text and press Enter or comma to add tokens. Click the X to remove. Backspace on empty input removes the last token.
          </p>
          <div class="space-y-3 max-w-lg">
            <TokenInput v-model="interactiveTokens" placeholder="Add tags..." />
            <div class="text-sm text-muted-foreground">
              Value: <code class="text-xs bg-muted px-1.5 py-0.5 rounded">{{ JSON.stringify(interactiveTokens) }}</code>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: Pre-filled -->
      <Card>
        <CardHeader>
          <CardTitle>Pre-filled</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Starts with existing tokens. Duplicates are prevented by default (case-insensitive).
          </p>
          <div class="space-y-3 max-w-lg">
            <TokenInput v-model="prefilledTokens" />
            <div class="text-sm text-muted-foreground">
              Value: <code class="text-xs bg-muted px-1.5 py-0.5 rounded">{{ JSON.stringify(prefilledTokens) }}</code>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 3: Max Tokens -->
      <Card>
        <CardHeader>
          <CardTitle>Max Tokens</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Limited to 3 tokens. The input field hides when the limit is reached.
          </p>
          <div class="space-y-3 max-w-lg">
            <TokenInput v-model="maxTokens" :max-tokens="3" placeholder="Max 3 tokens..." />
            <div class="flex items-center gap-2 text-sm text-muted-foreground">
              <span>{{ maxTokens.length }} / 3 tokens</span>
              <Badge v-if="maxTokens.length >= 3" variant="outline">Limit reached</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 4: With Label -->
      <Card>
        <CardHeader>
          <CardTitle>With Label</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Labeled token inputs for use within forms.
          </p>
          <div class="space-y-4 max-w-lg">
            <TokenInput
              v-model="skillTokens"
              label="Skills"
              placeholder="Add a skill..."
            />
            <TokenInput
              v-model="tagTokens"
              label="Tags"
              placeholder="Add a tag..."
            />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Readonly -->
      <Card>
        <CardHeader>
          <CardTitle>Readonly</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Tokens are displayed but cannot be added or removed.
          </p>
          <div class="max-w-lg">
            <TokenInput v-model="readonlyTokens" readonly />
          </div>
        </CardContent>
      </Card>

      <!-- Demo 6: Disabled -->
      <Card>
        <CardHeader>
          <CardTitle>Disabled</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Disabled state reduces opacity and prevents all interaction.
          </p>
          <div class="max-w-lg">
            <TokenInput v-model="disabledTokens" disabled />
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
