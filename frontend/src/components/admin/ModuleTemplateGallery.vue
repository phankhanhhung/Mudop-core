<script setup lang="ts">
import { ref } from 'vue'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { FileCode, Database, Layers, Shield, ArrowRight } from 'lucide-vue-next'
import { cn } from '@/lib/utils'
import type { Component } from 'vue'

interface Props {
  class?: string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'select': [payload: { source: string; moduleName: string }]
}>()

interface Template {
  id: string
  name: string
  description: string
  icon: Component
  iconColor: string
  template: string
}

const templates: Template[] = [
  {
    id: 'empty',
    name: 'Empty Module',
    description: 'Start with a clean module definition',
    icon: FileCode,
    iconColor: 'bg-blue-100 text-blue-600',
    template: `module \${ModuleName} v1.0 {
    author "Developer";
    description "\${ModuleName} module";

    namespace app.\${moduleName} {
        // Define your entities, types, and services here
    }
}`
  },
  {
    id: 'crud',
    name: 'CRUD Entity',
    description: 'Entity with key fields, associations, and CRUD service',
    icon: Database,
    iconColor: 'bg-green-100 text-green-600',
    template: `module \${ModuleName} v1.0 {
    author "Developer";
    description "\${ModuleName} module with CRUD entity";

    namespace app.\${moduleName} {
        entity \${ModuleName}Item {
            key ID: UUID;
            name: String(100);
            description: String(500);
            status: \${ModuleName}Status default #Active;
            createdAt: DateTime;
        }

        enum \${ModuleName}Status {
            Active = 1;
            Inactive = 2;
            Archived = 3;
        }

        service \${ModuleName}Service {
            entity Items as \${ModuleName}Item;
        }
    }
}`
  },
  {
    id: 'master-detail',
    name: 'Master-Detail',
    description: 'Parent-child relationship with compositions',
    icon: Layers,
    iconColor: 'bg-purple-100 text-purple-600',
    template: `module \${ModuleName} v1.0 {
    author "Developer";
    description "\${ModuleName} module with master-detail pattern";

    namespace app.\${moduleName} {
        entity \${ModuleName}Header {
            key ID: UUID;
            number: String(20);
            description: String(255);
            status: HeaderStatus default #Draft;
            totalAmount: Decimal(18,2);

            items: composition [*] of \${ModuleName}Item;
        }

        entity \${ModuleName}Item {
            key ID: UUID;
            lineNumber: Integer;
            description: String(255);
            quantity: Decimal(18,3);
            unitPrice: Decimal(18,2);
            amount: Decimal(18,2);
        }

        enum HeaderStatus {
            Draft = 1;
            Active = 2;
            Completed = 3;
            Cancelled = 4;
        }

        service \${ModuleName}Service {
            entity Headers as \${ModuleName}Header;
        }
    }
}`
  },
  {
    id: 'business-rules',
    name: 'Business Rules',
    description: 'Entity with business rules and access control',
    icon: Shield,
    iconColor: 'bg-amber-100 text-amber-600',
    template: `module \${ModuleName} v1.0 {
    author "Developer";
    description "\${ModuleName} module with business rules";

    namespace app.\${moduleName} {
        entity \${ModuleName}Record {
            key ID: UUID;
            name: String(100);
            email: String(255);
            amount: Decimal(18,2);
            status: RecordStatus default #Pending;
        }

        enum RecordStatus {
            Pending = 1;
            Approved = 2;
            Rejected = 3;
        }

        rule Validate\${ModuleName} for \${ModuleName}Record on before create {
            validate name is not null
                message 'Name is required'
                severity error;
            validate email like '%@%.%'
                message 'Invalid email format'
                severity error;
            validate amount > 0
                message 'Amount must be positive'
                severity error;
        }

        access control for \${ModuleName}Record {
            grant read to authenticated;
            grant create, update to role 'Editor';
            grant delete to role 'Admin';
        }

        service \${ModuleName}Service {
            entity Records as \${ModuleName}Record;
        }
    }
}`
  }
]

const selectedTemplateId = ref<string | null>(null)
const moduleName = ref('MyModule')
const showNameInput = ref(false)

function selectTemplate(template: Template) {
  selectedTemplateId.value = template.id
  showNameInput.value = true
  moduleName.value = 'MyModule'
}

function confirmSelection(template: Template) {
  if (!moduleName.value.trim()) {
    return
  }

  const pascalCaseName = moduleName.value.trim()
  const lowerCaseName = pascalCaseName.charAt(0).toLowerCase() + pascalCaseName.slice(1)

  const source = template.template
    .replace(/\$\{ModuleName\}/g, pascalCaseName)
    .replace(/\$\{moduleName\}/g, lowerCaseName)

  emit('select', { source, moduleName: pascalCaseName })

  // Reset state
  selectedTemplateId.value = null
  showNameInput.value = false
}

function cancelSelection() {
  selectedTemplateId.value = null
  showNameInput.value = false
}
</script>

<template>
  <div :class="cn('space-y-4', props.class)">
    <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
      <Card
        v-for="template in templates"
        :key="template.id"
        :class="cn(
          'cursor-pointer transition-all hover:shadow-md',
          selectedTemplateId === template.id && 'ring-2 ring-primary'
        )"
      >
        <CardHeader>
          <div class="flex items-start gap-3">
            <div :class="cn('p-2 rounded-full', template.iconColor)">
              <component :is="template.icon" class="h-5 w-5" />
            </div>
            <div class="flex-1">
              <CardTitle class="text-lg">{{ template.name }}</CardTitle>
              <CardDescription class="mt-1">
                {{ template.description }}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div v-if="selectedTemplateId === template.id && showNameInput" class="space-y-3">
            <div class="space-y-2">
              <label class="text-sm font-medium">Module Name</label>
              <Input
                v-model="moduleName"
                placeholder="MyModule"
                @keyup.enter="confirmSelection(template)"
                @keyup.escape="cancelSelection"
              />
            </div>
            <div class="flex gap-2">
              <Button
                size="sm"
                class="flex-1"
                @click="confirmSelection(template)"
              >
                <ArrowRight class="h-4 w-4 mr-1" />
                Generate
              </Button>
              <Button
                size="sm"
                variant="outline"
                @click="cancelSelection"
              >
                Cancel
              </Button>
            </div>
          </div>
          <Button
            v-else
            variant="outline"
            class="w-full"
            @click="selectTemplate(template)"
          >
            Use Template
          </Button>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
