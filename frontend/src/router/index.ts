import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore, waitForAuthReady } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'
import { usePluginRegistry, generatePluginRoutes } from '@/plugins'

// Lazy-loaded route components
const LoginView = () => import('@/views/auth/LoginView.vue')
const RegisterView = () => import('@/views/auth/RegisterView.vue')
const DashboardView = () => import('@/views/dashboard/DashboardView.vue')
const TenantListView = () => import('@/views/tenant/TenantListView.vue')
const TenantCreateView = () => import('@/views/tenant/TenantCreateView.vue')
const EntityListView = () => import('@/views/entity/EntityListView.vue')
const EntityCreateView = () => import('@/views/entity/EntityCreateView.vue')
const EntityEditView = () => import('@/views/entity/EntityEditView.vue')
const EntityDetailView = () => import('@/views/entity/EntityDetailView.vue')
const SettingsView = () => import('@/views/settings/SettingsView.vue')
const AdminModulesView = () => import('@/views/admin/AdminModulesView.vue')
const MetadataBrowserView = () => import('@/views/admin/MetadataBrowserView.vue')
const BatchOperationsView = () => import('@/views/admin/BatchOperationsView.vue')
const ServiceActionsView = () => import('@/views/admin/ServiceActionsView.vue')
const UserManagementView = () => import('@/views/admin/UserManagementView.vue')
const AuditLogView = () => import('@/views/admin/AuditLogView.vue')
const EntityAggregationView = () => import('@/views/entity/EntityAggregationView.vue')
const EntityNaturalQueryView = () => import('@/views/entity/EntityNaturalQueryView.vue')
const EntityErdView = () => import('@/views/entity/EntityErdView.vue')
const SequenceManagementView = () => import('@/views/admin/SequenceManagementView.vue')
const RoleManagementView = () => import('@/views/admin/RoleManagementView.vue')
const ApiDocsView = () => import('@/views/admin/ApiDocsView.vue')
const FormLayoutDesignerView = () => import('@/views/admin/FormLayoutDesignerView.vue')
const DataMigrationView = () => import('@/views/admin/DataMigrationView.vue')
const IntegrationHubView = () => import('@/views/admin/IntegrationHubView.vue')
const ReportDesignerView = () => import('@/views/admin/ReportDesignerView.vue')
const PluginManagementView = () => import('@/views/admin/PluginManagementView.vue')
const LaunchpadView = () => import('@/views/launchpad/LaunchpadView.vue')
const ComponentShowcaseView = () => import('@/views/showcase/ComponentShowcaseView.vue')
const AdvancedFieldsDemo = () => import('@/views/showcase/AdvancedFieldsDemo.vue')
const DataGridProDemo = () => import('@/views/showcase/DataGridProDemo.vue')
const TreeTableDemo = () => import('@/views/showcase/TreeTableDemo.vue')
const AnalyticalTableDemo = () => import('@/views/showcase/AnalyticalTableDemo.vue')
const UploadCollectionDemo = () => import('@/views/showcase/UploadCollectionDemo.vue')
const DynamicPageDemo = () => import('@/views/showcase/DynamicPageDemo.vue')
const IconTabBarDemo = () => import('@/views/showcase/IconTabBarDemo.vue')
const P13nDialogDemo = () => import('@/views/showcase/P13nDialogDemo.vue')
const WizardDemo = () => import('@/views/showcase/WizardDemo.vue')
const MessageBoxDemo = () => import('@/views/showcase/MessageBoxDemo.vue')
const StepInputDemo = () => import('@/views/showcase/StepInputDemo.vue')
const ResponsiveTableDemo = () => import('@/views/showcase/ResponsiveTableDemo.vue')
const PhaseFDemo = () => import('@/views/showcase/PhaseFDemo.vue')
// Phase F demos
const CarouselDemo = () => import('@/views/showcase/CarouselDemo.vue')
const ColorPickerDemo = () => import('@/views/showcase/ColorPickerDemo.vue')
const RangeSliderDemo = () => import('@/views/showcase/RangeSliderDemo.vue')
const RatingIndicatorDemo = () => import('@/views/showcase/RatingIndicatorDemo.vue')
const ProcessFlowDemo = () => import('@/views/showcase/ProcessFlowDemo.vue')
const GanttChartDemo = () => import('@/views/showcase/GanttChartDemo.vue')
const SplitAppDemo = () => import('@/views/showcase/SplitAppDemo.vue')
const PhaseGDemo = () => import('@/views/showcase/PhaseGDemo.vue')
// Phase G demos
const TokenInputDemo = () => import('@/views/showcase/TokenInputDemo.vue')
const TimelineDemo = () => import('@/views/showcase/TimelineDemo.vue')
const OverflowToolbarDemo = () => import('@/views/showcase/OverflowToolbarDemo.vue')
const DynamicSideContentDemo = () => import('@/views/showcase/DynamicSideContentDemo.vue')
const PlanningCalendarDemo = () => import('@/views/showcase/PlanningCalendarDemo.vue')
const FeedListDemo = () => import('@/views/showcase/FeedListDemo.vue')
const InfoLabelDemo = () => import('@/views/showcase/InfoLabelDemo.vue')
const RecentFeaturesDemo = () => import('@/views/showcase/RecentFeaturesDemo.vue')
const AggregationDemo = () => import('@/views/showcase/AggregationDemo.vue')
const NotFoundView = () => import('@/views/NotFoundView.vue')
const OfflineView = () => import('@/views/OfflineView.vue')

