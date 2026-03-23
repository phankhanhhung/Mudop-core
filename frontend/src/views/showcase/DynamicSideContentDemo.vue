<script setup lang="ts">
import { ref } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import DynamicSideContent from '@/components/smart/DynamicSideContent.vue'
import { ArrowLeft, PanelRight, FileText, HelpCircle, Link, BookOpen } from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Default Layout ─────────────────────────────────────────────

const demo1Visible = ref(true)
const demo1Narrow = ref(false)

// ─── Demo 2: Side on Left ───────────────────────────────────────────────

const demo2Visible = ref(true)

// ─── Demo 3: Equal Split ───────────────────────────────────────────────

const demo3Visible = ref(true)

// ─── Demo 4: Rich Content ──────────────────────────────────────────────

const demo4Visible = ref(true)
const demo4Name = ref('')
const demo4Description = ref('')
const demo4Category = ref('General')
const demo4Submitted = ref(false)
const demo4Error = ref('')

function demo4Submit() {
  if (!demo4Name.value.trim()) {
    demo4Error.value = 'Name is required.'
    return
  }
  demo4Error.value = ''
  demo4Submitted.value = true
  setTimeout(() => {
    demo4Submitted.value = false
    demo4Name.value = ''
    demo4Description.value = ''
    demo4Category.value = 'General'
  }, 2500)
}

// ─── Demo 5: Narrow Container ──────────────────────────────────────────

