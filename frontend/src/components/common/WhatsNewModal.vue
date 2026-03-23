<script setup lang="ts">
import { useOnboarding, CURRENT_VERSION } from '@/composables/useOnboarding'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { X, Sparkles } from 'lucide-vue-next'

const props = defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  close: []
}>()

const { dismissWhatsNew } = useOnboarding()

function handleClose() {
  dismissWhatsNew()
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition ease-out duration-200"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition ease-in duration-150"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="props.open"
        class="fixed inset-0 z-[100] flex items-center justify-center bg-black/50"
        @keydown.escape="handleClose"
      >
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="whats-new-title"
          class="w-full max-w-md mx-4"
        >
        <Card class="shadow-xl">
          <CardContent class="p-0">
            <!-- Header -->
            <div class="flex items-center justify-between border-b px-6 py-4">
              <div class="flex items-center gap-2">
                <Sparkles class="h-5 w-5 text-primary" />
                <h2 id="whats-new-title" class="text-lg font-semibold">
                  {{ $t('whatsNew.title', { version: CURRENT_VERSION }) }}
                </h2>
              </div>
              <Button
                variant="ghost"
                size="icon"
                :aria-label="$t('common.cancel')"
                @click="handleClose"
              >
                <X class="h-5 w-5" />
              </Button>
            </div>

            <!-- Content -->
            <div class="px-6 py-4 max-h-80 overflow-y-auto">
              <ul class="space-y-3">
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.odata') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.deepInsert') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.batch') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.metadata') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.i18n') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.temporal') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.events') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.rules') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.pwa') }}</span>
                </li>
                <li class="flex items-start gap-2">
                  <span class="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-primary" />
                  <span class="text-sm">{{ $t('whatsNew.items.accessibility') }}</span>
                </li>
              </ul>
            </div>

            <!-- Footer -->
            <div class="flex justify-end border-t px-6 py-4">
              <Button @click="handleClose">
                {{ $t('whatsNew.close') }}
              </Button>
            </div>
          </CardContent>
        </Card>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>
