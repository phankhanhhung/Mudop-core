<script setup lang="ts">
import { ref, computed } from 'vue'
import { RouterLink } from 'vue-router'
import SplitApp from '@/components/layout/SplitApp.vue'
import { Badge } from '@/components/ui/badge'
import { Mail, Clock, User, Paperclip } from 'lucide-vue-next'
import type { SplitAppMode } from '@/composables/useSplitApp'

// ─── Types ──────────────────────────────────────────────────────────────

interface Email {
  id: number
  from: string
  fromEmail: string
  to: string
  subject: string
  preview: string
  body: string
  date: string
  read: boolean
  hasAttachment: boolean
}

// ─── Sample emails ──────────────────────────────────────────────────────

const emails: Email[] = [
  {
    id: 1,
    from: 'Sarah Chen',
    fromEmail: 'sarah.chen@acme.corp',
    to: 'me@company.com',
    subject: 'Q4 Budget Review - Action Required',
    preview: 'Hi team, please review the attached Q4 budget proposal before our meeting on Friday...',
    body: 'Hi team,\n\nPlease review the attached Q4 budget proposal before our meeting on Friday. We need to finalize the allocations for the engineering and marketing departments by end of week.\n\nKey changes from Q3:\n- Engineering headcount increased by 15%\n- Marketing spend reduced by 8% due to channel optimization\n- Infrastructure costs shifted to cloud-first model\n\nLet me know if you have any questions or concerns.\n\nBest regards,\nSarah',
    date: '2026-02-10 09:15',
    read: false,
    hasAttachment: true,
  },
  {
    id: 2,
    from: 'DevOps Bot',
    fromEmail: 'noreply@ci.internal',
    to: 'engineering@company.com',
    subject: 'Build #4821 Failed - main branch',
    preview: 'The latest build on main has failed. 3 tests in the integration suite are reporting timeout errors...',
    body: 'Build #4821 on branch main has failed.\n\nFailure Summary:\n- 3 tests failed in integration suite\n- All failures are timeout-related in the payment processing module\n- Failure started after commit abc1234 by jdoe\n\nFailed Tests:\n1. PaymentGateway.ProcessAsync_Timeout\n2. PaymentGateway.RefundAsync_Timeout\n3. PaymentGateway.BatchProcess_Timeout\n\nPlease investigate and fix before merging additional changes.',
    date: '2026-02-10 08:42',
    read: false,
    hasAttachment: false,
  },
  {
    id: 3,
    from: 'Marcus Johnson',
    fromEmail: 'marcus.j@partner.io',
    to: 'me@company.com',
    subject: 'Partnership Proposal - DataSync Integration',
    preview: 'Following our conversation at the conference, I wanted to formalize our partnership proposal...',
    body: 'Hi,\n\nFollowing our conversation at the tech conference last week, I wanted to formalize our partnership proposal for the DataSync integration.\n\nWe believe there is a strong synergy between our real-time data synchronization platform and your BMMDL meta-model framework. Specifically, we propose:\n\n1. A joint integration module for bi-directional sync\n2. Co-marketing at upcoming industry events\n3. Shared documentation and developer resources\n\nI have attached a detailed proposal document. Would you be available for a call next Tuesday to discuss?\n\nBest,\nMarcus Johnson\nVP Partnerships, DataSync Inc.',
    date: '2026-02-09 16:30',
    read: true,
    hasAttachment: true,
  },
  {
    id: 4,
    from: 'HR Department',
    fromEmail: 'hr@company.com',
    to: 'all-staff@company.com',
    subject: 'Updated Remote Work Policy - Effective March 1',
    preview: 'Dear colleagues, we are pleased to announce updates to our remote work policy that will take effect...',
    body: 'Dear colleagues,\n\nWe are pleased to announce updates to our remote work policy, effective March 1, 2026.\n\nKey Changes:\n- Flexible work-from-home days increased from 2 to 3 per week\n- Home office stipend increased to $75/month\n- Quarterly in-person team events will be organized\n- Core collaboration hours: 10:00 AM - 2:00 PM local time\n\nPlease review the full policy document on the HR portal and reach out to your manager if you have questions.\n\nBest regards,\nHR Department',
    date: '2026-02-09 14:00',
    read: true,
    hasAttachment: false,
  },
  {
    id: 5,
    from: 'Elena Rodriguez',
    fromEmail: 'elena.r@company.com',
    to: 'me@company.com',
    subject: 'Code Review: Feature/temporal-queries PR #387',
    preview: 'I have left some comments on your PR. Overall looks great, but I have a few suggestions regarding...',
    body: 'Hey,\n\nI have left some comments on your PR #387 for the temporal queries feature. Overall the implementation looks great!\n\nA few suggestions:\n\n1. The bitemporal join logic in DynamicSqlBuilder could benefit from an index hint comment for the DBA\n2. Consider adding a max depth guard for recursive temporal expansions\n3. The test coverage for edge cases around midnight boundary conditions could be improved\n4. Nice use of the expression evaluator for computed temporal fields\n\nNothing blocking - approve with minor changes. Let me know when you have addressed the comments and I will do a final pass.\n\nCheers,\nElena',
    date: '2026-02-09 11:22',
    read: true,
    hasAttachment: false,
  },
  {
    id: 6,
    from: 'Jira Notifications',
    fromEmail: 'noreply@jira.internal',
    to: 'me@company.com',
    subject: '[BMMDL-2847] Story moved to In Progress',
    preview: 'The story "Implement SplitApp responsive layout component" has been moved to In Progress by...',
    body: 'Project: BMMDL\nIssue: BMMDL-2847\nType: Story\nPriority: High\n\nSummary: Implement SplitApp responsive layout component\n\nStatus Change: To Do -> In Progress\nAssignee: You\nSprint: Sprint 42 - UI Components\n\nDescription:\nBuild a mobile-first master-detail layout component that shows both panes on desktop and navigates between panes on mobile. Must include ResizeObserver-based responsive detection, animated transitions, and back navigation.\n\nAcceptance Criteria:\n- Desktop: side-by-side master and detail panes\n- Mobile: single pane with animated navigation\n- Back button in detail view on mobile\n- Empty state when no detail selected',
    date: '2026-02-08 17:45',
    read: true,
    hasAttachment: false,
  },
  {
    id: 7,
    from: 'Lisa Park',
    fromEmail: 'lisa.park@company.com',
    to: 'frontend-team@company.com',
    subject: 'Design System v2.4 Release Notes',
    preview: 'The latest design system update includes new tokens for responsive breakpoints and updated...',
    body: 'Hi Frontend Team,\n\nDesign System v2.4 has been published. Here are the highlights:\n\nNew Features:\n- Responsive breakpoint tokens (sm, md, lg, xl, 2xl)\n- Updated color palette with improved contrast ratios\n- New SplitView pattern documentation\n- Badge component variants (outline, secondary)\n\nBreaking Changes:\n- Deprecated `spacing-xs` token removed (use `spacing-1` instead)\n- Button focus ring now uses ring-offset pattern\n\nMigration Guide:\nSee the design system portal for detailed migration steps. Most changes are backward-compatible.\n\nPlease update your local dependencies with `npm update @company/design-system`.\n\nThanks,\nLisa',
    date: '2026-02-08 10:30',
    read: true,
    hasAttachment: true,
  },
  {
    id: 8,
    from: 'Alex Kim',
    fromEmail: 'alex.kim@company.com',
    to: 'me@company.com',
    subject: 'Lunch tomorrow?',
    preview: 'Hey! Want to grab lunch at that new ramen place downtown tomorrow? I heard they have amazing...',
    body: 'Hey!\n\nWant to grab lunch at that new ramen place downtown tomorrow? I heard they have amazing tonkotsu ramen and the wait is usually not too bad around 11:30.\n\nAlso, I wanted to pick your brain about the meta-model caching strategy. I am working on a similar pattern for the analytics module and could use some guidance.\n\nLet me know!\nAlex',
    date: '2026-02-07 18:15',
    read: true,
    hasAttachment: false,
  },
  {
    id: 9,
    from: 'Security Team',
    fromEmail: 'security@company.com',
    to: 'engineering@company.com',
    subject: 'Mandatory: Update your SSH keys by Feb 28',
    preview: 'As part of our annual security rotation, all engineering SSH keys must be rotated by February 28...',
    body: 'Dear Engineering Team,\n\nAs part of our annual security key rotation policy, all SSH keys used for repository access must be rotated by February 28, 2026.\n\nSteps:\n1. Generate a new ED25519 key: ssh-keygen -t ed25519\n2. Add the new public key to your GitHub profile\n3. Update your local git configuration\n4. Test access: ssh -T git@github.com\n5. Revoke your old key from GitHub\n\nKeys that are not rotated by the deadline will be automatically revoked on March 1.\n\nIf you need assistance, reach out to #security-help on Slack.\n\nSecurity Team',
    date: '2026-02-07 09:00',
    read: true,
    hasAttachment: false,
  },
  {
    id: 10,
    from: 'Newsletter Bot',
    fromEmail: 'digest@techweekly.io',
    to: 'me@company.com',
    subject: 'Tech Weekly: Vue 3.5 Released, TypeScript 6.0 Beta',
    preview: 'This week in tech: Vue 3.5 brings Vapor Mode to stable, TypeScript 6.0 beta adds pattern matching...',
    body: 'TECH WEEKLY DIGEST - February 7, 2026\n\nTop Stories:\n\n1. Vue 3.5 Released with Stable Vapor Mode\nThe Vue team has officially released v3.5, bringing Vapor Mode out of experimental. This compiler-driven rendering approach eliminates the virtual DOM for eligible components, resulting in up to 3x faster initial renders.\n\n2. TypeScript 6.0 Beta: Pattern Matching\nThe TypeScript team has released the 6.0 beta featuring long-awaited pattern matching syntax. The new match expressions provide exhaustive type narrowing.\n\n3. Tailwind CSS v4.1: Container Queries Everywhere\nTailwind v4.1 adds first-class container query support with @container variants.\n\n4. PostgreSQL 18 Preview: JSON Schema Validation\nThe upcoming PostgreSQL 18 release will include built-in JSON Schema validation for jsonb columns.\n\nRead more at techweekly.io',
    date: '2026-02-07 06:00',
    read: true,
    hasAttachment: false,
  },
]

