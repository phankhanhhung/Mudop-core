<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import type {
  ModuleMetadata,
  ServiceMetadata,
  EntitySetMetadata,
  EntityMetadata,
  ActionMetadata,
  FunctionMetadata
} from '@/types/metadata'
import { metadataService } from '@/services/metadataService'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell
} from '@/components/ui/table'
import {
  Database,
  Zap,
  Code,
  Package,
  Server,
  ExternalLink,
  Key,
  Link2,
  Tag,
  ArrowRight
} from 'lucide-vue-next'

interface SelectedNode {
  type: 'module' | 'service' | 'entity' | 'action' | 'function'
  data: ModuleMetadata | ServiceMetadata | EntitySetMetadata | ActionMetadata | FunctionMetadata
  moduleName: string
  serviceName?: string
}

const props = defineProps<{
  selectedNode: SelectedNode | null
}>()

const entityDetail = ref<EntityMetadata | null>(null)
const isLoadingEntity = ref(false)
const entityError = ref<string | null>(null)

watch(
  () => props.selectedNode,
  async (node) => {
    entityDetail.value = null
    entityError.value = null

    if (node && node.type === 'entity') {
      const entitySet = node.data as EntitySetMetadata
      isLoadingEntity.value = true
      try {
        entityDetail.value = await metadataService.getEntity(node.moduleName, entitySet.entityType)
      } catch (e) {
        entityError.value = e instanceof Error ? e.message : 'Failed to load entity metadata'
      } finally {
        isLoadingEntity.value = false
      }
    }
  },
  { immediate: true }
)

function formatAnnotationValue(value: unknown): string {
  if (typeof value === 'string') return value
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'number') return String(value)
  return JSON.stringify(value)
}

function cardinalityLabel(cardinality: string): string {
  switch (cardinality) {
    case 'ZeroOrOne': return '0..1'
    case 'One': return '1'
    case 'Many': return '*'
    case 'OneOrMore': return '1..*'
    default: return cardinality
  }
}
</script>

