<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import TreeTable from '@/components/smart/TreeTable.vue'
import type { TreeNode } from '@/composables/useTreeTable'
import type { EntityMetadata, FieldMetadata } from '@/types/metadata'
import { FolderTree, ArrowLeft } from 'lucide-vue-next'

// ── Metadata for org chart ──

const orgFields: FieldMetadata[] = [
  {
    name: 'Id',
    type: 'UUID',
    displayName: 'ID',
    isRequired: true,
    isReadOnly: true,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'Name',
    type: 'String',
    displayName: 'Name',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 100,
    annotations: {},
  },
  {
    name: 'Department',
    type: 'String',
    displayName: 'Department',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 50,
    annotations: {},
  },
  {
    name: 'Role',
    type: 'String',
    displayName: 'Role',
    isRequired: true,
    isReadOnly: false,
    isComputed: false,
    maxLength: 80,
    annotations: {},
  },
  {
    name: 'Employees',
    type: 'Integer',
    displayName: 'Employees',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    annotations: {},
  },
  {
    name: 'Budget',
    type: 'Decimal',
    displayName: 'Budget',
    isRequired: false,
    isReadOnly: false,
    isComputed: false,
    precision: 18,
    scale: 2,
    annotations: {},
  },
]

const orgMetadata: EntityMetadata = {
  name: 'OrgUnit',
  namespace: 'demo',
  displayName: 'Organization Unit',
  fields: orgFields,
  keys: ['Id'],
  associations: [],
  annotations: {},
}

// ── Generate hierarchical org data (~50 nodes, 3-4 levels) ──

let nodeId = 0
function uid(): string {
  nodeId++
  return `node-${nodeId}`
}

const orgData = ref<TreeNode[]>([
  {
    Id: uid(), Name: 'Acme Corporation', Department: 'Executive', Role: 'CEO', Employees: 2450, Budget: 120000000,
    children: [
      {
        Id: uid(), Name: 'Sarah Chen', Department: 'Technology', Role: 'CTO', Employees: 820, Budget: 45000000,
        children: [
          {
            Id: uid(), Name: 'Marcus Rivera', Department: 'Engineering', Role: 'VP Engineering', Employees: 540, Budget: 28000000,
            children: [
              { Id: uid(), Name: 'Lisa Park', Department: 'Frontend', Role: 'Director', Employees: 120, Budget: 6200000 },
              { Id: uid(), Name: 'James Wilson', Department: 'Backend', Role: 'Director', Employees: 180, Budget: 9400000 },
              { Id: uid(), Name: 'Anika Patel', Department: 'Platform', Role: 'Director', Employees: 95, Budget: 5100000 },
              { Id: uid(), Name: 'David Kim', Department: 'DevOps', Role: 'Director', Employees: 65, Budget: 3800000 },
              { Id: uid(), Name: 'Tomasz Nowak', Department: 'QA', Role: 'Director', Employees: 80, Budget: 3500000 },
            ],
          },
          {
            Id: uid(), Name: 'Rachel Green', Department: 'Product', Role: 'VP Product', Employees: 160, Budget: 9500000,
            children: [
              { Id: uid(), Name: 'Omar Hassan', Department: 'Product - Core', Role: 'Sr. Product Manager', Employees: 45, Budget: 2800000 },
              { Id: uid(), Name: 'Emily Tran', Department: 'Product - Growth', Role: 'Sr. Product Manager', Employees: 55, Budget: 3200000 },
              { Id: uid(), Name: 'Carlos Mendez', Department: 'UX Design', Role: 'Design Lead', Employees: 60, Budget: 3500000 },
            ],
          },
          {
            Id: uid(), Name: 'Yuki Tanaka', Department: 'Data Science', Role: 'VP Data', Employees: 120, Budget: 7500000,
            children: [
              { Id: uid(), Name: 'Priya Sharma', Department: 'ML Engineering', Role: 'Director', Employees: 65, Budget: 4200000 },
              { Id: uid(), Name: 'Alex Volkov', Department: 'Analytics', Role: 'Director', Employees: 55, Budget: 3300000 },
            ],
          },
        ],
      },
      {
        Id: uid(), Name: 'Michael Torres', Department: 'Revenue', Role: 'CRO', Employees: 680, Budget: 35000000,
        children: [
          {
            Id: uid(), Name: 'Jennifer Adams', Department: 'Sales', Role: 'VP Sales', Employees: 420, Budget: 22000000,
            children: [
              { Id: uid(), Name: 'Robert Chang', Department: 'Enterprise Sales', Role: 'Director', Employees: 180, Budget: 10000000 },
              { Id: uid(), Name: 'Maria Santos', Department: 'SMB Sales', Role: 'Director', Employees: 140, Budget: 7000000 },
              { Id: uid(), Name: 'Thomas Mueller', Department: 'Sales Ops', Role: 'Director', Employees: 100, Budget: 5000000 },
            ],
          },
          {
            Id: uid(), Name: 'Laura Kim', Department: 'Marketing', Role: 'VP Marketing', Employees: 260, Budget: 13000000,
            children: [
              { Id: uid(), Name: 'Daniel Brown', Department: 'Brand', Role: 'Director', Employees: 80, Budget: 4500000 },
              { Id: uid(), Name: 'Sophie Laurent', Department: 'Demand Gen', Role: 'Director', Employees: 95, Budget: 5000000 },
              { Id: uid(), Name: 'Nathan Lee', Department: 'Content', Role: 'Director', Employees: 85, Budget: 3500000 },
            ],
          },
        ],
      },
      {
        Id: uid(), Name: 'Amanda Foster', Department: 'Finance', Role: 'CFO', Employees: 340, Budget: 18000000,
        children: [
          { Id: uid(), Name: 'Gregory Hall', Department: 'Accounting', Role: 'Controller', Employees: 120, Budget: 6000000 },
          { Id: uid(), Name: 'Diana Reyes', Department: 'FP&A', Role: 'VP Finance', Employees: 80, Budget: 4500000 },
          { Id: uid(), Name: 'Kevin O\'Brien', Department: 'Treasury', Role: 'Treasurer', Employees: 45, Budget: 2500000 },
          { Id: uid(), Name: 'Janet Wu', Department: 'Procurement', Role: 'Director', Employees: 95, Budget: 5000000 },
        ],
      },
      {
        Id: uid(), Name: 'Patricia Moore', Department: 'People', Role: 'CHRO', Employees: 280, Budget: 14000000,
        children: [
          { Id: uid(), Name: 'Brian Jackson', Department: 'Talent Acquisition', Role: 'Director', Employees: 85, Budget: 4500000 },
          { Id: uid(), Name: 'Christine Nguyen', Department: 'People Ops', Role: 'Director', Employees: 95, Budget: 4800000 },
          { Id: uid(), Name: 'Steven Clark', Department: 'L&D', Role: 'Director', Employees: 60, Budget: 2700000 },
          { Id: uid(), Name: 'Michelle Davis', Department: 'Total Rewards', Role: 'Director', Employees: 40, Budget: 2000000 },
        ],
      },
      {
        Id: uid(), Name: 'Richard Campbell', Department: 'Legal', Role: 'General Counsel', Employees: 90, Budget: 5500000,
        children: [
          { Id: uid(), Name: 'Andrea Miller', Department: 'Corporate Legal', Role: 'Sr. Counsel', Employees: 35, Budget: 2200000 },
          { Id: uid(), Name: 'Philip Reed', Department: 'Compliance', Role: 'Chief Compliance Officer', Employees: 55, Budget: 3300000 },
        ],
      },
      {
        Id: uid(), Name: 'Elizabeth Hart', Department: 'Operations', Role: 'COO', Employees: 240, Budget: 12000000,
        children: [
          { Id: uid(), Name: 'Mark Thompson', Department: 'IT Infrastructure', Role: 'Director', Employees: 110, Budget: 5500000 },
          { Id: uid(), Name: 'Nancy Garcia', Department: 'Facilities', Role: 'Director', Employees: 75, Budget: 3800000 },
          { Id: uid(), Name: 'Paul Anderson', Department: 'Security', Role: 'CISO', Employees: 55, Budget: 2700000 },
        ],
      },
    ],
  },
])

