<script setup lang="ts">
import { ref, computed } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Card, CardHeader, CardTitle, CardContent } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import SmartField from '@/components/smart/SmartField.vue'
import type { FieldMetadata } from '@/types/metadata'
import {
  Clock,
  Calendar,
  DollarSign,
  ListChecks,
  Hash,
  Eye,
  Pencil,
  RotateCcw,
  ArrowLeft,
} from 'lucide-vue-next'

// ─── Mode toggle ──────────────────────────────────────────────────────────

const mode = ref<'display' | 'edit'>('edit')

function toggleMode() {
  mode.value = mode.value === 'edit' ? 'display' : 'edit'
}

// ─── 1. TimePicker ────────────────────────────────────────────────────────

const timeField: FieldMetadata = {
  name: 'MeetingTime',
  type: 'Time',
  displayName: 'Meeting Time',
  description: 'Scheduled time for the team sync',
  isRequired: true,
  isReadOnly: false,
  isComputed: false,
  annotations: {},
}

const timeFieldReadonly: FieldMetadata = {
  name: 'ClosingTime',
  type: 'Time',
  displayName: 'Store Closing Time',
  isRequired: false,
  isReadOnly: true,
  isComputed: true,
  annotations: {},
}

// ─── 2. CurrencyField ────────────────────────────────────────────────────

const currencyUsd: FieldMetadata = {
  name: 'SalePrice',
  type: 'Decimal',
  displayName: 'Sale Price (USD)',
  description: 'Product retail price in US Dollars',
  isRequired: true,
  isReadOnly: false,
  isComputed: false,
  precision: 18,
  scale: 2,
  annotations: { '@Semantics.CurrencyCode': 'USD' },
}

const currencyEur: FieldMetadata = {
  name: 'ImportCost',
  type: 'Decimal',
  displayName: 'Import Cost (EUR)',
  description: 'Supplier cost in Euros',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  precision: 18,
  scale: 2,
  annotations: { '@Semantics.CurrencyCode': 'EUR' },
}

const currencyJpy: FieldMetadata = {
  name: 'ListPrice',
  type: 'Decimal',
  displayName: 'List Price (JPY)',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  precision: 18,
  scale: 0,
  annotations: { '@Semantics.CurrencyCode': 'JPY' },
}

// ─── 3. DateRange ─────────────────────────────────────────────────────────

const dateRangeField: FieldMetadata = {
  name: 'ContractPeriod',
  type: 'Date',
  displayName: 'Contract Period',
  description: 'Start and end dates of the service agreement',
  isRequired: true,
  isReadOnly: false,
  isComputed: false,
  annotations: { '@UI.DateRange': true },
}

const dateRangeField2: FieldMetadata = {
  name: 'FiscalQuarter',
  type: 'Date',
  displayName: 'Reporting Period',
  description: 'Select a date range for the financial report',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  annotations: { '@UI.DateRange': true },
}

// ─── 4. MultiComboBox ─────────────────────────────────────────────────────

const multiSelectField: FieldMetadata = {
  name: 'Categories',
  type: 'Enum',
  displayName: 'Product Categories',
  description: 'Select one or more categories',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  enumValues: [
    { name: 'Electronics', value: 'electronics', displayName: 'Electronics' },
    { name: 'Clothing', value: 'clothing', displayName: 'Clothing' },
    { name: 'Books', value: 'books', displayName: 'Books' },
    { name: 'HomeGarden', value: 'home', displayName: 'Home & Garden' },
    { name: 'Sports', value: 'sports', displayName: 'Sports & Outdoors' },
    { name: 'Toys', value: 'toys', displayName: 'Toys & Games' },
    { name: 'Automotive', value: 'auto', displayName: 'Automotive' },
    { name: 'Health', value: 'health', displayName: 'Health & Beauty' },
  ],
  annotations: { '@UI.MultiSelect': true },
}