<template>
  <!-- Empty state -->
  <div v-if="!selectedNode" class="flex items-center justify-center h-full text-muted-foreground">
    <div class="text-center">
      <Database class="h-12 w-12 mx-auto mb-3 opacity-30" />
      <p class="text-sm">Select an item from the tree to view its details</p>
    </div>
  </div>

  <!-- Module detail -->
  <div v-else-if="selectedNode.type === 'module'" class="space-y-4">
    <Card>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Package class="h-5 w-5" />
          <CardTitle>{{ (selectedNode.data as ModuleMetadata).name }}</CardTitle>
          <Badge variant="outline">v{{ (selectedNode.data as ModuleMetadata).version }}</Badge>
        </div>
      </CardHeader>
      <CardContent class="space-y-4">
        <div v-if="(selectedNode.data as ModuleMetadata).description" class="text-sm text-muted-foreground">
          {{ (selectedNode.data as ModuleMetadata).description }}
        </div>

        <div class="grid grid-cols-3 gap-4">
          <div class="rounded-lg border p-4 text-center">
            <div class="text-2xl font-bold">{{ (selectedNode.data as ModuleMetadata).services.length }}</div>
            <div class="text-xs text-muted-foreground mt-1">Services</div>
          </div>
          <div class="rounded-lg border p-4 text-center">
            <div class="text-2xl font-bold">
              {{ (selectedNode.data as ModuleMetadata).services.reduce((sum, s) => sum + s.entities.length, 0) }}
            </div>
            <div class="text-xs text-muted-foreground mt-1">Entities</div>
          </div>
          <div class="rounded-lg border p-4 text-center">
            <div class="text-2xl font-bold">
              {{ (selectedNode.data as ModuleMetadata).services.reduce((sum, s) => sum + s.actions.length + s.functions.length, 0) }}
            </div>
            <div class="text-xs text-muted-foreground mt-1">Operations</div>
          </div>
        </div>

        <div>
          <h4 class="text-sm font-medium mb-2">Services</h4>
          <div class="space-y-1">
            <div
              v-for="service in (selectedNode.data as ModuleMetadata).services"
              :key="service.name"
              class="flex items-center gap-2 text-sm p-2 rounded-md bg-muted/50"
            >
              <Server class="h-4 w-4 text-muted-foreground" />
              <span class="font-medium">{{ service.name }}</span>
              <span class="text-muted-foreground text-xs">({{ service.namespace }})</span>
              <div class="ml-auto flex gap-2">
                <Badge variant="secondary" class="text-xs">{{ service.entities.length }} entities</Badge>
                <Badge v-if="service.actions.length > 0" variant="secondary" class="text-xs">{{ service.actions.length }} actions</Badge>
                <Badge v-if="service.functions.length > 0" variant="secondary" class="text-xs">{{ service.functions.length }} functions</Badge>
              </div>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  </div>

  <!-- Service detail -->
  <div v-else-if="selectedNode.type === 'service'" class="space-y-4">
    <Card>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Server class="h-5 w-5" />
          <CardTitle>{{ (selectedNode.data as ServiceMetadata).name }}</CardTitle>
        </div>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="text-sm text-muted-foreground">
          Namespace: <code class="text-xs bg-muted px-1.5 py-0.5 rounded">{{ (selectedNode.data as ServiceMetadata).namespace }}</code>
        </div>

        <div class="grid grid-cols-3 gap-4">
          <div class="rounded-lg border p-4 text-center">
            <div class="text-2xl font-bold">{{ (selectedNode.data as ServiceMetadata).entities.length }}</div>
            <div class="text-xs text-muted-foreground mt-1">Entities</div>
          </div>
          <div class="rounded-lg border p-4 text-center">
            <div class="text-2xl font-bold">{{ (selectedNode.data as ServiceMetadata).actions.length }}</div>
            <div class="text-xs text-muted-foreground mt-1">Actions</div>
          </div>
          <div class="rounded-lg border p-4 text-center">
            <div class="text-2xl font-bold">{{ (selectedNode.data as ServiceMetadata).functions.length }}</div>
            <div class="text-xs text-muted-foreground mt-1">Functions</div>
          </div>
        </div>

        <!-- Entity list -->
        <div v-if="(selectedNode.data as ServiceMetadata).entities.length > 0">
          <h4 class="text-sm font-medium mb-2">Entities</h4>
          <div class="space-y-1">
            <div
              v-for="entity in (selectedNode.data as ServiceMetadata).entities"
              :key="entity.name"
              class="flex items-center gap-2 text-sm p-2 rounded-md bg-muted/50"
            >
              <Database class="h-4 w-4 text-muted-foreground" />
              <span class="font-medium">{{ entity.name }}</span>
              <span class="text-muted-foreground text-xs">({{ entity.entityType }})</span>
              <RouterLink
                :to="`/odata/${selectedNode.moduleName}/${entity.entityType}`"
                class="ml-auto"
              >
                <Button variant="ghost" size="sm" class="h-7 text-xs">
                  <ExternalLink class="h-3 w-3 mr-1" />
                  Open
                </Button>
              </RouterLink>
            </div>
          </div>
        </div>

        <!-- Action list -->
        <div v-if="(selectedNode.data as ServiceMetadata).actions.length > 0">
          <h4 class="text-sm font-medium mb-2">Actions</h4>
          <div class="space-y-1">
            <div
              v-for="action in (selectedNode.data as ServiceMetadata).actions"
              :key="action.name"
              class="flex items-center gap-2 text-sm p-2 rounded-md bg-muted/50"
            >
              <Zap class="h-4 w-4 text-muted-foreground" />
              <span class="font-medium">{{ action.name }}</span>
              <Badge v-if="action.isBound" variant="outline" class="text-xs">bound</Badge>
              <span v-if="action.returnType" class="ml-auto text-xs text-muted-foreground">
                <ArrowRight class="h-3 w-3 inline" /> {{ action.returnType }}
              </span>
            </div>
          </div>
        </div>

        <!-- Function list -->
        <div v-if="(selectedNode.data as ServiceMetadata).functions.length > 0">
          <h4 class="text-sm font-medium mb-2">Functions</h4>
          <div class="space-y-1">
            <div
              v-for="fn in (selectedNode.data as ServiceMetadata).functions"
              :key="fn.name"
              class="flex items-center gap-2 text-sm p-2 rounded-md bg-muted/50"
            >
              <Code class="h-4 w-4 text-muted-foreground" />
              <span class="font-medium">{{ fn.name }}</span>
              <Badge v-if="fn.isBound" variant="outline" class="text-xs">bound</Badge>
              <span class="ml-auto text-xs text-muted-foreground">
                <ArrowRight class="h-3 w-3 inline" /> {{ fn.returnType }}
              </span>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  </div>

  <!-- Entity detail -->
  <div v-else-if="selectedNode.type === 'entity'" class="space-y-4">
    <!-- Loading state -->
    <div v-if="isLoadingEntity" class="flex items-center justify-center py-12">
      <Spinner size="lg" />
    </div>

    <!-- Error state -->
    <Card v-else-if="entityError">
      <CardContent class="py-8 text-center text-destructive">
        <p class="text-sm">{{ entityError }}</p>
      </CardContent>
    </Card>

    <!-- Entity metadata loaded -->
    <template v-else-if="entityDetail">
      <Card>
        <CardHeader>
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Database class="h-5 w-5" />
              <CardTitle>{{ entityDetail.name }}</CardTitle>
            </div>
            <RouterLink :to="`/odata/${selectedNode.moduleName}/${entityDetail.name}`">
              <Button variant="outline" size="sm">
                <ExternalLink class="h-4 w-4 mr-1.5" />
                Open in List View
              </Button>
            </RouterLink>
          </div>
        </CardHeader>
        <CardContent class="space-y-2">
          <div class="flex flex-wrap gap-x-6 gap-y-1 text-sm">
            <div>
              <span class="text-muted-foreground">Namespace:</span>
              <code class="ml-1 text-xs bg-muted px-1.5 py-0.5 rounded">{{ entityDetail.namespace }}</code>
            </div>
            <div v-if="entityDetail.displayName">
              <span class="text-muted-foreground">Display Name:</span>
              <span class="ml-1">{{ entityDetail.displayName }}</span>
            </div>
            <div>
              <span class="text-muted-foreground">Keys:</span>
              <Badge v-for="key in entityDetail.keys" :key="key" variant="outline" class="ml-1 text-xs">
                <Key class="h-3 w-3 mr-0.5" />
                {{ key }}
              </Badge>
            </div>
          </div>
          <div v-if="entityDetail.description" class="text-sm text-muted-foreground">
            {{ entityDetail.description }}
          </div>
        </CardContent>
      </Card>

      <!-- Fields table -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-2">
            <CardTitle class="text-base">Fields</CardTitle>
            <Badge variant="secondary" class="text-xs">{{ entityDetail.fields.length }}</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead class="text-center">Required</TableHead>
                  <TableHead class="text-center">Read Only</TableHead>
                  <TableHead class="text-center">Computed</TableHead>
                  <TableHead>Default</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="field in entityDetail.fields" :key="field.name">
                  <TableCell class="font-medium">
                    <div class="flex items-center gap-1">
                      <Key v-if="entityDetail.keys.includes(field.name)" class="h-3 w-3 text-amber-500" />
                      {{ field.name }}
                      <span v-if="field.displayName" class="text-xs text-muted-foreground">({{ field.displayName }})</span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge variant="outline" class="text-xs font-mono">
                      {{ field.type }}
                      <template v-if="field.maxLength">({{ field.maxLength }})</template>
                      <template v-if="field.precision">({{ field.precision }}<template v-if="field.scale">,{{ field.scale }}</template>)</template>
                    </Badge>
                    <div v-if="field.enumValues && field.enumValues.length > 0" class="mt-1 flex flex-wrap gap-1">
                      <Badge
                        v-for="ev in field.enumValues"
                        :key="ev.name"
                        variant="secondary"
                        class="text-xs"
                      >
                        {{ ev.name }}={{ ev.value }}
                      </Badge>
                    </div>
                  </TableCell>
                  <TableCell class="text-center">
                    <Badge v-if="field.isRequired" variant="default" class="text-xs bg-amber-600">Yes</Badge>
                    <span v-else class="text-muted-foreground text-xs">No</span>
                  </TableCell>
                  <TableCell class="text-center">
                    <Badge v-if="field.isReadOnly" variant="secondary" class="text-xs">Yes</Badge>
                    <span v-else class="text-muted-foreground text-xs">No</span>
                  </TableCell>
                  <TableCell class="text-center">
                    <Badge v-if="field.isComputed" variant="secondary" class="text-xs">Yes</Badge>
                    <span v-else class="text-muted-foreground text-xs">No</span>
                  </TableCell>
                  <TableCell>
                    <code v-if="field.defaultValue !== undefined && field.defaultValue !== null" class="text-xs bg-muted px-1.5 py-0.5 rounded">
                      {{ field.defaultValue }}
                    </code>
                    <span v-else class="text-muted-foreground text-xs">-</span>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      <!-- Associations table -->
      <Card v-if="entityDetail.associations.length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <Link2 class="h-4 w-4" />
            <CardTitle class="text-base">Associations</CardTitle>
            <Badge variant="secondary" class="text-xs">{{ entityDetail.associations.length }}</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Target Entity</TableHead>
                  <TableHead>Cardinality</TableHead>
                  <TableHead>Foreign Key</TableHead>
                  <TableHead class="text-center">Composition</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="assoc in entityDetail.associations" :key="assoc.name">
                  <TableCell class="font-medium">{{ assoc.name }}</TableCell>
                  <TableCell>
                    <Badge variant="outline" class="text-xs font-mono">{{ assoc.targetEntity }}</Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary" class="text-xs">{{ cardinalityLabel(assoc.cardinality) }}</Badge>
                  </TableCell>
                  <TableCell>
                    <code v-if="assoc.foreignKey" class="text-xs bg-muted px-1.5 py-0.5 rounded">{{ assoc.foreignKey }}</code>
                    <span v-else class="text-muted-foreground text-xs">-</span>
                  </TableCell>
                  <TableCell class="text-center">
                    <Badge v-if="assoc.isComposition" variant="default" class="text-xs bg-blue-600">Yes</Badge>
                    <span v-else class="text-muted-foreground text-xs">No</span>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      <!-- Annotations -->
      <Card v-if="Object.keys(entityDetail.annotations).length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <Tag class="h-4 w-4" />
            <CardTitle class="text-base">Annotations</CardTitle>
            <Badge variant="secondary" class="text-xs">{{ Object.keys(entityDetail.annotations).length }}</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="space-y-1">
            <div
              v-for="(value, key) in entityDetail.annotations"
              :key="String(key)"
              class="flex items-center gap-2 text-sm p-2 rounded-md bg-muted/50"
            >
              <code class="text-xs font-medium">@{{ key }}</code>
              <span class="text-muted-foreground text-xs ml-auto">{{ formatAnnotationValue(value) }}</span>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Bound Actions -->
      <Card v-if="entityDetail.boundActions && entityDetail.boundActions.length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <Zap class="h-4 w-4" />
            <CardTitle class="text-base">Bound Actions</CardTitle>
            <Badge variant="secondary" class="text-xs">{{ entityDetail.boundActions.length }}</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="space-y-3">
            <div
              v-for="action in entityDetail.boundActions"
              :key="action.name"
              class="p-3 rounded-md border"
            >
              <div class="flex items-center gap-2 mb-2">
                <Zap class="h-4 w-4 text-muted-foreground" />
                <span class="font-medium text-sm">{{ action.name }}</span>
                <Badge variant="outline" class="text-xs">bound</Badge>
                <span v-if="action.returnType" class="ml-auto text-xs text-muted-foreground">
                  <ArrowRight class="h-3 w-3 inline" /> {{ action.returnType }}
                </span>
              </div>
              <Table v-if="action.parameters.length > 0">
                <TableHeader>
                  <TableRow>
                    <TableHead class="text-xs">Parameter</TableHead>
                    <TableHead class="text-xs">Type</TableHead>
                    <TableHead class="text-xs text-center">Required</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="param in action.parameters" :key="param.name">
                    <TableCell class="text-xs font-medium">{{ param.name }}</TableCell>
                    <TableCell><Badge variant="outline" class="text-xs font-mono">{{ param.type }}</Badge></TableCell>
                    <TableCell class="text-center">
                      <Badge v-if="param.isRequired" variant="default" class="text-xs bg-amber-600">Yes</Badge>
                      <span v-else class="text-muted-foreground text-xs">No</span>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          </div>
        </CardContent>
      </Card>

      <!-- Bound Functions -->
      <Card v-if="entityDetail.boundFunctions && entityDetail.boundFunctions.length > 0">
        <CardHeader>
          <div class="flex items-center gap-2">
            <Code class="h-4 w-4" />
            <CardTitle class="text-base">Bound Functions</CardTitle>
            <Badge variant="secondary" class="text-xs">{{ entityDetail.boundFunctions.length }}</Badge>
          </div>
        </CardHeader>
        <CardContent>
          <div class="space-y-3">
            <div
              v-for="fn in entityDetail.boundFunctions"
              :key="fn.name"
              class="p-3 rounded-md border"
            >
              <div class="flex items-center gap-2 mb-2">
                <Code class="h-4 w-4 text-muted-foreground" />
                <span class="font-medium text-sm">{{ fn.name }}</span>
                <Badge variant="outline" class="text-xs">bound</Badge>
                <span class="ml-auto text-xs text-muted-foreground">
                  <ArrowRight class="h-3 w-3 inline" /> {{ fn.returnType }}
                </span>
              </div>
              <Table v-if="fn.parameters.length > 0">
                <TableHeader>
                  <TableRow>
                    <TableHead class="text-xs">Parameter</TableHead>
                    <TableHead class="text-xs">Type</TableHead>
                    <TableHead class="text-xs text-center">Required</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="param in fn.parameters" :key="param.name">
                    <TableCell class="text-xs font-medium">{{ param.name }}</TableCell>
                    <TableCell><Badge variant="outline" class="text-xs font-mono">{{ param.type }}</Badge></TableCell>
                    <TableCell class="text-center">
                      <Badge v-if="param.isRequired" variant="default" class="text-xs bg-amber-600">Yes</Badge>
                      <span v-else class="text-muted-foreground text-xs">No</span>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          </div>
        </CardContent>
      </Card>
    </template>

    <!-- Fallback: entity set info only (no detailed metadata) -->
    <Card v-else>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Database class="h-5 w-5" />
          <CardTitle>{{ (selectedNode.data as EntitySetMetadata).name }}</CardTitle>
        </div>
      </CardHeader>
      <CardContent>
        <div class="text-sm text-muted-foreground">
          Entity Type: <code class="text-xs bg-muted px-1.5 py-0.5 rounded">{{ (selectedNode.data as EntitySetMetadata).entityType }}</code>
        </div>
        <RouterLink :to="`/odata/${selectedNode.moduleName}/${(selectedNode.data as EntitySetMetadata).entityType}`" class="mt-3 inline-block">
          <Button variant="outline" size="sm">
            <ExternalLink class="h-4 w-4 mr-1.5" />
            Open in List View
          </Button>
        </RouterLink>
      </CardContent>
    </Card>
  </div>

  <!-- Action detail -->
  <div v-else-if="selectedNode.type === 'action'" class="space-y-4">
    <Card>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Zap class="h-5 w-5" />
          <CardTitle>{{ (selectedNode.data as ActionMetadata).name }}</CardTitle>
          <Badge variant="outline" class="text-xs">Action</Badge>
          <Badge v-if="(selectedNode.data as ActionMetadata).isBound" variant="secondary" class="text-xs">Bound</Badge>
        </div>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="flex flex-wrap gap-x-6 gap-y-1 text-sm">
          <div v-if="(selectedNode.data as ActionMetadata).bindingParameter">
            <span class="text-muted-foreground">Binding Parameter:</span>
            <code class="ml-1 text-xs bg-muted px-1.5 py-0.5 rounded">{{ (selectedNode.data as ActionMetadata).bindingParameter }}</code>
          </div>
          <div>
            <span class="text-muted-foreground">Return Type:</span>
            <Badge v-if="(selectedNode.data as ActionMetadata).returnType" variant="outline" class="ml-1 text-xs font-mono">
              {{ (selectedNode.data as ActionMetadata).returnType }}
            </Badge>
            <span v-else class="ml-1 text-xs text-muted-foreground italic">void</span>
          </div>
        </div>

        <!-- Parameters -->
        <div v-if="(selectedNode.data as ActionMetadata).parameters.length > 0">
          <h4 class="text-sm font-medium mb-2">Parameters</h4>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Type</TableHead>
                <TableHead class="text-center">Required</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="param in (selectedNode.data as ActionMetadata).parameters" :key="param.name">
                <TableCell class="font-medium">{{ param.name }}</TableCell>
                <TableCell><Badge variant="outline" class="text-xs font-mono">{{ param.type }}</Badge></TableCell>
                <TableCell class="text-center">
                  <Badge v-if="param.isRequired" variant="default" class="text-xs bg-amber-600">Yes</Badge>
                  <span v-else class="text-muted-foreground text-xs">No</span>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
        <div v-else class="text-sm text-muted-foreground italic">No parameters</div>
      </CardContent>
    </Card>
  </div>

  <!-- Function detail -->
  <div v-else-if="selectedNode.type === 'function'" class="space-y-4">
    <Card>
      <CardHeader>
        <div class="flex items-center gap-2">
          <Code class="h-5 w-5" />
          <CardTitle>{{ (selectedNode.data as FunctionMetadata).name }}</CardTitle>
          <Badge variant="outline" class="text-xs">Function</Badge>
          <Badge v-if="(selectedNode.data as FunctionMetadata).isBound" variant="secondary" class="text-xs">Bound</Badge>
        </div>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="flex flex-wrap gap-x-6 gap-y-1 text-sm">
          <div v-if="(selectedNode.data as FunctionMetadata).bindingParameter">
            <span class="text-muted-foreground">Binding Parameter:</span>
            <code class="ml-1 text-xs bg-muted px-1.5 py-0.5 rounded">{{ (selectedNode.data as FunctionMetadata).bindingParameter }}</code>
          </div>
          <div>
            <span class="text-muted-foreground">Return Type:</span>
            <Badge variant="outline" class="ml-1 text-xs font-mono">
              {{ (selectedNode.data as FunctionMetadata).returnType }}
            </Badge>
          </div>
        </div>

        <!-- Parameters -->
        <div v-if="(selectedNode.data as FunctionMetadata).parameters.length > 0">
          <h4 class="text-sm font-medium mb-2">Parameters</h4>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Type</TableHead>
                <TableHead class="text-center">Required</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="param in (selectedNode.data as FunctionMetadata).parameters" :key="param.name">
                <TableCell class="font-medium">{{ param.name }}</TableCell>
                <TableCell><Badge variant="outline" class="text-xs font-mono">{{ param.type }}</Badge></TableCell>
                <TableCell class="text-center">
                  <Badge v-if="param.isRequired" variant="default" class="text-xs bg-amber-600">Yes</Badge>
                  <span v-else class="text-muted-foreground text-xs">No</span>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
        <div v-else class="text-sm text-muted-foreground italic">No parameters</div>
      </CardContent>
    </Card>
  </div>
</template>