// ── Demo controls ──

const selectionMode = ref<'none' | 'single' | 'multi'>('multi')
const selectedKeys = ref<string[]>([])
const sortField = ref<string | undefined>(undefined)
const sortDirection = ref<'asc' | 'desc'>('asc')

function handleSelectionChange(keys: string[]) {
  selectedKeys.value = keys
}

function handleSort(field: string, direction: 'asc' | 'desc') {
  sortField.value = field
  sortDirection.value = direction
}

const selectionModes = ['none', 'single', 'multi'] as const
</script>

<template>
  <DefaultLayout>
    <div class="space-y-6">
      <!-- Page header -->
      <div class="flex items-center gap-4">
        <RouterLink to="/showcase">
          <Button variant="ghost" size="sm">
            <ArrowLeft class="h-4 w-4 mr-1" />
            Back to Showcase
          </Button>
        </RouterLink>
        <div class="flex items-center gap-3">
          <div class="flex items-center justify-center h-10 w-10 rounded-lg bg-primary/10">
            <FolderTree class="h-5 w-5 text-primary" />
          </div>
          <div>
            <h1 class="text-2xl font-bold text-foreground">Tree Table</h1>
            <p class="text-sm text-muted-foreground">
              Hierarchical data grid with expand/collapse, virtual scrolling, selection, and sorting
            </p>
          </div>
        </div>
      </div>

      <!-- Controls -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">Controls</CardTitle>
        </CardHeader>
        <CardContent>
          <div class="flex flex-wrap items-center gap-4">
            <!-- Selection mode -->
            <div class="flex items-center gap-2">
              <span class="text-sm font-medium text-muted-foreground">Selection:</span>
              <div class="flex gap-1">
                <Button
                  v-for="mode in selectionModes"
                  :key="mode"
                  :variant="selectionMode === mode ? 'default' : 'outline'"
                  size="sm"
                  @click="selectionMode = mode"
                >
                  {{ mode }}
                </Button>
              </div>
            </div>

            <!-- Selection info -->
            <div v-if="selectedKeys.length > 0" class="flex items-center gap-2">
              <Badge variant="secondary">{{ selectedKeys.length }} selected</Badge>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- TreeTable demo -->
      <Card>
        <CardHeader>
          <CardTitle class="text-base">Organization Chart</CardTitle>
        </CardHeader>
        <CardContent>
          <TreeTable
            :treeData="orgData"
            toggleField="Name"
            :metadata="orgMetadata"
            :selectionMode="selectionMode"
            :initialExpanded="true"
            :enableVirtualScroll="false"
            :sortField="sortField"
            :sortDirection="sortDirection"
            title="Acme Corp Org Structure"
            maxHeight="700px"
            @selection-change="handleSelectionChange"
            @sort="handleSort"
          />
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
