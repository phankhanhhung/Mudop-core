import api from './api'
import type { EntityMetadata } from '@/types/metadata'

export interface NlQueryMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  query?: NlQueryResult
  timestamp: number
}

export interface NlQueryResult {
  filter?: string
  expand?: string
  select?: string
  orderby?: string
  description: string
}

interface NlQueryApiRequest {
  entityType: string
  moduleName: string
  prompt: string
  schemaContext: string
  history?: { role: string; content: string }[]
}

function buildSchemaContext(metadata: EntityMetadata): string {
  const lines: string[] = [`Entity: ${metadata.name}`, 'Fields:']
  for (const field of metadata.fields ?? []) {
    let desc = `  ${field.name}: ${field.type}`
    if (field.isRequired) desc += ' (required)'
    if (field.enumValues && field.enumValues.length > 0) {
      desc += ` [enum: ${field.enumValues.map((e) => e.value).join(', ')}]`
    }
    if (field.maxLength) desc += ` (maxLength: ${field.maxLength})`
    lines.push(desc)
  }
  if (metadata.associations && metadata.associations.length > 0) {
    lines.push('Associations:')
    for (const assoc of metadata.associations) {
      lines.push(`  ${assoc.name} → ${assoc.targetEntity} (${assoc.cardinality ?? 'ManyToOne'})`)
    }
  }
  return lines.join('\n')
}

export const nlQueryService = {
  buildSchemaContext,

  async translate(
    prompt: string,
    metadata: EntityMetadata,
    moduleName: string,
    history?: NlQueryMessage[],
  ): Promise<NlQueryResult> {
    const schemaContext = buildSchemaContext(metadata)
    const historyPayload = history
      ?.filter((m) => m.role === 'user' || m.role === 'assistant')
      .map((m) => ({
        role: m.role,
        content: m.role === 'assistant' && m.query
          ? JSON.stringify(m.query)
          : m.content,
      }))

    const request: NlQueryApiRequest = {
      entityType: metadata.name,
      moduleName,
      prompt,
      schemaContext,
      history: historyPayload && historyPayload.length > 0 ? historyPayload : undefined,
    }

    const response = await api.post<NlQueryResult>('/ai/nl-query', request)
    return response.data
  },
}

export default nlQueryService
