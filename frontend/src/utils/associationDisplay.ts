import type { AssociationMetadata } from '@/types/metadata'

/**
 * Case-insensitive field access for OData responses (which may use PascalCase).
 */
export function getField(obj: Record<string, unknown>, name: string): unknown {
  if (name in obj) return obj[name]
  const lower = name.toLowerCase()
  for (const key of Object.keys(obj)) {
    if (key.toLowerCase() === lower) return obj[key]
  }
  return undefined
}

/**
 * Extract the best display value from an expanded navigation object.
 * Priority: Name > Title > DisplayName > Code > first non-ID string > ID
 */
export function getExpandedDisplayValue(expandedObj: unknown): string | null {
  if (!expandedObj || typeof expandedObj !== 'object') return null

  const obj = expandedObj as Record<string, unknown>
  const preferred = ['name', 'title', 'displayName', 'display_name', 'code']

  for (const pref of preferred) {
    const val = getField(obj, pref)
    if (val != null && val !== '') return String(val)
  }

  // First non-ID string value
  for (const [key, val] of Object.entries(obj)) {
    if (
      typeof val === 'string' &&
      val !== '' &&
      !key.toLowerCase().endsWith('id') &&
      !key.startsWith('@')
    ) {
      return val
    }
  }

  // Fallback to ID
  const id = getField(obj, 'id')
  if (id != null) return String(id)

  return null
}

/**
 * Find the association metadata for a given FK field name.
 */
export function findAssociationForField(
  fieldName: string,
  associations: AssociationMetadata[]
): AssociationMetadata | undefined {
  return associations.find((a) => a.foreignKey === fieldName)
}