// ─── State ──────────────────────────────────────────────────────────────

const selectedEmail = ref<Email | null>(null)
const splitAppRef = ref<InstanceType<typeof SplitApp> | null>(null)
const currentMode = ref<SplitAppMode>('both')
const _isMobile = computed(() => splitAppRef.value?.isMobile ?? false)

function selectEmail(email: Email): void {
  selectedEmail.value = email
  email.read = true
  splitAppRef.value?.setHasDetail(true)
}

function onModeChange(mode: SplitAppMode): void {
  currentMode.value = mode
}

function onBack(): void {
  // Optionally clear selection on back
}

function formatDate(dateStr: string): string {
  const date = new Date(dateStr)
  const now = new Date()
  const isToday = date.toDateString() === now.toDateString()
  if (isToday) {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  }
  return date.toLocaleDateString([], { month: 'short', day: 'numeric' })
}

const unreadCount = computed(() => emails.filter(e => !e.read).length)
</script>

<template>
  <div class="container mx-auto p-6 space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-bold">Split App</h1>
        <p class="text-muted-foreground">Responsive master-detail layout with mobile navigation</p>
      </div>
      <RouterLink
        to="/showcase"
        class="text-sm text-muted-foreground hover:text-foreground transition-colors"
      >
        &larr; Back to Showcase
      </RouterLink>
    </div>

    <!-- Mode indicator -->
    <div class="flex gap-2 items-center text-sm text-muted-foreground">
      <span>Mode:</span>
      <Badge>{{ currentMode }}</Badge>
      <span v-if="_isMobile" class="flex items-center gap-1">
        <span class="inline-block w-2 h-2 rounded-full bg-orange-400" />
        Mobile
      </span>
      <span v-else class="flex items-center gap-1">
        <span class="inline-block w-2 h-2 rounded-full bg-green-400" />
        Desktop
      </span>
    </div>

    <!-- Split App (in a bordered container with fixed height) -->
    <div class="border border-border rounded-lg h-[600px]">
      <SplitApp
        ref="splitAppRef"
        class="rounded-lg"
        @mode-change="onModeChange"
        @back="onBack"
      >
        <template #master-header>
          <div class="flex items-center justify-between w-full">
            <div class="flex items-center gap-2">
              <Mail class="h-4 w-4" />
              <h2 class="font-semibold">Inbox</h2>
            </div>
            <Badge v-if="unreadCount > 0" variant="secondary" class="text-xs">
              {{ unreadCount }} new
            </Badge>
          </div>
        </template>

        <template #master="{ showDetail }">
          <div
            v-for="email in emails"
            :key="email.id"
            class="px-4 py-3 border-b border-border cursor-pointer hover:bg-muted/30 transition-colors"
            :class="{
              'bg-primary/5 border-l-2 border-l-primary': selectedEmail?.id === email.id,
              'border-l-2 border-l-transparent': selectedEmail?.id !== email.id,
            }"
            @click="selectEmail(email); showDetail()"
          >
            <div class="flex items-center justify-between">
              <span
                class="text-sm truncate"
                :class="email.read ? 'text-muted-foreground' : 'font-semibold text-foreground'"
              >
                {{ email.from }}
              </span>
              <span class="text-xs text-muted-foreground flex-shrink-0 ml-2 flex items-center gap-1">
                <Paperclip v-if="email.hasAttachment" class="h-3 w-3" />
                {{ formatDate(email.date) }}
              </span>
            </div>
            <div
              class="text-sm truncate mt-0.5"
              :class="email.read ? 'text-muted-foreground' : 'text-foreground'"
            >
              {{ email.subject }}
            </div>
            <div class="text-xs text-muted-foreground truncate mt-0.5">
              {{ email.preview }}
            </div>
          </div>
        </template>

        <template #detail-header>
          <h2 class="font-semibold truncate">
            {{ selectedEmail?.subject || 'No email selected' }}
          </h2>
        </template>

        <template #detail>
          <div v-if="selectedEmail" class="p-4 space-y-4">
            <!-- Email metadata -->
            <div class="space-y-2 pb-4 border-b border-border">
              <div class="flex items-start justify-between">
                <div class="flex items-center gap-3">
                  <div class="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
                    <User class="h-5 w-5 text-primary" />
                  </div>
                  <div>
                    <div class="font-medium">{{ selectedEmail.from }}</div>
                    <div class="text-xs text-muted-foreground">{{ selectedEmail.fromEmail }}</div>
                  </div>
                </div>
                <div class="flex items-center gap-1 text-xs text-muted-foreground flex-shrink-0">
                  <Clock class="h-3 w-3" />
                  <span>{{ selectedEmail.date }}</span>
                </div>
              </div>
              <div class="text-xs text-muted-foreground">
                To: {{ selectedEmail.to }}
              </div>
              <div v-if="selectedEmail.hasAttachment" class="flex items-center gap-1 text-xs text-muted-foreground">
                <Paperclip class="h-3 w-3" />
                <span>Has attachments</span>
              </div>
            </div>

            <!-- Email body -->
            <div class="whitespace-pre-wrap text-sm leading-relaxed">{{ selectedEmail.body }}</div>
          </div>
        </template>
      </SplitApp>
    </div>

    <!-- Usage notes -->
    <div class="text-sm text-muted-foreground space-y-1">
      <p><strong>Resize</strong> the browser window to see the responsive behavior:</p>
      <ul class="list-disc ml-5 space-y-0.5">
        <li>Desktop (wide): both panes visible side by side</li>
        <li>Mobile (narrow): single pane with animated navigation</li>
        <li>Back button appears in detail view on mobile</li>
        <li>Container-query based (not window-based) for nested use</li>
      </ul>
    </div>
  </div>
</template>
