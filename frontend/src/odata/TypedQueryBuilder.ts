/**
 * Type-Safe OData Query Builder — fluent, chainable query construction.
 *
 * Provides a fluent API for building OData queries with runtime validation.
 * For full compile-time type safety, use with generated entity interfaces.
 *
 * Usage:
 *   // Basic query
 *   const query = ODataQuery.from('Customers')
 *     .filter(f => f.field('name').contains('John'))
 *     .select('Id', 'name', 'email')
 *     .expand('Orders', e => e.select('Id', 'total').top(5))
 *     .orderBy('name', 'asc')
 *     .top(20)
 *     .skip(0)
 *     .count()
 *     .build()
 *
 *   // Result: { $filter, $select, $expand, $orderby, $top, $skip, $count }
 *
 *   // Complex nested expand
 *   ODataQuery.from('Orders')
 *     .expand('Customer', c => c
 *       .select('name', 'email')
 *       .expand('Address')
 *     )
 *     .expand('Items', i => i
 *       .filter(f => f.field('quantity').gt(0))
 *       .orderBy('lineNumber')
 *     )
 *     .build()
 */

import type { ODataQueryOptions, FilterCondition } from '@/types/odata'
import { buildODataFilter, formatODataValue } from '@/utils/odataQueryBuilder'

// ---------------------------------------------------------------------------
// Field Filter Builder
// ---------------------------------------------------------------------------

export class FieldFilter {
  constructor(private fieldName: string) {}

  eq(value: unknown): FilterCondition {
    return { field: this.fieldName, operator: 'eq', value }
  }

  ne(value: unknown): FilterCondition {
    return { field: this.fieldName, operator: 'ne', value }
  }

  gt(value: unknown): FilterCondition {
    return { field: this.fieldName, operator: 'gt', value }
  }

  ge(value: unknown): FilterCondition {
    return { field: this.fieldName, operator: 'ge', value }
  }

  lt(value: unknown): FilterCondition {
    return { field: this.fieldName, operator: 'lt', value }
  }

  le(value: unknown): FilterCondition {
    return { field: this.fieldName, operator: 'le', value }
  }

  contains(value: string): FilterCondition {
    return { field: this.fieldName, operator: 'contains', value }
  }

  startsWith(value: string): FilterCondition {
    return { field: this.fieldName, operator: 'startswith', value }
  }

  endsWith(value: string): FilterCondition {
    return { field: this.fieldName, operator: 'endswith', value }
  }

  between(min: unknown, max: unknown): FilterCondition[] {
    return [
      { field: this.fieldName, operator: 'ge', value: min },
      { field: this.fieldName, operator: 'le', value: max },
    ]
  }

  isNull(): FilterCondition {
    return { field: this.fieldName, operator: 'eq', value: null }
  }

  isNotNull(): FilterCondition {
    return { field: this.fieldName, operator: 'ne', value: null }
  }

  in(values: unknown[]): string {
    // OData 4: field in (val1, val2, val3)
    const formatted = values.map(v => formatODataValue(v)).join(',')
    return `${this.fieldName} in (${formatted})`
  }
}

// ---------------------------------------------------------------------------
// Filter Builder (callback-style)
// ---------------------------------------------------------------------------

export class FilterBuilder {
  private conditions: FilterCondition[] = []
  private rawExpressions: string[] = []
  private logic: 'and' | 'or' = 'and'

  field(name: string): FieldFilter {
    return new FieldFilter(name)
  }

  add(condition: FilterCondition | FilterCondition[]): this {
    if (Array.isArray(condition)) {
      this.conditions.push(...condition)
    } else {
      this.conditions.push(condition)
    }
    return this
  }

  raw(expression: string): this {
    this.rawExpressions.push(expression)
    return this
  }

  or(): this {
    this.logic = 'or'
    return this
  }

  and(): this {
    this.logic = 'and'
    return this
  }

  build(): string {
    const parts: string[] = []

    if (this.conditions.length > 0) {
      parts.push(buildODataFilter(this.conditions, this.logic))
    }

    parts.push(...this.rawExpressions)

    return parts.join(` ${this.logic} `)
  }
}

// ---------------------------------------------------------------------------
// Expand Builder
// ---------------------------------------------------------------------------

export class ExpandBuilder {
  private selectFields: string[] = []
  private filterString: string = ''
  private orderByString: string = ''
  private topValue?: number
  private skipValue?: number
  private nestedExpands: Map<string, ExpandBuilder> = new Map()

  select(...fields: string[]): this {
    this.selectFields.push(...fields)
    return this
  }

  filter(callback: (f: FilterBuilder) => void): this {
    const builder = new FilterBuilder()
    callback(builder)
    this.filterString = builder.build()
    return this
  }

  orderBy(field: string, direction: 'asc' | 'desc' = 'asc'): this {
    this.orderByString = `${field} ${direction}`
    return this
  }

  top(n: number): this {
    this.topValue = n
    return this
  }

  skip(n: number): this {
    this.skipValue = n
    return this
  }

  expand(name: string, callback?: (e: ExpandBuilder) => void): this {
    const builder = new ExpandBuilder()
    if (callback) callback(builder)
    this.nestedExpands.set(name, builder)
    return this
  }