const routes: RouteRecordRaw[] = [
  // Auth routes (public)
  {
    path: '/auth',
    name: 'auth',
    redirect: '/auth/login',
    meta: { requiresGuest: true },
    children: [
      {
        path: 'login',
        name: 'login',
        component: LoginView,
        meta: { title: 'Login' }
      },
      {
        path: 'register',
        name: 'register',
        component: RegisterView,
        meta: { title: 'Register' }
      },
      {
        path: 'oauth-callback',
        name: 'oauth-callback',
        component: { template: '<div>Completing sign-in...</div>' },
        meta: { title: 'OAuth Callback' }
      }
    ]
  },

  // Protected routes
  {
    path: '/',
    name: 'home',
    redirect: '/dashboard',
    meta: { requiresAuth: true }
  },
  {
    path: '/dashboard',
    name: 'dashboard',
    component: DashboardView,
    meta: { requiresAuth: true, title: 'Dashboard' }
  },
  {
    path: '/launchpad',
    name: 'launchpad',
    component: LaunchpadView,
    meta: { requiresAuth: true, title: 'Launchpad' }
  },

  // Tenant routes
  {
    path: '/tenants',
    name: 'tenants',
    component: TenantListView,
    meta: { requiresAuth: true, title: 'Tenants' }
  },
  {
    path: '/tenants/create',
    name: 'tenant-create',
    component: TenantCreateView,
    meta: { requiresAuth: true, title: 'Create Tenant' }
  },

  // ERD view (standalone, no tenant required)
  {
    path: '/erd',
    name: 'entity-erd',
    component: EntityErdView,
    meta: { requiresAuth: true, title: 'Entity Relationship Diagram' },
  },

  // Dynamic entity routes (requires tenant)
  {
    path: '/odata/:module/:entity/analytics',
    name: 'entity-aggregation',
    component: EntityAggregationView,
    meta: { requiresAuth: true, requiresTenant: true, title: 'Analytics' }
  },
  {
    path: '/odata/:module/:entity',
    name: 'entity-list',
    component: EntityListView,
    meta: { requiresAuth: true, requiresTenant: true }
  },
  {
    path: '/odata/:module/:entity/new',
    name: 'entity-create',
    component: EntityCreateView,
    meta: { requiresAuth: true, requiresTenant: true }
  },
  {
    path: '/odata/:module/:entity/kanban',
    name: 'entity-kanban',
    component: () => import('@/views/entity/EntityKanbanView.vue'),
    meta: { requiresAuth: true, requiresTenant: true, title: 'Kanban Board' },
  },
  {
    path: '/odata/:module/:entity/nlq',
    name: 'entity-nlq',
    component: EntityNaturalQueryView,
    meta: { requiresAuth: true, requiresTenant: true, title: 'Natural Language Query' },
  },
  {
    path: '/odata/:module/:entity/:id',
    name: 'entity-detail',
    component: EntityDetailView,
    meta: { requiresAuth: true, requiresTenant: true }
  },
  {
    path: '/odata/:module/:entity/:id/edit',
    name: 'entity-edit',
    component: EntityEditView,
    meta: { requiresAuth: true, requiresTenant: true }
  },

  // Showcase
  {
    path: '/showcase',
    name: 'showcase',
    component: ComponentShowcaseView,
    meta: { requiresAuth: true, title: 'Component Showcase' }
  },
  {
    path: '/showcase/advanced-fields',
    name: 'advanced-fields-demo',
    component: AdvancedFieldsDemo,
    meta: { requiresAuth: true, title: 'Advanced Field Types' }
  },
  {
    path: '/showcase/data-grid-pro',
    name: 'data-grid-pro-demo',
    component: DataGridProDemo,
    meta: { requiresAuth: true, title: 'Data Grid Pro' }
  },
  {
    path: '/showcase/tree-table',
    name: 'tree-table-demo',
    component: TreeTableDemo,
    meta: { requiresAuth: true, title: 'Tree Table' }
  },
  {
    path: '/showcase/analytical-table',
    name: 'analytical-table-demo',
    component: AnalyticalTableDemo,
    meta: { requiresAuth: true, title: 'Analytical Table' }
  },
  {
    path: '/showcase/upload-collection',
    name: 'upload-collection-demo',
    component: UploadCollectionDemo,
    meta: { requiresAuth: true, title: 'Upload Collection' }
  },
  {
    path: '/showcase/dynamic-page',
    name: 'dynamic-page-demo',
    component: DynamicPageDemo,
    meta: { requiresAuth: true, title: 'Dynamic Page' }
  },
  {
    path: '/showcase/icon-tab-bar',
    name: 'icon-tab-bar-demo',
    component: IconTabBarDemo,
    meta: { requiresAuth: true, title: 'Icon Tab Bar' }
  },
  {
    path: '/showcase/p13n-dialog',
    name: 'p13n-dialog-demo',
    component: P13nDialogDemo,
    meta: { requiresAuth: true, title: 'P13n Dialog' }
  },
  {
    path: '/showcase/wizard',
    name: 'wizard-demo',
    component: WizardDemo,
    meta: { requiresAuth: true, title: 'Wizard' }
  },
  {
    path: '/showcase/message-box',
    name: 'message-box-demo',
    component: MessageBoxDemo,
    meta: { requiresAuth: true, title: 'Message Box' }
  },
  {
    path: '/showcase/step-input',
    name: 'step-input-demo',
    component: StepInputDemo,
    meta: { requiresAuth: true, title: 'Step Input' }
  },
  {
    path: '/showcase/responsive-table',
    name: 'responsive-table-demo',
    component: ResponsiveTableDemo,
    meta: { requiresAuth: true, title: 'Responsive Table Popin' }
  },
  // Phase F + G consolidated demos
  {
    path: '/showcase/phase-f',
    name: 'phase-f-demo',
    component: PhaseFDemo,
    meta: { requiresAuth: true, title: 'Phase F Components' }
  },
  {
    path: '/showcase/phase-g',
    name: 'phase-g-demo',
    component: PhaseGDemo,
    meta: { requiresAuth: true, title: 'Phase G Components' }
  },
  // Phase F demos
  {
    path: '/showcase/carousel',
    name: 'carousel-demo',
    component: CarouselDemo,
    meta: { requiresAuth: true, title: 'Carousel' }
  },
  {
    path: '/showcase/color-picker',
    name: 'color-picker-demo',
    component: ColorPickerDemo,
    meta: { requiresAuth: true, title: 'Color Picker' }
  },
  {
    path: '/showcase/range-slider',
    name: 'range-slider-demo',
    component: RangeSliderDemo,
    meta: { requiresAuth: true, title: 'Range Slider' }
  },
  {
    path: '/showcase/rating-indicator',
    name: 'rating-indicator-demo',
    component: RatingIndicatorDemo,
    meta: { requiresAuth: true, title: 'Rating Indicator' }
  },
  {
    path: '/showcase/process-flow',
    name: 'process-flow-demo',
    component: ProcessFlowDemo,
    meta: { requiresAuth: true, title: 'Process Flow' }
  },
  {
    path: '/showcase/gantt-chart',
    name: 'gantt-chart-demo',
    component: GanttChartDemo,
    meta: { requiresAuth: true, title: 'Gantt Chart' }
  },
  {
    path: '/showcase/split-app',
    name: 'split-app-demo',
    component: SplitAppDemo,
    meta: { requiresAuth: true, title: 'Split App' }
  },
  // Phase G demos
  {
    path: '/showcase/token-input',
    name: 'token-input-demo',
    component: TokenInputDemo,
    meta: { requiresAuth: true, title: 'Token Input' }
  },
  {
    path: '/showcase/timeline',
    name: 'timeline-demo',
    component: TimelineDemo,
    meta: { requiresAuth: true, title: 'Timeline' }
  },
  {
    path: '/showcase/overflow-toolbar',
    name: 'overflow-toolbar-demo',
    component: OverflowToolbarDemo,
    meta: { requiresAuth: true, title: 'Overflow Toolbar' }
  },
  {
    path: '/showcase/dynamic-side-content',
    name: 'dynamic-side-content-demo',
    component: DynamicSideContentDemo,
    meta: { requiresAuth: true, title: 'Dynamic Side Content' }
  },
  {
    path: '/showcase/planning-calendar',
    name: 'planning-calendar-demo',
    component: PlanningCalendarDemo,
    meta: { requiresAuth: true, title: 'Planning Calendar' }
  },
  {
    path: '/showcase/feed-list',
    name: 'feed-list-demo',
    component: FeedListDemo,
    meta: { requiresAuth: true, title: 'Feed List' }
  },
  {
    path: '/showcase/info-label',
    name: 'info-label-demo',
    component: InfoLabelDemo,
    meta: { requiresAuth: true, title: 'Info Label' }
  },
  {
    path: '/showcase/recent-features',
    name: 'recent-features-demo',
    component: RecentFeaturesDemo,
    meta: { requiresAuth: true, title: 'Recent Backend Features' }
  },
  {
    path: '/showcase/aggregation',
    name: 'aggregation-demo',
    component: AggregationDemo,
    meta: { requiresAuth: true, title: 'Aggregation Playground' }
  },

  // Settings
  {
    path: '/settings',
    name: 'settings',
    component: SettingsView,
    meta: { requiresAuth: true, title: 'Settings' }
  },

  // Admin routes
  {
    path: '/admin/modules',
    name: 'admin-modules',
    component: AdminModulesView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Module Management' }
  },
  {
    path: '/admin/metadata',
    name: 'admin-metadata',
    component: MetadataBrowserView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Metadata Browser' }
  },
  {
    path: '/admin/batch',
    name: 'admin-batch',
    component: BatchOperationsView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Batch Operations' }
  },
  {
    path: '/admin/actions',
    name: 'admin-actions',
    component: ServiceActionsView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Service Actions' }
  },
  // Moved to plugin system: PlatformIdentityPlugin manages /admin/users
  {
    path: '/admin/sequences',
    name: 'admin-sequences',
    component: SequenceManagementView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], requiresTenant: true, title: 'Sequence Management' }
  },

  {
    path: '/admin/roles',
    name: 'admin-roles',
    component: RoleManagementView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Role Management' }
  },
  {
    path: '/admin/audit',
    name: 'admin-audit',
    component: AuditLogView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Audit Log' }
  },
  {
    path: '/admin/api-docs',
    name: 'admin-api-docs',
    component: ApiDocsView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'API Documentation' }
  },
  {
    path: '/admin/form-designer',
    name: 'admin-form-designer',
    component: FormLayoutDesignerView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Form Layout Designer' }
  },
  {
    path: '/admin/data-migration',
    name: 'data-migration',
    component: DataMigrationView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Data Migration' },
  },
  {
    path: '/admin/integrations',
    name: 'admin-integrations',
    component: IntegrationHubView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Integration Hub' },
  },
  // Moved to plugin system: ReportingPlugin manages /admin/reports
  {
    path: '/admin/plugins',
    name: 'admin-plugins',
    component: PluginManagementView,
    meta: { requiresAuth: true, requiredRoles: ['Admin'], title: 'Plugins' },
  },

  // Offline fallback
  {
    path: '/offline',
    name: 'offline',
    component: OfflineView,
    meta: { requiresGuest: false }
  },

  // 404
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: NotFoundView,
    meta: { title: 'Not Found' }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