const multiSelectPriority: FieldMetadata = {
  name: 'AssignedTeams',
  type: 'Enum',
  displayName: 'Assigned Teams',
  description: 'Teams responsible for this project',
  isRequired: true,
  isReadOnly: false,
  isComputed: false,
  enumValues: [
    { name: 'Frontend', value: 'fe', displayName: 'Frontend' },
    { name: 'Backend', value: 'be', displayName: 'Backend' },
    { name: 'DevOps', value: 'devops', displayName: 'DevOps' },
    { name: 'QA', value: 'qa', displayName: 'QA' },
    { name: 'Design', value: 'design', displayName: 'Design' },
  ],
  annotations: { '@UI.MultiSelect': true },
}

// ─── 5. MaskedInput ───────────────────────────────────────────────────────

const maskedPhone: FieldMetadata = {
  name: 'Phone',
  type: 'String',
  displayName: 'Phone Number',
  description: 'US phone number format',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 14,
  annotations: { '@UI.InputMask': 'phone' },
}

const maskedCreditCard: FieldMetadata = {
  name: 'CardNumber',
  type: 'String',
  displayName: 'Credit Card',
  description: '16-digit card number',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 19,
  annotations: { '@UI.InputMask': 'creditCard' },
}

const maskedSsn: FieldMetadata = {
  name: 'SSN',
  type: 'String',
  displayName: 'Social Security Number',
  description: 'US SSN format',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 11,
  annotations: { '@UI.InputMask': 'ssn' },
}

const maskedPostal: FieldMetadata = {
  name: 'PostalCode',
  type: 'String',
  displayName: 'ZIP Code',
  description: 'US postal code',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 5,
  annotations: { '@UI.InputMask': 'postalCode' },
}

const maskedCustom: FieldMetadata = {
  name: 'ProductCode',
  type: 'String',
  displayName: 'Product Code',
  description: 'Format: AA-####-AA (custom mask)',
  isRequired: false,
  isReadOnly: false,
  isComputed: false,
  maxLength: 10,
  annotations: { '@UI.InputMask': 'AA-####-AA' },
}

// ─── Values ───────────────────────────────────────────────────────────────

const values = ref<Record<string, unknown>>({
  MeetingTime: '14:30:00',
  ClosingTime: '18:00:00',
  SalePrice: 1249.99,
  ImportCost: 892.50,
  ListPrice: 158000,
  ContractPeriod: { from: '2026-01-01', to: '2026-12-31' },
  FiscalQuarter: null,
  Categories: ['electronics', 'books'],
  AssignedTeams: ['fe', 'be', 'qa'],
  Phone: '5551234567',
  CardNumber: '4111111111111111',
  SSN: '123456789',
  PostalCode: '94105',
  ProductCode: 'AB1234CD',
})

function updateValue(field: string, val: unknown) {
  values.value[field] = val
}

function resetAll() {
  values.value = {
    MeetingTime: '14:30:00',
    ClosingTime: '18:00:00',
    SalePrice: 1249.99,
    ImportCost: 892.50,
    ListPrice: 158000,
    ContractPeriod: { from: '2026-01-01', to: '2026-12-31' },
    FiscalQuarter: null,
    Categories: ['electronics', 'books'],
    AssignedTeams: ['fe', 'be', 'qa'],
    Phone: '5551234567',
    CardNumber: '4111111111111111',
    SSN: '123456789',
    PostalCode: '94105',
    ProductCode: 'AB1234CD',
  }
}

// JSON state viewer
const jsonState = computed(() => JSON.stringify(values.value, null, 2))
</script>

