import { computed } from 'vue'
import type { Ref } from 'vue'
import { useRoute } from 'vue-router'
import { formatValue } from '@/utils/formValidator'
import { findAssociationForField, getExpandedDisplayValue } from '@/utils/associationDisplay'
import type { FieldMetadata, EntityMetadata, AssociationMetadata } from '@/types/metadata'

export function useAssociationHelpers(
  _fields: Ref<FieldMetadata[]>,
  metadata: Ref<EntityMetadata | null>,
  rowData: Ref<Record<string, unknown> | null>
) {
  const route = useRoute()
  const currentModule = computed(() => route.params.module as string)

  // Expandable associations: associations where cardinality is ZeroOrOne or One and foreignKey exists
  const expandableAssociations = computed<AssociationMetadata[]>(() => {
    if (!metadata.value) return []
    return metadata.value.associations.filter(
      (a) =>
        a.foreignKey &&
        (a.cardinality === 'ZeroOrOne' || a.cardinality === 'One')
    )
  })

  // Formats a cell value for display
  function getFormattedValue(field: FieldMetadata): string {
    const value = rowData.value?.[field.name]
    const assoc = findAssociationForField(field.name, expandableAssociations.value)
    if (assoc && rowData.value) {
      const expanded = rowData.value[assoc.name]
      const display = getExpandedDisplayValue(expanded)
      if (display) return display
    }
    return formatValue(value, field.type, field.enumValues)
  }

  // Returns router link for association FK value
  function getAssociationLink(field: FieldMetadata): string | null {
    const assoc = findAssociationForField(field.name, expandableAssociations.value)
    if (!assoc || !rowData.value) return null
    const fkValue = rowData.value[field.name]
    if (!fkValue) return null
    const target = assoc.targetEntity
    const lastDot = target.lastIndexOf('.')
    const targetModule = lastDot >= 0 ? target.substring(0, lastDot) : currentModule.value
    const targetEntity = lastDot >= 0 ? target.substring(lastDot + 1) : target
    return `/odata/${targetModule}/${targetEntity}/${fkValue}`
  }

  // Returns true if field is an FK/association field
  function isAssociationField(field: FieldMetadata): boolean {
    return !!findAssociationForField(field.name, expandableAssociations.value)
  }

  return { expandableAssociations, getFormattedValue, getAssociationLink, isAssociationField }
}
