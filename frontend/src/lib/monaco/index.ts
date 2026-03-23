export { registerBmmdlLanguage } from './bmmdl-language'
export { registerBmmdlThemes } from './bmmdl-theme'
export { registerBmmdlCompletion } from './bmmdl-completion'
export { registerBmmdlAiCompletion, disposeAiCompletion } from './bmmdl-ai-completion'

import { registerBmmdlLanguage } from './bmmdl-language'
import { registerBmmdlThemes } from './bmmdl-theme'
import { registerBmmdlCompletion } from './bmmdl-completion'
import { registerBmmdlAiCompletion } from './bmmdl-ai-completion'

let initialized = false

export function initBmmdlMonaco() {
  if (initialized) return
  initialized = true
  registerBmmdlLanguage()
  registerBmmdlThemes()
  registerBmmdlCompletion()
  registerBmmdlAiCompletion()
}
