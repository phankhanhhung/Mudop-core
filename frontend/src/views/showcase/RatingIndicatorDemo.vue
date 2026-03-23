<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import RatingIndicator from '@/components/smart/RatingIndicator.vue'
import { ArrowLeft, Star } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Interactive ───────────────────────────────────────────────

const interactiveValue = ref(3)

// ─── Demo 2: Readonly (half-star) ──────────────────────────────────────

const readonlyValue = ref(3.5)

// ─── Demo 3: Custom max ───────────────────────────────────────────────

const customMaxValue = ref(7)

// ─── Demo 4: Sizes ────────────────────────────────────────────────────

const sizeSmValue = ref(4)
const sizeMdValue = ref(4)
const sizeLgValue = ref(4)

// ─── Demo 5: Custom color ─────────────────────────────────────────────

const customColorValue = ref(3)

// ─── Demo 6: Disabled ─────────────────────────────────────────────────

const disabledValue = ref(2)

// ─── Demo 7: With labels (form) ───────────────────────────────────────

const qualityRating = ref(0)
const serviceRating = ref(0)
const valueRating = ref(0)
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
            <Star class="h-6 w-6" />
            Rating Indicator
          </h1>
          <p class="text-muted-foreground mt-1">
            Star rating input with hover preview, half-stars, and keyboard support.
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
            Default 5-star rating. Hover to preview, click to set. Click the same star again to clear.
          </p>
          <div class="flex items-end gap-6">
            <RatingIndicator v-model="interactiveValue" show-value />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ interactiveValue }}</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: Readonly (half-star) -->
      <Card>
        <CardHeader>
          <CardTitle>Readonly (Half-Star)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Fractional values display half-star rendering in readonly mode. Set to 3.5 stars.
          </p>
          <RatingIndicator v-model="readonlyValue" readonly show-value />
        </CardContent>
      </Card>

      <!-- Demo 3: Custom max -->
      <Card>
        <CardHeader>
          <CardTitle>Custom Max Value</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            A 10-star rating scale for more granularity.
          </p>
          <div class="flex items-end gap-6">
            <RatingIndicator v-model="customMaxValue" :max-value="10" show-value />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ customMaxValue }}</Badge>
            </div>
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
            Three sizes: sm, md (default), and lg. Each adjusts the star icon dimensions.
          </p>
          <div class="flex flex-col gap-4">
            <div class="flex items-center gap-4">
              <span class="text-sm text-muted-foreground w-12">Small</span>
              <RatingIndicator v-model="sizeSmValue" size="sm" />
            </div>
            <div class="flex items-center gap-4">
              <span class="text-sm text-muted-foreground w-12">Medium</span>
              <RatingIndicator v-model="sizeMdValue" size="md" />
            </div>
            <div class="flex items-center gap-4">
              <span class="text-sm text-muted-foreground w-12">Large</span>
              <RatingIndicator v-model="sizeLgValue" size="lg" />
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 5: Custom color -->
      <Card>
        <CardHeader>
          <CardTitle>Custom Color</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Using <code class="text-xs bg-muted px-1 py-0.5 rounded">text-rose-500</code> for a heart-like color.
          </p>
          <div class="flex items-end gap-6">
            <RatingIndicator v-model="customColorValue" color="text-rose-500" show-value />
            <div class="text-sm text-muted-foreground pb-1">
              Value: <Badge variant="secondary">{{ customColorValue }}</Badge>
            </div>
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
          <RatingIndicator v-model="disabledValue" disabled show-value />
        </CardContent>
      </Card>

      <!-- Demo 7: With labels (form) -->
      <Card>
        <CardHeader>
          <CardTitle>With Labels (Form)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Multiple rating fields with labels, simulating a feedback form.
          </p>
          <div class="space-y-4 max-w-md">
            <RatingIndicator
              v-model="qualityRating"
              label="Product Quality"
              show-value
            />
            <RatingIndicator
              v-model="serviceRating"
              label="Customer Service"
              show-value
            />
            <RatingIndicator
              v-model="valueRating"
              label="Value for Money"
              show-value
            />
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
