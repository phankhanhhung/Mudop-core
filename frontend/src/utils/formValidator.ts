import { z } from 'zod'
import type { FieldMetadata, FieldType } from '@/types/metadata'
import { formatDate, formatDateTime, formatDecimal, formatBoolean } from '@/utils/formatting'

/**
 * Creates a Zod schema for a field based on its metadata
 */
export function createFieldSchema(field: FieldMetadata): z.ZodTypeAny {
  let schema: z.ZodTypeAny

  switch (field.type) {
    case 'String':
      schema = z.string()
      if (field.maxLength) {
        schema = (schema as z.ZodString).max(
          field.maxLength,
          `Maximum length is ${field.maxLength} characters`
        )
      }
      break

    case 'Integer':
      schema = z.number().int('Must be a whole number')
      break

    case 'Decimal':
      schema = z.number()
      if (field.precision !== undefined && field.scale !== undefined) {
        // Validate decimal places
        schema = (schema as z.ZodNumber).refine(
          (val) => {
            const decimalPart = val.toString().split('.')[1]
            return !decimalPart || decimalPart.length <= field.scale!
          },
          `Maximum ${field.scale} decimal places allowed`
        )
      }
      break

    case 'Boolean':
      schema = z.boolean()
      break

    case 'Date':
      schema = z.string().regex(
        /^\d{4}-\d{2}-\d{2}$/,
        'Invalid date format (YYYY-MM-DD)'
      )
      break

    case 'Time':
      schema = z.string().regex(
        /^\d{2}:\d{2}(:\d{2})?$/,
        'Invalid time format (HH:MM or HH:MM:SS)'
      )
      break

    case 'DateTime':
    case 'Timestamp':
      schema = z.string().datetime('Invalid datetime format')
      break

    case 'UUID':
      schema = z.string().uuid('Invalid UUID format')
      break

    case 'Binary':
      schema = z.string() // Base64 encoded
      break

    case 'Enum':
      if (field.enumValues?.length) {
        const validValues = field.enumValues.map((e) => e.value)
        schema = z.union([
          z.string(),
          z.number()
        ]).refine(
          (val) => validValues.includes(val),
          `Must be one of: ${field.enumValues.map((e) => e.name).join(', ')}`
        )
      } else {
        schema = z.union([z.string(), z.number()])
      }
      break

    case 'Array':
      schema = z.array(z.unknown())
      break

    default:
      schema = z.unknown()
  }

  // Handle optionality
  if (!field.isRequired) {
    schema = schema.optional().nullable()
  }

  return schema
}

/**
 * Creates a Zod schema for an entire entity based on its metadata
 */
export function createEntitySchema(
  fields: FieldMetadata[],
  mode: 'create' | 'update' = 'create'
): z.ZodObject<Record<string, z.ZodTypeAny>> {
  const shape: Record<string, z.ZodTypeAny> = {}

  for (const field of fields) {
    // Skip read-only and computed fields
    if (field.isReadOnly || field.isComputed) {
      continue
    }

    let fieldSchema = createFieldSchema(field)

    // In update mode, all fields are optional
    if (mode === 'update' && field.isRequired) {
      fieldSchema = fieldSchema.optional()
    }

    shape[field.name] = fieldSchema
  }

  return z.object(shape)
}

/**
 * Validates form data against entity metadata
 */
export interface ValidationResult {
  success: boolean
  data?: Record<string, unknown>
  errors?: Record<string, string>
}

export function validateFormData(
  data: Record<string, unknown>,
  fields: FieldMetadata[],
  mode: 'create' | 'update' = 'create'
): ValidationResult {
  const schema = createEntitySchema(fields, mode)
  const result = schema.safeParse(data)

  if (result.success) {
    return {
      success: true,
      data: result.data
    }
  }

  // Convert Zod errors to field-keyed object
  const errors: Record<string, string> = {}
  for (const issue of result.error.issues) {
    const path = issue.path.join('.')
    if (!errors[path]) {
      errors[path] = issue.message
    }
  }

  return {
    success: false,
    errors
  }
}

/**
 * Gets the HTML input type for a field type
 */
export function getInputType(fieldType: FieldType): string {
  switch (fieldType) {
    case 'String':
    case 'UUID':
      return 'text'
    case 'Integer':
    case 'Decimal':
      return 'number'
    case 'Boolean':
      return 'checkbox'
    case 'Date':
      return 'date'
    case 'Time':
      return 'time'
    case 'DateTime':
    case 'Timestamp':
      return 'datetime-local'
    default:
      return 'text'
  }
}

/**
 * Formats a value for display based on field type
 */
export function formatValue(
  value: unknown,
  fieldType: FieldType,
  enumValues?: { name: string; value: unknown }[]
): string {
  if (value === null || value === undefined) {
    return '-'
  }

  switch (fieldType) {
    case 'Boolean':
      return formatBoolean(value)

    case 'Date':
      return formatDate(value as string)

    case 'DateTime':
    case 'Timestamp':
      return formatDateTime(value as string)

    case 'Enum':
      if (enumValues) {
        const enumItem = enumValues.find((e) => e.value === value)
        return enumItem?.name ?? String(value)
      }
      return String(value)

    case 'Decimal':
      return typeof value === 'number' ? formatDecimal(value) : String(value)

    default:
      return String(value)
  }
}

/**
 * Parses a form input value to the appropriate type
 */
export function parseInputValue(value: string, fieldType: FieldType): unknown {
  if (value === '' || value === null || value === undefined) {
    return null
  }

  switch (fieldType) {
    case 'Integer':
      return parseInt(value, 10)
    case 'Decimal':
      return parseFloat(value)
    case 'Boolean':
      return value === 'true' || value === '1'
    default:
      return value
  }
}