// ── Dynamic plugin routes ────────────────────────────────────────────────────
// Load plugin manifest and register plugin-contributed routes.
// This runs once on app startup. Plugin routes are added before the 404 catch-all
// so they take precedence.
let pluginRoutesLoaded = false

async function loadPluginRoutes() {
  if (pluginRoutesLoaded) return
  pluginRoutesLoaded = true
  try {
    const { loadManifest } = usePluginRegistry()
    const manifest = await loadManifest()
    const pluginRoutes = generatePluginRoutes(manifest)
    for (const route of pluginRoutes) {
      router.addRoute(route)
    }
  } catch {
    // Plugin routes are optional — silently continue if manifest fails
  }
}

// Navigation guards
router.beforeEach(async (to, _from, next) => {
  // Wait for the auth store to finish restoring the session from
  // persisted tokens.  This resolves instantly after the first load.
  await waitForAuthReady()

  // Load plugin routes on first authenticated navigation
  if (!pluginRoutesLoaded) {
    const authStore = useAuthStore()
    if (authStore.isAuthenticated) {
      await loadPluginRoutes()
      // If the current route is the 404 catch-all but now matches a plugin route,
      // retry the navigation.
      if (to.name === 'not-found') {
        const resolved = router.resolve(to.fullPath)
        if (resolved.name !== 'not-found') {
          return next(to.fullPath)
        }
      }
    }
  }

  const authStore = useAuthStore()
  const tenantStore = useTenantStore()

  const isAuthenticated = authStore.isAuthenticated
  const hasTenant = tenantStore.hasTenant

  // Check if route requires authentication
  if (to.meta.requiresAuth && !isAuthenticated) {
    return next({
      name: 'login',
      query: { redirect: to.fullPath }
    })
  }

  // Check if route requires guest (redirect authenticated users)
  if (to.meta.requiresGuest && isAuthenticated) {
    return next({ name: 'dashboard' })
  }

  // Check if route requires specific roles (RBAC)
  if (to.meta.requiredRoles && to.meta.requiredRoles.length > 0) {
    const userRoles = authStore.userRoles ?? []
    const hasRole = to.meta.requiredRoles.some((r: string) => userRoles.includes(r))
    if (!hasRole) {
      return next({ name: 'dashboard' })
    }
  }

  // Check if route requires tenant selection
  if (to.meta.requiresTenant && !hasTenant) {
    return next({
      name: 'tenants',
      query: { redirect: to.fullPath }
    })
  }

  // Update document title
  const title = to.meta.title as string | undefined
  document.title = title ? `${title} | BMMDL` : 'BMMDL Platform'

  next()
})

// Focus management on route change
router.afterEach(() => {
  // Move focus to main content after navigation for screen readers
  const main = document.getElementById('main-content')
  if (main) {
    main.focus({ preventScroll: false })
  }
})

export default router

// Type augmentation for route meta
declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    requiresGuest?: boolean
    requiresTenant?: boolean
    requiredRoles?: string[]
    title?: string
    roles?: string[]
    plugin?: string
  }
}
