<script setup lang="ts">
import { ref, nextTick, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Send, Pin, Play } from 'lucide-vue-next'
import type { NlQueryMessage, NlQueryResult } from '@/services/nlQueryService'

interface Props {
  messages: NlQueryMessage[]
  loading?: boolean
  entityType: string
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
})

const emit = defineEmits<{
  send: [text: string]
  runQuery: [result: NlQueryResult]
  pinQuery: [result: NlQueryResult]
}>()

const { t } = useI18n()
const inputText = ref('')
const messagesContainer = ref<HTMLElement | null>(null)

function handleSend() {
  const text = inputText.value.trim()
  if (!text || props.loading) return
  inputText.value = ''
  emit('send', text)
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    handleSend()
  }
}

// Auto-scroll to bottom when messages change
watch(
  () => props.messages.length,
  async () => {
    await nextTick()
    if (messagesContainer.value) {
      messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
    }
  },
)
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Messages area -->
    <div
      ref="messagesContainer"
      class="flex-1 overflow-y-auto p-4 space-y-4 min-h-0"
    >
      <!-- Empty state -->
      <div v-if="messages.length === 0" class="flex flex-col items-center justify-center h-full text-center text-gray-400 py-12">
        <div class="text-4xl mb-3">💬</div>
        <p class="text-sm font-medium text-gray-500">{{ t('nlq.emptyHint') }}</p>
        <p class="text-xs text-gray-400 mt-1">{{ t('nlq.exampleHint', { entity: entityType }) }}</p>
      </div>

      <!-- Messages -->
      <template v-for="message in messages" :key="message.id">
        <!-- User message -->
        <div v-if="message.role === 'user'" class="flex justify-end">
          <div class="max-w-[80%] bg-blue-600 text-white rounded-2xl rounded-tr-sm px-4 py-2.5 text-sm">
            {{ message.content }}
          </div>
        </div>

        <!-- Assistant message -->
        <div v-else class="flex justify-start">
          <div class="max-w-[85%] space-y-2">
            <!-- Text explanation -->
            <div class="bg-gray-100 dark:bg-gray-800 rounded-2xl rounded-tl-sm px-4 py-2.5 text-sm text-gray-800 dark:text-gray-200">
              {{ message.query?.description ?? message.content }}
            </div>

            <!-- Query preview card -->
            <div
              v-if="message.query && (message.query.filter || message.query.expand || message.query.orderby)"
              class="bg-white dark:bg-gray-900 border border-gray-200 dark:border-gray-700 rounded-xl p-3 text-xs space-y-1.5"
            >
              <div class="font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide text-[10px]">
                {{ t('nlq.queryPreview') }}
              </div>
              <div v-if="message.query.filter" class="font-mono text-gray-700 dark:text-gray-300 bg-gray-50 dark:bg-gray-800 rounded px-2 py-1 break-all">
                <span class="text-blue-500">$filter</span>=<span>{{ message.query.filter }}</span>
              </div>
              <div v-if="message.query.expand" class="font-mono text-gray-700 dark:text-gray-300 bg-gray-50 dark:bg-gray-800 rounded px-2 py-1 break-all">
                <span class="text-green-500">$expand</span>=<span>{{ message.query.expand }}</span>
              </div>
              <div v-if="message.query.orderby" class="font-mono text-gray-700 dark:text-gray-300 bg-gray-50 dark:bg-gray-800 rounded px-2 py-1 break-all">
                <span class="text-purple-500">$orderby</span>=<span>{{ message.query.orderby }}</span>
              </div>
              <!-- Action buttons -->
              <div class="flex gap-2 pt-1">
                <button
                  class="flex items-center gap-1 px-2 py-1 bg-blue-600 hover:bg-blue-700 text-white rounded text-xs font-medium transition-colors"
                  @click="emit('runQuery', message.query!)"
                >
                  <Play class="h-3 w-3" />
                  {{ t('nlq.runQuery') }}
                </button>
                <button
                  class="flex items-center gap-1 px-2 py-1 bg-gray-100 hover:bg-gray-200 dark:bg-gray-800 dark:hover:bg-gray-700 text-gray-700 dark:text-gray-300 rounded text-xs font-medium transition-colors"
                  @click="emit('pinQuery', message.query!)"
                >
                  <Pin class="h-3 w-3" />
                  {{ t('nlq.pinQuery') }}
                </button>
              </div>
            </div>
          </div>
        </div>
      </template>

      <!-- Loading indicator -->
      <div v-if="loading" class="flex justify-start">
        <div class="bg-gray-100 dark:bg-gray-800 rounded-2xl rounded-tl-sm px-4 py-2.5">
          <div class="flex gap-1">
            <div class="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style="animation-delay: 0ms" />
            <div class="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style="animation-delay: 150ms" />
            <div class="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style="animation-delay: 300ms" />
          </div>
        </div>
      </div>
    </div>

    <!-- Input area -->
    <div class="border-t border-gray-200 dark:border-gray-700 p-3">
      <div class="flex gap-2 items-end">
        <textarea
          v-model="inputText"
          :placeholder="t('nlq.inputPlaceholder', { entity: entityType })"
          :disabled="loading"
          rows="2"
          class="flex-1 resize-none rounded-xl border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-900 text-sm text-gray-900 dark:text-gray-100 placeholder-gray-400 px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent disabled:opacity-50"
          @keydown="handleKeydown"
        />
        <button
          :disabled="!inputText.trim() || loading"
          class="p-2.5 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed text-white rounded-xl transition-colors"
          :aria-label="t('nlq.send')"
          @click="handleSend"
        >
          <Send class="h-4 w-4" />
        </button>
      </div>
    </div>
  </div>
</template>
