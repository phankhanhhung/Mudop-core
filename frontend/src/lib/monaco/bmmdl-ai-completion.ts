import * as monaco from 'monaco-editor'
import { aiService } from '@/services/aiService'

const CONTEXT_LINES_BEFORE = 30
const CONTEXT_LINES_AFTER = 5
const DEBOUNCE_MS = 600

// Guard: inline completions disabled if env var is explicitly false
const ENABLED = import.meta.env.VITE_AI_INLINE_COMPLETIONS !== 'false'

let _disposable: monaco.IDisposable | null = null
let _debounceTimer: ReturnType<typeof setTimeout> | null = null

export function registerBmmdlAiCompletion(): void {
  if (!ENABLED || _disposable) return

  _disposable = monaco.languages.registerInlineCompletionsProvider('bmmdl', {
    provideInlineCompletions(
      model: monaco.editor.ITextModel,
      position: monaco.Position,
      _context: monaco.languages.InlineCompletionContext,
      token: monaco.CancellationToken,
    ): Promise<monaco.languages.InlineCompletions> {
      return new Promise((resolve) => {
        if (_debounceTimer) clearTimeout(_debounceTimer)

        _debounceTimer = setTimeout(async () => {
          if (token.isCancellationRequested) {
            resolve({ items: [] })
            return
          }

          const lineCount = model.getLineCount()
          const startLine = Math.max(1, position.lineNumber - CONTEXT_LINES_BEFORE)
          const endLine = Math.min(lineCount, position.lineNumber + CONTEXT_LINES_AFTER)
          const endCol = model.getLineMaxColumn(endLine)

          const contextText = model.getValueInRange({
            startLineNumber: startLine,
            startColumn: 1,
            endLineNumber: endLine,
            endColumn: endCol,
          })

          try {
            const resp = await aiService.assist({
              operation: 'complete',
              context: contextText,
              cursorLine: position.lineNumber - startLine + 1,
              cursorColumn: position.column,
            })

            if (token.isCancellationRequested || !resp.result.trim()) {
              resolve({ items: [] })
              return
            }

            resolve({
              items: [
                {
                  insertText: resp.result,
                  range: {
                    startLineNumber: position.lineNumber,
                    startColumn: position.column,
                    endLineNumber: position.lineNumber,
                    endColumn: position.column,
                  },
                },
              ],
            })
          } catch {
            resolve({ items: [] })
          }
        }, DEBOUNCE_MS)
      })
    },

    disposeInlineCompletions(): void {},
  })
}

export function disposeAiCompletion(): void {
  if (_debounceTimer) {
    clearTimeout(_debounceTimer)
    _debounceTimer = null
  }
  _disposable?.dispose()
  _disposable = null
}