<template>
  <DefaultLayout>
    <div class="space-y-8 pb-12">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <div class="flex items-center gap-2 mb-1">
            <router-link to="/showcase" class="text-muted-foreground hover:text-foreground transition-colors">
              <ArrowLeft class="h-5 w-5" />
            </router-link>
            <h1 class="text-3xl font-bold text-foreground">Advanced Field Types</h1>
          </div>
          <p class="text-muted-foreground">
            5 annotation-driven field components for SAP Fiori / OpenUI5 parity
          </p>
        </div>
        <div class="flex items-center gap-2">
          <Button size="sm" variant="outline" @click="resetAll">
            <RotateCcw class="h-4 w-4 mr-1" />
            Reset
          </Button>
          <Button size="sm" :variant="mode === 'edit' ? 'default' : 'secondary'" @click="toggleMode">
            <component :is="mode === 'edit' ? Eye : Pencil" class="h-4 w-4 mr-1" />
            {{ mode === 'edit' ? 'View Display Mode' : 'View Edit Mode' }}
          </Button>
        </div>
      </div>

      <!-- ================================================================ -->
      <!-- 1. TimePicker                                                     -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
              <Clock class="h-5 w-5 text-primary" />
            </div>
            <div>
              <CardTitle>TimePicker</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Replaces native <code class="text-xs bg-muted px-1 rounded">&lt;input type="time"&gt;</code> for
                <Badge variant="secondary" class="text-xs ml-1">field.type = "Time"</Badge>
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">Editable time field</p>
              <SmartField
                :field="timeField"
                :modelValue="values.MeetingTime"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('MeetingTime', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">Read-only (computed)</p>
              <SmartField
                :field="timeFieldReadonly"
                :modelValue="values.ClosingTime"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('ClosingTime', v)"
              />
            </div>
          </div>
          <div class="mt-4 rounded-md bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">
              Auto-detects 12h/24h from locale. Scrollable hour/minute/second columns with AM/PM toggle.
              Keyboard: Arrow keys adjust, Tab between columns, Enter confirm, Escape close.
              Emits ISO time string <code class="bg-muted px-1 rounded">"HH:mm:ss"</code>.
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- 2. CurrencyField                                                  -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-emerald-500/10">
              <DollarSign class="h-5 w-5 text-emerald-600" />
            </div>
            <div>
              <CardTitle>CurrencyField</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Triggered by
                <Badge variant="secondary" class="text-xs ml-1">@Semantics.CurrencyCode</Badge>
                annotation on Decimal fields
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">USD — US Dollar</p>
              <SmartField
                :field="currencyUsd"
                :modelValue="values.SalePrice"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('SalePrice', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">EUR — Euro</p>
              <SmartField
                :field="currencyEur"
                :modelValue="values.ImportCost"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('ImportCost', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">JPY — Japanese Yen (no decimals)</p>
              <SmartField
                :field="currencyJpy"
                :modelValue="values.ListPrice"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('ListPrice', v)"
              />
            </div>
          </div>
          <div class="mt-4 rounded-md bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">
              Currency symbol resolved from <code class="bg-muted px-1 rounded">Intl.NumberFormat</code>.
              Shows formatted value on blur (e.g. "$1,249.99"), raw number while editing.
              Uses <code class="bg-muted px-1 rounded">formatCurrency()</code> from utils/formatting.ts.
              Emits <code class="bg-muted px-1 rounded">number | null</code>.
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- 3. DateRange                                                      -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-blue-500/10">
              <Calendar class="h-5 w-5 text-blue-600" />
            </div>
            <div>
              <CardTitle>DateRangeField</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Triggered by
                <Badge variant="secondary" class="text-xs ml-1">@UI.DateRange: true</Badge>
                annotation on Date fields
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">With pre-populated range</p>
              <SmartField
                :field="dateRangeField"
                :modelValue="values.ContractPeriod"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('ContractPeriod', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">Empty — try the presets</p>
              <SmartField
                :field="dateRangeField2"
                :modelValue="values.FiscalQuarter"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('FiscalQuarter', v)"
              />
            </div>
          </div>
          <div class="mt-4 rounded-md bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">
              Calendar grid with range highlighting. Quick presets: Today, Last 7 Days, This Month, This Quarter.
              Click once for start date, click again for end date. Validates from &lt;= to.
              Emits <code class="bg-muted px-1 rounded">{"{"} from: "YYYY-MM-DD", to: "YYYY-MM-DD" {"}"}</code>.
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- 4. MultiComboBox                                                  -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-violet-500/10">
              <ListChecks class="h-5 w-5 text-violet-600" />
            </div>
            <div>
              <CardTitle>MultiComboBox</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Triggered by
                <Badge variant="secondary" class="text-xs ml-1">@UI.MultiSelect: true</Badge>
                on Enum fields, or <code class="text-xs bg-muted px-1 rounded">field.type === "Array"</code>
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">Product categories (8 options)</p>
              <SmartField
                :field="multiSelectField"
                :modelValue="values.Categories"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('Categories', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">Assigned teams (required)</p>
              <SmartField
                :field="multiSelectPriority"
                :modelValue="values.AssignedTeams"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('AssignedTeams', v)"
              />
            </div>
          </div>
          <div class="mt-4 rounded-md bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">
              Removable tag chips inside input. Searchable dropdown with checkboxes.
              Keyboard: ArrowDown opens, Space/Enter toggles, Backspace removes last tag.
              Emits <code class="bg-muted px-1 rounded">(string | number)[]</code>.
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- 5. MaskedInput                                                    -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <div class="flex items-center gap-3">
            <div class="flex h-10 w-10 items-center justify-center rounded-lg bg-amber-500/10">
              <Hash class="h-5 w-5 text-amber-600" />
            </div>
            <div>
              <CardTitle>MaskedInput</CardTitle>
              <p class="text-sm text-muted-foreground mt-0.5">
                Triggered by
                <Badge variant="secondary" class="text-xs ml-1">@UI.InputMask</Badge>
                annotation on String fields
              </p>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <div class="grid grid-cols-1 md:grid-cols-3 gap-6">
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">
                <code class="bg-muted px-1 rounded text-xs">"phone"</code> pattern
              </p>
              <SmartField
                :field="maskedPhone"
                :modelValue="values.Phone"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('Phone', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">
                <code class="bg-muted px-1 rounded text-xs">"creditCard"</code> pattern
              </p>
              <SmartField
                :field="maskedCreditCard"
                :modelValue="values.CardNumber"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('CardNumber', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">
                <code class="bg-muted px-1 rounded text-xs">"ssn"</code> pattern
              </p>
              <SmartField
                :field="maskedSsn"
                :modelValue="values.SSN"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('SSN', v)"
              />
            </div>
            <div>
              <p class="text-xs font-medium text-muted-foreground mb-3">
                <code class="bg-muted px-1 rounded text-xs">"postalCode"</code> pattern
              </p>
              <SmartField
                :field="maskedPostal"
                :modelValue="values.PostalCode"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('PostalCode', v)"
              />
            </div>
            <div class="md:col-span-2">
              <p class="text-xs font-medium text-muted-foreground mb-3">
                Custom pattern: <code class="bg-muted px-1 rounded text-xs">"AA-####-AA"</code>
                <span class="text-muted-foreground/60 ml-1">(# = digit, A = letter, * = any)</span>
              </p>
              <SmartField
                :field="maskedCustom"
                :modelValue="values.ProductCode"
                :mode="mode"
                @update:modelValue="(v: unknown) => updateValue('ProductCode', v)"
              />
            </div>
          </div>
          <div class="mt-4 rounded-md bg-muted/50 p-3">
            <p class="text-xs text-muted-foreground">
              Built-in patterns: <code class="bg-muted px-1 rounded">phone</code>,
              <code class="bg-muted px-1 rounded">creditCard</code>,
              <code class="bg-muted px-1 rounded">postalCode</code>,
              <code class="bg-muted px-1 rounded">ssn</code>.
              Custom patterns use <code class="bg-muted px-1 rounded">#</code> = digit,
              <code class="bg-muted px-1 rounded">A</code> = letter,
              <code class="bg-muted px-1 rounded">*</code> = any.
              Auto-formats as user types with correct cursor positioning.
              Emits raw unmasked value.
            </p>
          </div>
        </CardContent>
      </Card>

      <!-- ================================================================ -->
      <!-- Live State Viewer                                                 -->
      <!-- ================================================================ -->
      <Card>
        <CardHeader>
          <CardTitle>Live State</CardTitle>
        </CardHeader>
        <CardContent>
          <pre class="text-xs bg-muted rounded-md p-4 overflow-x-auto max-h-96 overflow-y-auto">{{ jsonState }}</pre>
        </CardContent>
      </Card>
    </div>
  </DefaultLayout>
</template>