const demo5Visible = ref(true)
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
            <PanelRight class="h-6 w-6" />
            Dynamic Side Content
          </h1>
          <p class="text-muted-foreground mt-1">
            Adaptive two-panel layout with responsive side content that stacks or hides on narrow containers.
          </p>
        </div>
      </div>

      <!-- Demo 1: Default Layout -->
      <Card>
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            Default Layout
            <Badge variant="secondary">Side on Right</Badge>
            <Badge v-if="demo1Narrow" variant="outline">Narrow</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <DynamicSideContent
            v-model:sideContentVisible="demo1Visible"
            @breakpoint-change="(e) => demo1Narrow = e.isNarrow"
          >
            <div class="space-y-3">
              <h3 class="text-lg font-semibold">Main Content Area</h3>
              <p class="text-sm text-muted-foreground">
                This is the main content area that takes up approximately two-thirds of the available width.
                When the container becomes narrow, the side panel will stack below this content.
              </p>
              <p class="text-sm text-muted-foreground">
                Try resizing your browser window to see the responsive behavior. The toggle button
                in the top-right allows you to manually show or hide the side panel.
              </p>
              <div class="grid grid-cols-2 gap-3">
                <div class="rounded-lg bg-muted/50 p-3 text-center text-sm text-muted-foreground">
                  Content Block A
                </div>
                <div class="rounded-lg bg-muted/50 p-3 text-center text-sm text-muted-foreground">
                  Content Block B
                </div>
              </div>
            </div>

            <template #side>
              <div class="space-y-3">
                <h4 class="font-medium text-sm">Side Panel</h4>
                <p class="text-xs text-muted-foreground">
                  This side panel provides supplementary information. It occupies roughly one-third of the width.
                </p>
                <div class="space-y-2">
                  <div class="rounded bg-muted/50 p-2 text-xs text-muted-foreground">Info item 1</div>
                  <div class="rounded bg-muted/50 p-2 text-xs text-muted-foreground">Info item 2</div>
                  <div class="rounded bg-muted/50 p-2 text-xs text-muted-foreground">Info item 3</div>
                </div>
              </div>
            </template>
          </DynamicSideContent>
        </CardContent>
      </Card>

      <!-- Demo 2: Side on Left -->
      <Card>
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            Side on Left
            <Badge variant="secondary">sideContentPosition="begin"</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <DynamicSideContent
            v-model:sideContentVisible="demo2Visible"
            sideContentPosition="begin"
          >
            <div class="space-y-3">
              <h3 class="text-lg font-semibold">Main Content</h3>
              <p class="text-sm text-muted-foreground">
                The side panel is positioned on the left (begin) side. This pattern is useful for navigation
                panels, filter sidebars, or table-of-contents panels.
              </p>
              <div class="rounded-lg bg-muted/50 p-4 text-sm text-muted-foreground">
                Primary content area with the side panel positioned to the left.
              </div>
            </div>

            <template #side>
              <div class="space-y-2">
                <h4 class="font-medium text-sm">Navigation</h4>
                <ul class="space-y-1">
                  <li class="text-sm text-primary cursor-pointer hover:underline">Section One</li>
                  <li class="text-sm text-muted-foreground cursor-pointer hover:text-foreground">Section Two</li>
                  <li class="text-sm text-muted-foreground cursor-pointer hover:text-foreground">Section Three</li>
                  <li class="text-sm text-muted-foreground cursor-pointer hover:text-foreground">Section Four</li>
                </ul>
              </div>
            </template>
          </DynamicSideContent>
        </CardContent>
      </Card>

      <!-- Demo 3: Equal Split -->
      <Card>
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            Equal Split
            <Badge variant="secondary">50 / 50</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <DynamicSideContent
            v-model:sideContentVisible="demo3Visible"
            :equalSplit="true"
          >
            <div class="space-y-3">
              <h3 class="text-lg font-semibold">Left Panel (50%)</h3>
              <p class="text-sm text-muted-foreground">
                With equal split enabled, both panels share the available width equally.
                This is useful for comparison views or side-by-side editing.
              </p>
              <div class="rounded-lg bg-blue-500/10 p-3 text-sm text-blue-700 dark:text-blue-300">
                Panel A content
              </div>
            </div>

            <template #side>
              <div class="space-y-3">
                <h3 class="text-lg font-semibold">Right Panel (50%)</h3>
                <p class="text-sm text-muted-foreground">
                  Both panels have equal width, making this ideal for comparison layouts.
                </p>
                <div class="rounded-lg bg-green-500/10 p-3 text-sm text-green-700 dark:text-green-300">
                  Panel B content
                </div>
              </div>
            </template>
          </DynamicSideContent>
        </CardContent>
      </Card>

      <!-- Demo 4: With Rich Content -->
      <Card>
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            With Rich Content
            <Badge variant="secondary">Form + Help Panel</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <DynamicSideContent v-model:sideContentVisible="demo4Visible">
            <div class="space-y-4">
              <h3 class="text-lg font-semibold flex items-center gap-2">
                <FileText class="h-5 w-5" />
                Create New Record
              </h3>
              <div v-if="demo4Submitted" class="rounded-md bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-200 dark:border-emerald-800 px-4 py-3 text-sm text-emerald-700 dark:text-emerald-400">
                Record created successfully!
              </div>
              <div v-else class="space-y-3">
                <div>
                  <label class="text-sm font-medium mb-1 block">Name <span class="text-destructive">*</span></label>
                  <input
                    v-model="demo4Name"
                    type="text"
                    placeholder="Enter name..."
                    class="w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                  />
                  <p v-if="demo4Error" class="text-xs text-destructive mt-1">{{ demo4Error }}</p>
                </div>
                <div>
                  <label class="text-sm font-medium mb-1 block">Description</label>
                  <textarea
                    v-model="demo4Description"
                    placeholder="Enter description..."
                    rows="3"
                    class="w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                  />
                </div>
                <div>
                  <label class="text-sm font-medium mb-1 block">Category</label>
                  <select
                    v-model="demo4Category"
                    class="w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
                  >
                    <option>General</option>
                    <option>Finance</option>
                    <option>Operations</option>
                  </select>
                </div>
                <Button size="sm" @click="demo4Submit">Submit</Button>
              </div>
            </div>

            <template #side>
              <div class="space-y-4">
                <div class="space-y-2">
                  <h4 class="font-medium text-sm flex items-center gap-1.5">
                    <HelpCircle class="h-4 w-4" />
                    Help
                  </h4>
                  <p class="text-xs text-muted-foreground">
                    Fill out the form to create a new record. All fields marked with an asterisk are required.
                  </p>
                </div>
                <div class="space-y-2">
                  <h4 class="font-medium text-sm flex items-center gap-1.5">
                    <Link class="h-4 w-4" />
                    Related Links
                  </h4>
                  <ul class="space-y-1 text-xs">
                    <li class="text-primary cursor-pointer hover:underline">Documentation</li>
                    <li class="text-primary cursor-pointer hover:underline">Field Reference Guide</li>
                    <li class="text-primary cursor-pointer hover:underline">Validation Rules</li>
                  </ul>
                </div>
                <div class="space-y-2">
                  <h4 class="font-medium text-sm flex items-center gap-1.5">
                    <BookOpen class="h-4 w-4" />
                    Tips
                  </h4>
                  <ul class="space-y-1 text-xs text-muted-foreground list-disc ml-4">
                    <li>Use descriptive names for easier search</li>
                    <li>Categories help organize records</li>
                    <li>Descriptions support markdown formatting</li>
                  </ul>
                </div>
              </div>
            </template>
          </DynamicSideContent>
        </CardContent>
      </Card>

      <!-- Demo 5: Narrow Container (Stacked) -->
      <Card>
        <CardHeader>
          <CardTitle class="flex items-center gap-2">
            Stacked on Narrow
            <Badge variant="secondary">Fixed 400px Container</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent>
          <p class="text-sm text-muted-foreground mb-4">
            This demo uses a fixed narrow container (400px) to show the stacked falldown behavior.
            The side content falls below the main content when the container is narrower than the breakpoint.
          </p>
          <div class="mx-auto border border-dashed border-border rounded-lg p-4" style="max-width: 400px;">
            <DynamicSideContent
              v-model:sideContentVisible="demo5Visible"
              :breakpoint="720"
            >
              <div class="space-y-2">
                <h3 class="font-semibold">Main Content</h3>
                <p class="text-xs text-muted-foreground">
                  In this narrow container, the side content stacks below automatically.
                </p>
              </div>

              <template #side>
                <div class="space-y-2">
                  <h4 class="font-medium text-sm">Side Panel (Stacked)</h4>
                  <p class="text-xs text-muted-foreground">
                    This panel appears below the main content because the container width is
                    below the 720px breakpoint.
                  </p>
                </div>
              </template>
            </DynamicSideContent>
          </div>
        </CardContent>
      </Card>

      <!-- Usage Notes -->
      <Card>
        <CardHeader>
          <CardTitle>Usage Notes</CardTitle>
        </CardHeader>
        <CardContent class="text-sm text-muted-foreground space-y-2">
          <p><strong>Responsive behavior</strong> is based on the container width (ResizeObserver), not the viewport. This makes the component work correctly when nested inside other layouts.</p>
          <p><strong>sideContentFallDown</strong> controls what happens when the container is narrow: "below" stacks the side content under main, "hidden" hides it entirely.</p>
          <p><strong>equalSplit</strong> gives both panels equal width (50/50) instead of the default 67/33 ratio.</p>
          <p><strong>sideContentPosition</strong> places the side panel at "begin" (left) or "end" (right) of the main content.</p>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