  build(): string {
    const parts: string[] = []

    if (this.selectFields.length > 0) {
      parts.push(`$select=${this.selectFields.join(',')}`)
    }
    if (this.filterString) {
      parts.push(`$filter=${this.filterString}`)
    }
    if (this.orderByString) {
      parts.push(`$orderby=${this.orderByString}`)
    }
    if (this.topValue !== undefined) {
      parts.push(`$top=${this.topValue}`)
    }
    if (this.skipValue !== undefined) {
      parts.push(`$skip=${this.skipValue}`)
    }
    if (this.nestedExpands.size > 0) {
      const expandParts: string[] = []
      for (const [name, builder] of this.nestedExpands) {
        const nested = builder.build()
        expandParts.push(nested ? `${name}(${nested})` : name)
      }
      parts.push(`$expand=${expandParts.join(',')}`)
    }

    return parts.join(';')
  }
}

// ---------------------------------------------------------------------------
// Main Query Builder
// ---------------------------------------------------------------------------

export class ODataQuery {
  private _entitySet: string
  private _select: string[] = []
  private _filter: string = ''
  private _orderBy: string[] = []
  private _top?: number
  private _skip?: number
  private _count: boolean = false
  private _search: string = ''
  private _apply: string = ''
  private _expands: Map<string, string> = new Map()
  private _temporal: { asOf?: string; validAt?: string; includeHistory?: boolean } = {}

  private constructor(entitySet: string) {
    this._entitySet = entitySet
  }

  /**
   * Start building a query for an entity set.
   */
  static from(entitySet: string): ODataQuery {
    return new ODataQuery(entitySet)
  }

  /**
   * Select specific fields.
   */
  select(...fields: string[]): this {
    this._select.push(...fields)
    return this
  }

  /**
   * Add filter conditions.
   * Accepts a callback with a FilterBuilder for fluent filter construction.
   */
  filter(callback: (f: FilterBuilder) => void): this {
    const builder = new FilterBuilder()
    callback(builder)
    this._filter = builder.build()
    return this
  }

  /**
   * Add a raw filter expression.
   */
  filterRaw(expression: string): this {
    this._filter = this._filter
      ? `${this._filter} and ${expression}`
      : expression
    return this
  }

  /**
   * Add an expand for a navigation property.
   */
  expand(name: string, callback?: (e: ExpandBuilder) => void): this {
    if (callback) {
      const builder = new ExpandBuilder()
      callback(builder)
      const options = builder.build()
      this._expands.set(name, options ? `${name}(${options})` : name)
    } else {
      this._expands.set(name, name)
    }
    return this
  }

  /**
   * Add ordering.
   */
  orderBy(field: string, direction: 'asc' | 'desc' = 'asc'): this {
    this._orderBy.push(`${field} ${direction}`)
    return this
  }

  /**
   * Set page size (top).
   */
  top(n: number): this {
    this._top = n
    return this
  }

  /**
   * Set offset (skip).
   */
  skip(n: number): this {
    this._skip = n
    return this
  }

  /**
   * Enable inline count.
   */
  count(enabled: boolean = true): this {
    this._count = enabled
    return this
  }

  /**
   * Set full-text search query.
   */
  search(query: string): this {
    this._search = query
    return this
  }

  /**
   * Set aggregation ($apply).
   */
  apply(expression: string): this {
    this._apply = expression
    return this
  }

  /**
   * Set temporal query parameters.
   */
  temporal(options: { asOf?: string; validAt?: string; includeHistory?: boolean }): this {
    this._temporal = options
    return this
  }

  /**
   * Set page (combines top + skip).
   */
  page(pageNumber: number, pageSize: number): this {
    this._top = pageSize
    this._skip = (pageNumber - 1) * pageSize
    return this
  }

  /**
   * Build the ODataQueryOptions object.
   */
  build(): ODataQueryOptions {
    const opts: ODataQueryOptions = {}

    if (this._select.length > 0) opts.$select = this._select.join(',')
    if (this._filter) opts.$filter = this._filter
    if (this._orderBy.length > 0) opts.$orderby = this._orderBy.join(',')
    if (this._top !== undefined) opts.$top = this._top
    if (this._skip !== undefined) opts.$skip = this._skip
    if (this._count) opts.$count = true
    if (this._search) opts.$search = this._search
    if (this._apply) opts.$apply = this._apply

    if (this._expands.size > 0) {
      opts.$expand = [...this._expands.values()].join(',')
    }

    // Temporal
    if (this._temporal.asOf) opts.asOf = this._temporal.asOf
    if (this._temporal.validAt) opts.validAt = this._temporal.validAt
    if (this._temporal.includeHistory) opts.includeHistory = true

    return opts
  }

  /**
   * Build the full URL (for debugging/display).
   */
  toUrl(baseUrl: string = ''): string {
    const opts = this.build()
    const params = new URLSearchParams()

    if (opts.$filter) params.append('$filter', opts.$filter)
    if (opts.$select) params.append('$select', opts.$select)
    if (opts.$expand) params.append('$expand', opts.$expand)
    if (opts.$orderby) params.append('$orderby', opts.$orderby)
    if (opts.$top !== undefined) params.append('$top', opts.$top.toString())
    if (opts.$skip !== undefined) params.append('$skip', opts.$skip.toString())
    if (opts.$count) params.append('$count', 'true')
    if (opts.$search) params.append('$search', opts.$search)
    if (opts.$apply) params.append('$apply', opts.$apply)

    const qs = params.toString()
    return `${baseUrl}/${this._entitySet}${qs ? `?${qs}` : ''}`
  }

  /**
   * Get the entity set name.
   */
  getEntitySet(): string {
    return this._entitySet
  }
}
