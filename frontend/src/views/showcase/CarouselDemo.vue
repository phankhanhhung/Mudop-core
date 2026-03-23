<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import Carousel from '@/components/smart/Carousel.vue'
import CarouselSlide from '@/components/smart/CarouselSlide.vue'
import { ArrowLeft, GalleryHorizontalEnd } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Image Carousel (gradient slides) ──────────────────────────

const imageIndex = ref(0)

const gradientSlides = [
  { gradient: 'from-blue-500 to-cyan-400', title: 'Welcome to BMMDL', subtitle: 'Enterprise-grade DSL platform' },
  { gradient: 'from-purple-500 to-pink-400', title: 'Smart Components', subtitle: 'OpenUI5-parity component library' },
  { gradient: 'from-orange-500 to-yellow-400', title: 'OData v4 Runtime', subtitle: 'Full query and CRUD support' },
  { gradient: 'from-green-500 to-emerald-400', title: 'Multi-Tenancy', subtitle: 'Built-in tenant isolation' },
  { gradient: 'from-rose-500 to-red-400', title: 'Bitemporal Data', subtitle: 'Transaction and valid time tracking' },
]

// ─── Demo 2: Card Carousel ─────────────────────────────────────────────

const cardIndex = ref(0)

const featureCards = [
  { title: 'Smart Filter Bar', description: 'Dynamic filtering with type-aware controls, variant management, and adapt-filters dialog.', tag: 'Filtering' },
  { title: 'Analytical Table', description: 'Aggregation, grouping, totals rows, and tree-style hierarchical data display.', tag: 'Analytics' },
  { title: 'Tree Table', description: 'Hierarchical data with expand/collapse, lazy loading, and drag-drop reordering.', tag: 'Hierarchy' },
  { title: 'Personalization Dialog', description: 'Column visibility, ordering, sorting, grouping, and filtering preferences.', tag: 'P13n' },
]

// ─── Demo 3: Controls (manual, no loop) ────────────────────────────────

const controlIndex = ref(0)

const controlSlides = [
  { color: 'bg-slate-100 dark:bg-slate-800', label: 'Slide 1 of 3', detail: 'No loop mode - cannot go before first' },
  { color: 'bg-slate-200 dark:bg-slate-700', label: 'Slide 2 of 3', detail: 'Manual play/pause control available' },
  { color: 'bg-slate-300 dark:bg-slate-600', label: 'Slide 3 of 3', detail: 'No loop mode - cannot go past last' },
]
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
            <GalleryHorizontalEnd class="h-6 w-6" />
            Carousel
          </h1>
          <p class="text-muted-foreground mt-1">
            Content slider with navigation, auto-play, touch/swipe, and dot indicators.
          </p>
        </div>
      </div>

      <!-- Demo 1: Image Carousel -->
      <Card>
        <CardHeader>
          <CardTitle>Image Carousel (Auto-Play)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Auto-play enabled with 4-second interval. Pauses on hover. Loop wraps from last to first.
            Gradient backgrounds simulate image content.
          </p>
          <div class="max-w-2xl">
            <Carousel
              auto-play
              :auto-play-interval="4000"
              aspect-ratio="16/9"
              @slide-change="imageIndex = $event"
            >
              <CarouselSlide v-for="(slide, i) in gradientSlides" :key="i">
                <div
                  class="h-full w-full bg-gradient-to-br flex flex-col items-center justify-center text-white"
                  :class="slide.gradient"
                >
                  <h2 class="text-3xl font-bold mb-2">{{ slide.title }}</h2>
                  <p class="text-lg opacity-90">{{ slide.subtitle }}</p>
                </div>
              </CarouselSlide>
            </Carousel>
          </div>
          <div class="mt-3 text-sm text-muted-foreground">
            Current slide: <Badge variant="secondary">{{ imageIndex + 1 }} / {{ gradientSlides.length }}</Badge>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 2: Card Carousel -->
      <Card>
        <CardHeader>
          <CardTitle>Card Carousel</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Feature announcement cards with arrows and dot indicators. No auto-play.
            Navigate with arrows on hover or click the dot indicators.
          </p>
          <div class="max-w-xl">
            <Carousel @slide-change="cardIndex = $event">
              <CarouselSlide v-for="(card, i) in featureCards" :key="i">
                <div class="h-full w-full p-8 bg-muted/50 rounded-lg flex flex-col justify-center">
                  <Badge variant="outline" class="w-fit mb-3">{{ card.tag }}</Badge>
                  <h3 class="text-xl font-semibold mb-2">{{ card.title }}</h3>
                  <p class="text-muted-foreground">{{ card.description }}</p>
                </div>
              </CarouselSlide>
            </Carousel>
          </div>
          <div class="mt-3 text-sm text-muted-foreground">
            Current card: <Badge variant="secondary">{{ cardIndex + 1 }} / {{ featureCards.length }}</Badge>
          </div>
        </CardContent>
      </Card>

      <!-- Demo 3: Controls (No Loop, Play/Pause) -->
      <Card>
        <CardHeader>
          <CardTitle>Manual Controls (No Loop)</CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            Loop disabled. Arrows hide at boundaries. Play/pause button visible for manual auto-play control.
            Try pressing Space to toggle play/pause when focused, or use Arrow keys to navigate.
          </p>
          <div class="max-w-lg">
            <Carousel
              :loop="false"
              show-play-button
              :auto-play-interval="2000"
              @slide-change="controlIndex = $event"
            >
              <CarouselSlide v-for="(slide, i) in controlSlides" :key="i">
                <div
                  class="h-48 w-full flex flex-col items-center justify-center rounded-lg"
                  :class="slide.color"
                >
                  <p class="text-lg font-semibold">{{ slide.label }}</p>
                  <p class="text-sm text-muted-foreground mt-1">{{ slide.detail }}</p>
                </div>
              </CarouselSlide>
            </Carousel>
          </div>
          <div class="mt-3 text-sm text-muted-foreground">
            Current slide: <Badge variant="secondary">{{ controlIndex + 1 }} / {{ controlSlides.length }}</Badge>
          </div>
        </CardContent>
      </Card>

      <!-- Keyboard Shortcuts Reference -->
      <Card>
        <CardHeader>
          <CardTitle>Keyboard Shortcuts</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
            <div>
              <kbd class="px-2 py-1 bg-muted rounded text-xs font-mono">ArrowLeft</kbd>
              <span class="ml-2 text-muted-foreground">Previous slide</span>
            </div>
            <div>
              <kbd class="px-2 py-1 bg-muted rounded text-xs font-mono">ArrowRight</kbd>
              <span class="ml-2 text-muted-foreground">Next slide</span>
            </div>
            <div>
              <kbd class="px-2 py-1 bg-muted rounded text-xs font-mono">Home</kbd>
              <span class="ml-2 text-muted-foreground">First slide</span>
            </div>
            <div>
              <kbd class="px-2 py-1 bg-muted rounded text-xs font-mono">End</kbd>
              <span class="ml-2 text-muted-foreground">Last slide</span>
            </div>
            <div>
              <kbd class="px-2 py-1 bg-muted rounded text-xs font-mono">Space</kbd>
              <span class="ml-2 text-muted-foreground">Toggle play/pause</span>
            </div>
            <div>
              <span class="text-muted-foreground">Swipe left/right on touch</span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
