<script setup lang="ts">
import { ref, markRaw } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import ProcessFlow from '@/components/smart/ProcessFlow.vue'
import type { ProcessNode } from '@/composables/useProcessFlow'
import {
  ArrowLeft,
  Workflow,
  CheckCircle2,
  CreditCard,
  Package,
  Truck,
  MapPin,
  Code,
  GitPullRequest,
  TestTube,
  Server,
  Rocket,
} from 'lucide-vue-next'
import { useRouter } from 'vue-router'

const router = useRouter()

// ─── Demo 1: Order Fulfillment ────────────────────────────────────────────

const orderNodes: ProcessNode[] = [
  {
    id: 'order-received',
    title: 'Order Received',
    status: 'positive',
    icon: markRaw(CheckCircle2),
  },
  {
    id: 'payment-verified',
    title: 'Payment',
    subtitle: 'Verified',
    status: 'positive',
    icon: markRaw(CreditCard),
  },
  {
    id: 'warehouse',
    title: 'Warehouse',
    subtitle: 'Delayed',
    status: 'critical',
    icon: markRaw(Package),
    children: [
      { id: 'pick', title: 'Pick', status: 'positive' },
      { id: 'pack', title: 'Pack', status: 'critical' },
      { id: 'label', title: 'Label', status: 'planned' },
    ],
  },
  {
    id: 'shipping',
    title: 'Shipping',
    status: 'neutral',
    icon: markRaw(Truck),
  },
  {
    id: 'delivery',
    title: 'Delivery',
    status: 'planned',
    icon: markRaw(MapPin),
  },
]

// ─── Demo 2: Software Release Pipeline ───────────────────────────────────

const releaseNodes: ProcessNode[] = [
  {
    id: 'development',
    title: 'Development',
    status: 'positive',
    icon: markRaw(Code),
  },
  {
    id: 'code-review',
    title: 'Code Review',
    status: 'positive',
    icon: markRaw(GitPullRequest),
  },
  {
    id: 'testing',
    title: 'Testing',
    subtitle: '3 failures',
    status: 'critical',
    icon: markRaw(TestTube),
  },
  {
    id: 'staging',
    title: 'Staging',
    status: 'planned',
    icon: markRaw(Server),
  },
  {
    id: 'production',
    title: 'Production',
    status: 'planned',
    icon: markRaw(Rocket),
  },
]

// ─── Clicked node state ──────────────────────────────────────────────────

const clickedNode = ref<ProcessNode | null>(null)

function onNodeClick(node: ProcessNode) {
  clickedNode.value = node
}

function statusLabel(status: string): string {
  return status.charAt(0).toUpperCase() + status.slice(1)
}

function statusBadgeVariant(status: string): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case 'positive': return 'default'
    case 'negative': return 'destructive'
    case 'critical': return 'secondary'
    default: return 'outline'
  }
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
            <Workflow class="h-6 w-6" />
            Process Flow
          </h1>
          <p class="text-muted-foreground mt-1">
            Visual process/workflow display with status indicators and connections.
          </p>
        </div>
      </div>

      <!-- Demo 1: Order Fulfillment -->
      <Card>
        <CardHeader>
          <CardTitle>Order Fulfillment</CardTitle>
        </CardHeader>
        <CardContent>
          <ProcessFlow
            :nodes="orderNodes"
            @node-click="onNodeClick"
          />
        </CardContent>
      </Card>

      <!-- Demo 2: Software Release Pipeline -->
      <Card>
        <CardHeader>
          <CardTitle>Software Release Pipeline</CardTitle>
        </CardHeader>
        <CardContent>
          <ProcessFlow
            :nodes="releaseNodes"
            @node-click="onNodeClick"
          />
        </CardContent>
      </Card>

      <!-- Clicked node info -->
      <Card v-if="clickedNode">
        <CardHeader>
          <CardTitle class="text-base">Selected Node</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-[120px_1fr] gap-y-2 text-sm">
            <span class="text-muted-foreground">ID</span>
            <span class="font-mono">{{ clickedNode.id }}</span>

            <span class="text-muted-foreground">Title</span>
            <span class="font-medium">{{ clickedNode.title }}</span>

            <span class="text-muted-foreground">Subtitle</span>
            <span>{{ clickedNode.subtitle || '-' }}</span>

            <span class="text-muted-foreground">Status</span>
            <div>
              <Badge :variant="statusBadgeVariant(clickedNode.status)">
                {{ statusLabel(clickedNode.status) }}
              </Badge>
            </div>

            <span class="text-muted-foreground">Sub-steps</span>
            <span>{{ clickedNode.children?.length ?? 0 }}</span>
          </div>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
