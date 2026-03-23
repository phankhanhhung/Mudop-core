<script setup lang="ts">
import { useRouter } from 'vue-router'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import {
  Zap,
  Upload,
  Users,
  Shield,
  BookOpen,
  FileText,
  ArrowRight
} from 'lucide-vue-next'
import { type Component } from 'vue'

const router = useRouter()

interface QuickAction {
  labelKey: string
  descKey: string
  path: string
  icon: Component
  color: string
}

const actions: QuickAction[] = [
  {
    labelKey: 'dashboard.compileModule',
    descKey: 'dashboard.compileModuleDesc',
    path: '/admin/modules',
    icon: Upload,
    color: 'bg-primary/10 text-primary'
  },
  {
    labelKey: 'dashboard.browseMetadata',
    descKey: 'dashboard.browseMetadataDesc',
    path: '/admin/metadata',
    icon: BookOpen,
    color: 'bg-emerald-500/10 text-emerald-500'
  },
  {
    labelKey: 'dashboard.userManagement',
    descKey: 'dashboard.userManagementDesc',
    path: '/admin/users',
    icon: Users,
    color: 'bg-violet-500/10 text-violet-500'
  },
  {
    labelKey: 'dashboard.roleManagement',
    descKey: 'dashboard.roleManagementDesc',
    path: '/admin/roles',
    icon: Shield,
    color: 'bg-amber-500/10 text-amber-500'
  },
  {
    labelKey: 'dashboard.viewAuditLog',
    descKey: 'dashboard.viewAuditLogDesc',
    path: '/admin/audit',
    icon: FileText,
    color: 'bg-cyan-500/10 text-cyan-500'
  },
  {
    labelKey: 'dashboard.apiDocumentation',
    descKey: 'dashboard.apiDocumentationDesc',
    path: '/admin/api-docs',
    icon: BookOpen,
    color: 'bg-rose-500/10 text-rose-500'
  }
]

function navigate(path: string) {
  router.push(path)
}
</script>

<template>
  <Card>
    <CardHeader class="pb-3">
      <div class="flex items-center gap-3">
        <div class="h-9 w-9 rounded-lg bg-amber-500/10 flex items-center justify-center">
          <Zap class="h-5 w-5 text-amber-500" />
        </div>
        <div>
          <CardTitle class="text-base">{{ $t('dashboard.quickActions') }}</CardTitle>
          <CardDescription>{{ $t('dashboard.quickActionsSubtitle') }}</CardDescription>
        </div>
      </div>
    </CardHeader>
    <CardContent>
      <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
        <button
          v-for="action in actions"
          :key="action.path"
          class="group flex items-start gap-3 p-3 rounded-lg border border-transparent hover:border-border hover:bg-accent/50 transition-all text-left"
          @click="navigate(action.path)"
        >
          <div class="h-9 w-9 rounded-lg flex items-center justify-center shrink-0" :class="action.color">
            <component :is="action.icon" class="h-4.5 w-4.5" />
          </div>
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-1">
              <span class="text-sm font-medium">{{ $t(action.labelKey) }}</span>
              <ArrowRight class="h-3 w-3 text-muted-foreground opacity-0 group-hover:opacity-100 transition-opacity" />
            </div>
            <p class="text-xs text-muted-foreground mt-0.5 line-clamp-1">
              {{ $t(action.descKey) }}
            </p>
          </div>
        </button>
      </div>
    </CardContent>
  </Card>
</template>
