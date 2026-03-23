<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import FlexibleColumnLayout from '@/components/layout/FlexibleColumnLayout.vue'
import { useFcl } from '@/composables/useFcl'
import type { FclLayout } from '@/composables/useFcl'

const props = withDefaults(defineProps<{
  layout?: FclLayout
}>(), {
  layout: 'OneColumn',
})

const fcl = useFcl(props.layout)

defineExpose({ fcl })
</script>

<template>
  <DefaultLayout>
    <FlexibleColumnLayout
      :layout="fcl.effectiveLayout.value"
      @layout-change="fcl.setLayout"
    >
      <template #begin>
        <slot name="begin" />
      </template>
      <template #mid>
        <slot name="mid" />
      </template>
      <template #end>
        <slot name="end" />
      </template>
    </FlexibleColumnLayout>
  </DefaultLayout>
</template>
