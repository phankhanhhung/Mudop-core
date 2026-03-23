import { describe, it, expect } from 'vitest'
import {
  ODataQuery,
  FilterBuilder,
  ExpandBuilder,
  FieldFilter,
} from '@/odata/TypedQueryBuilder'

// ==========================================================================
// ODataQuery — Main Query Builder
// ==========================================================================

describe('ODataQuery', () => {
  describe('from()', () => {
    it('should create a query builder with entity set name', () => {
      const query = ODataQuery.from('Customers')
      expect(query.getEntitySet()).toBe('Customers')
    })

    it('should build empty options when no clauses are added', () => {
      const opts = ODataQuery.from('Customers').build()
      expect(opts).toEqual({})
    })
  })

  describe('select()', () => {
    it('should build $select with comma-separated fields', () => {
      const opts = ODataQuery.from('Customers')
        .select('Id', 'name', 'email')
        .build()

      expect(opts.$select).toBe('Id,name,email')
    })

    it('should accumulate fields across multiple select calls', () => {
      const opts = ODataQuery.from('Customers')
        .select('Id', 'name')
        .select('email')
        .build()

      expect(opts.$select).toBe('Id,name,email')
    })

    it('should not set $select when no fields are specified', () => {
      const opts = ODataQuery.from('Customers').build()
      expect(opts.$select).toBeUndefined()
    })
  })

  describe('top() / skip() / count()', () => {
    it('should build correct pagination options', () => {
      const opts = ODataQuery.from('Customers')
        .top(20)
        .skip(40)
        .count()
        .build()

      expect(opts.$top).toBe(20)
      expect(opts.$skip).toBe(40)
      expect(opts.$count).toBe(true)
    })

    it('should handle top(0) and skip(0)', () => {
      const opts = ODataQuery.from('Customers')
        .top(0)
        .skip(0)
        .build()

      expect(opts.$top).toBe(0)
      expect(opts.$skip).toBe(0)
    })

    it('should allow disabling count', () => {
      const opts = ODataQuery.from('Customers')
        .count(true)
        .count(false)
        .build()

      expect(opts.$count).toBeUndefined()
    })
  })

  describe('page()', () => {
    it('should compute top and skip from page number and page size', () => {
      const opts = ODataQuery.from('Customers')
        .page(3, 20)
        .build()

      expect(opts.$top).toBe(20)
      expect(opts.$skip).toBe(40)
    })

    it('should handle page 1 with skip=0', () => {
      const opts = ODataQuery.from('Customers')
        .page(1, 10)
        .build()

      expect(opts.$top).toBe(10)
      expect(opts.$skip).toBe(0)
    })

    it('should handle large page numbers', () => {
      const opts = ODataQuery.from('Products')
        .page(100, 50)
        .build()

      expect(opts.$top).toBe(50)
      expect(opts.$skip).toBe(4950)
    })
  })

  describe('orderBy()', () => {
    it('should build a single ascending orderby', () => {
      const opts = ODataQuery.from('Customers')
        .orderBy('name', 'asc')
        .build()

      expect(opts.$orderby).toBe('name asc')
    })

    it('should default direction to asc', () => {
      const opts = ODataQuery.from('Customers')
        .orderBy('name')
        .build()

      expect(opts.$orderby).toBe('name asc')
    })

    it('should combine multiple orderby clauses with comma', () => {
      const opts = ODataQuery.from('Customers')
        .orderBy('name', 'asc')
        .orderBy('createdAt', 'desc')
        .build()

      expect(opts.$orderby).toBe('name asc,createdAt desc')
    })
  })

  describe('search()', () => {
    it('should set $search option', () => {
      const opts = ODataQuery.from('Customers')
        .search('hello')
        .build()

      expect(opts.$search).toBe('hello')
    })

    it('should not set $search when empty', () => {
      const opts = ODataQuery.from('Customers').build()
      expect(opts.$search).toBeUndefined()
    })
  })

  describe('apply()', () => {
    it('should set $apply aggregation expression', () => {
      const opts = ODataQuery.from('Orders')
        .apply('groupby((Status),aggregate(Balance with sum as Total))')
        .build()

      expect(opts.$apply).toBe(
        'groupby((Status),aggregate(Balance with sum as Total))'
      )
    })
  })

  describe('temporal()', () => {
    it('should set asOf temporal parameter', () => {
      const opts = ODataQuery.from('PriceHistory')
        .temporal({ asOf: '2025-01-01' })
        .build()

      expect(opts.asOf).toBe('2025-01-01')
      expect(opts.validAt).toBeUndefined()
      expect(opts.includeHistory).toBeUndefined()
    })

    it('should set validAt temporal parameter', () => {
      const opts = ODataQuery.from('PriceHistory')
        .temporal({ validAt: '2025-06-15' })
        .build()

      expect(opts.validAt).toBe('2025-06-15')
    })

    it('should set includeHistory flag', () => {
      const opts = ODataQuery.from('PriceHistory')
        .temporal({ includeHistory: true })
        .build()

      expect(opts.includeHistory).toBe(true)
    })

    it('should support all temporal options together', () => {
      const opts = ODataQuery.from('PriceHistory')
        .temporal({
          asOf: '2025-01-01',
          validAt: '2025-06-15',
          includeHistory: true,
        })
        .build()

      expect(opts.asOf).toBe('2025-01-01')
      expect(opts.validAt).toBe('2025-06-15')
      expect(opts.includeHistory).toBe(true)
    })
  })

  describe('expand()', () => {
    it('should build simple expand without options', () => {
      const opts = ODataQuery.from('Customers')
        .expand('Orders')
        .build()

      expect(opts.$expand).toBe('Orders')
    })

    it('should build expand with nested options', () => {
      const opts = ODataQuery.from('Customers')
        .expand('Orders', e =>
          e.select('Id', 'total')
            .top(5)
            .filter(f => {
              f.add(f.field('total').gt(100))
            })
        )
        .build()

      expect(opts.$expand).toContain('Orders(')
      expect(opts.$expand).toContain('$select=Id,total')
      expect(opts.$expand).toContain('$top=5')
      expect(opts.$expand).toContain('$filter=total gt 100')
    })

    it('should combine multiple expands with comma', () => {
      const opts = ODataQuery.from('Orders')
        .expand('Customer')
        .expand('Items')
        .build()

      expect(opts.$expand).toBe('Customer,Items')
    })

    it('should not set $expand when no expands added', () => {
      const opts = ODataQuery.from('Customers').build()
      expect(opts.$expand).toBeUndefined()
    })
  })

  describe('filter()', () => {
    it('should build filter from callback', () => {
      const opts = ODataQuery.from('Customers')
        .filter(f => {
          f.add(f.field('name').eq('John'))
        })
        .build()

      expect(opts.$filter).toBe("name eq 'John'")
    })

    it('should build filter with multiple conditions', () => {
      const opts = ODataQuery.from('Customers')
        .filter(f => {
          f.add(f.field('name').eq('John'))
          f.add(f.field('age').gt(18))
        })
        .build()

      expect(opts.$filter).toBe("name eq 'John' and age gt 18")
    })
  })

  describe('filterRaw()', () => {
    it('should set raw filter expression', () => {
      const opts = ODataQuery.from('Customers')
        .filterRaw('year(createdAt) eq 2025')
        .build()

      expect(opts.$filter).toBe('year(createdAt) eq 2025')
    })

    it('should combine with existing filter using and', () => {
      const opts = ODataQuery.from('Customers')
        .filter(f => f.add(f.field('status').eq('Active')))
        .filterRaw('year(createdAt) eq 2025')
        .build()

      expect(opts.$filter).toBe("status eq 'Active' and year(createdAt) eq 2025")
    })
  })

  describe('toUrl()', () => {
    it('should build URL with entity set and no query params', () => {
      const url = ODataQuery.from('Customers').toUrl('/odata/myapp')
      expect(url).toBe('/odata/myapp/Customers')
    })

    it('should build URL with query parameters', () => {
      const url = ODataQuery.from('Customers')
        .select('Id', 'name')
        .top(10)
        .toUrl('/odata/myapp')

      expect(url).toContain('/odata/myapp/Customers?')
      expect(url).toContain('%24select=Id%2Cname')
      expect(url).toContain('%24top=10')
    })

    it('should build URL with all options', () => {
      const url = ODataQuery.from('Customers')
        .select('Id', 'name')
        .filter(f => f.add(f.field('name').eq('John')))
        .orderBy('name')
        .top(10)
        .skip(0)
        .count()
        .search('test')
        .toUrl('/odata/myapp')

      expect(url).toContain('/odata/myapp/Customers?')
      // URL-encoded $filter, $select, $orderby, $top, $skip, $count, $search
      const params = new URLSearchParams(url.split('?')[1])
      expect(params.get('$filter')).toBe("name eq 'John'")
      expect(params.get('$select')).toBe('Id,name')
      expect(params.get('$orderby')).toBe('name asc')
      expect(params.get('$top')).toBe('10')
      expect(params.get('$skip')).toBe('0')
      expect(params.get('$count')).toBe('true')
      expect(params.get('$search')).toBe('test')
    })

    it('should default to empty base URL', () => {
      const url = ODataQuery.from('Customers').toUrl()
      expect(url).toBe('/Customers')
    })
  })

  describe('chaining combinations', () => {
    it('should support filter + select + top + orderBy + expand', () => {
      const opts = ODataQuery.from('Customers')
        .filter(f => {
          f.add(f.field('status').eq('Active'))
        })
        .select('Id', 'name', 'status')
        .top(50)
        .orderBy('name', 'asc')
        .expand('Orders')
        .build()

      expect(opts.$filter).toBe("status eq 'Active'")
      expect(opts.$select).toBe('Id,name,status')
      expect(opts.$top).toBe(50)
      expect(opts.$orderby).toBe('name asc')
      expect(opts.$expand).toBe('Orders')
    })

    it('should support page + count + search + apply', () => {
      const opts = ODataQuery.from('Products')
        .page(2, 25)
        .count()
        .search('widget')
        .build()

      expect(opts.$top).toBe(25)
      expect(opts.$skip).toBe(25)
      expect(opts.$count).toBe(true)
      expect(opts.$search).toBe('widget')
    })

    it('should support temporal + select + filter', () => {
      const opts = ODataQuery.from('PriceHistory')
        .temporal({ asOf: '2025-01-01', validAt: '2025-06-15' })
        .select('price', 'effectiveFrom')
        .filter(f => f.add(f.field('price').gt(100)))
        .build()

      expect(opts.asOf).toBe('2025-01-01')
      expect(opts.validAt).toBe('2025-06-15')
      expect(opts.$select).toBe('price,effectiveFrom')
      expect(opts.$filter).toBe('price gt 100')
    })

    it('should return this for chaining from all methods', () => {
      const query = ODataQuery.from('Customers')
      const result = query
        .select('Id')
        .filter(f => f.add(f.field('x').eq(1)))
        .orderBy('Id')
        .top(10)
        .skip(0)
        .count()
        .search('q')
        .apply('agg')
        .temporal({ asOf: '2025-01-01' })
        .expand('Orders')
        .filterRaw('extra eq 1')
        .page(1, 10)

      // If chaining is broken, this would throw
      expect(result.build()).toBeDefined()
    })
  })
})

// ==========================================================================
// FieldFilter
// ==========================================================================

describe('FieldFilter', () => {
  describe('comparison operators', () => {
    it('should create eq condition', () => {
      const ff = new FieldFilter('name')
      const cond = ff.eq('John')
      expect(cond).toEqual({ field: 'name', operator: 'eq', value: 'John' })
    })

    it('should create ne condition', () => {
      const ff = new FieldFilter('status')
      const cond = ff.ne('Inactive')
      expect(cond).toEqual({ field: 'status', operator: 'ne', value: 'Inactive' })
    })

    it('should create gt condition', () => {
      const ff = new FieldFilter('age')
      const cond = ff.gt(18)
      expect(cond).toEqual({ field: 'age', operator: 'gt', value: 18 })
    })

    it('should create ge condition', () => {
      const ff = new FieldFilter('age')
      const cond = ff.ge(18)
      expect(cond).toEqual({ field: 'age', operator: 'ge', value: 18 })
    })

    it('should create lt condition', () => {
      const ff = new FieldFilter('price')
      const cond = ff.lt(50)
      expect(cond).toEqual({ field: 'price', operator: 'lt', value: 50 })
    })

    it('should create le condition', () => {
      const ff = new FieldFilter('price')
      const cond = ff.le(100)
      expect(cond).toEqual({ field: 'price', operator: 'le', value: 100 })
    })
  })

  describe('string operators', () => {
    it('should create contains condition', () => {
      const ff = new FieldFilter('name')
      const cond = ff.contains('John')
      expect(cond).toEqual({ field: 'name', operator: 'contains', value: 'John' })
    })

    it('should create startsWith condition', () => {
      const ff = new FieldFilter('name')
      const cond = ff.startsWith('Jo')
      expect(cond).toEqual({ field: 'name', operator: 'startswith', value: 'Jo' })
    })

    it('should create endsWith condition', () => {
      const ff = new FieldFilter('email')
      const cond = ff.endsWith('.com')
      expect(cond).toEqual({ field: 'email', operator: 'endswith', value: '.com' })
    })
  })

  describe('null operators', () => {
    it('should create isNull condition', () => {
      const ff = new FieldFilter('deletedAt')
      const cond = ff.isNull()
      expect(cond).toEqual({ field: 'deletedAt', operator: 'eq', value: null })
    })

    it('should create isNotNull condition', () => {
      const ff = new FieldFilter('email')
      const cond = ff.isNotNull()
      expect(cond).toEqual({ field: 'email', operator: 'ne', value: null })
    })
  })

  describe('in()', () => {
    it('should create OData in expression for strings', () => {
      const ff = new FieldFilter('status')
      const expr = ff.in(['Active', 'Pending'])
      expect(expr).toBe("status in ('Active','Pending')")
    })

    it('should create OData in expression for numbers', () => {
      const ff = new FieldFilter('priority')
      const expr = ff.in([1, 2, 3])
      expect(expr).toBe('priority in (1,2,3)')
    })

    it('should handle mixed types', () => {
      const ff = new FieldFilter('val')
      const expr = ff.in([1, 'two', true, null])
      expect(expr).toBe("val in (1,'two',true,null)")
    })
  })

  describe('between()', () => {
    it('should return array of two conditions (ge and le)', () => {
      const ff = new FieldFilter('price')
      const conditions = ff.between(10, 100)

      expect(conditions).toHaveLength(2)
      expect(conditions[0]).toEqual({ field: 'price', operator: 'ge', value: 10 })
      expect(conditions[1]).toEqual({ field: 'price', operator: 'le', value: 100 })
    })

    it('should work with string values', () => {
      const ff = new FieldFilter('date')
      const conditions = ff.between('2025-01-01', '2025-12-31')

      expect(conditions).toHaveLength(2)
      expect(conditions[0]).toEqual({
        field: 'date',
        operator: 'ge',
        value: '2025-01-01',
      })
      expect(conditions[1]).toEqual({
        field: 'date',
        operator: 'le',
        value: '2025-12-31',
      })
    })
  })
})

// ==========================================================================
// FilterBuilder
// ==========================================================================

describe('FilterBuilder', () => {
  describe('field()', () => {
    it('should return a FieldFilter for the given field name', () => {
      const fb = new FilterBuilder()
      const ff = fb.field('name')
      expect(ff).toBeInstanceOf(FieldFilter)
    })
  })

  describe('add()', () => {
    it('should add a single condition and build filter string', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('name').eq('John'))
      expect(fb.build()).toBe("name eq 'John'")
    })

    it('should combine multiple conditions with "and" by default', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('name').eq('John'))
      fb.add(fb.field('age').gt(18))
      expect(fb.build()).toBe("name eq 'John' and age gt 18")
    })

    it('should accept array of conditions (from between())', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('price').between(10, 100))
      expect(fb.build()).toBe('price ge 10 and price le 100')
    })

    it('should return this for chaining', () => {
      const fb = new FilterBuilder()
      const result = fb.add(fb.field('x').eq(1))
      expect(result).toBe(fb)
    })
  })

  describe('raw()', () => {
    it('should pass through raw expression', () => {
      const fb = new FilterBuilder()
      fb.raw('year(createdAt) eq 2025')
      expect(fb.build()).toBe('year(createdAt) eq 2025')
    })

    it('should combine raw expressions with conditions using logic operator', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('status').eq('Active'))
      fb.raw('year(createdAt) eq 2025')
      expect(fb.build()).toBe("status eq 'Active' and year(createdAt) eq 2025")
    })

    it('should combine multiple raw expressions', () => {
      const fb = new FilterBuilder()
      fb.raw('year(createdAt) eq 2025')
      fb.raw('month(createdAt) eq 6')
      expect(fb.build()).toBe(
        'year(createdAt) eq 2025 and month(createdAt) eq 6'
      )
    })
  })

  describe('or()', () => {
    it('should combine conditions with "or" logic', () => {
      const fb = new FilterBuilder()
      fb.or()
      fb.add(fb.field('status').eq('Active'))
      fb.add(fb.field('status').eq('Pending'))
      expect(fb.build()).toBe("status eq 'Active' or status eq 'Pending'")
    })

    it('should combine conditions and raw expressions with "or"', () => {
      const fb = new FilterBuilder()
      fb.or()
      fb.add(fb.field('name').contains('John'))
      fb.raw('isVIP eq true')
      expect(fb.build()).toBe("contains(name, 'John') or isVIP eq true")
    })
  })

  describe('and()', () => {
    it('should reset logic back to "and"', () => {
      const fb = new FilterBuilder()
      fb.or()
      fb.and()
      fb.add(fb.field('a').eq(1))
      fb.add(fb.field('b').eq(2))
      expect(fb.build()).toBe('a eq 1 and b eq 2')
    })
  })

  describe('build() with empty state', () => {
    it('should return empty string with no conditions or raw expressions', () => {
      const fb = new FilterBuilder()
      expect(fb.build()).toBe('')
    })
  })

  describe('complex filter scenarios', () => {
    it('should handle contains filter', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('name').contains('John'))
      expect(fb.build()).toBe("contains(name, 'John')")
    })

    it('should handle startsWith filter', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('name').startsWith('Jo'))
      expect(fb.build()).toBe("startswith(name, 'Jo')")
    })

    it('should handle endsWith filter', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('email').endsWith('.com'))
      expect(fb.build()).toBe("endswith(email, '.com')")
    })

    it('should handle null checks', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('deletedAt').isNull())
      expect(fb.build()).toBe('deletedAt eq null')
    })

    it('should handle not-null checks', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('email').isNotNull())
      expect(fb.build()).toBe('email ne null')
    })

    it('should handle between combined with other conditions', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('status').eq('Active'))
      fb.add(fb.field('price').between(10, 100))
      expect(fb.build()).toBe(
        "status eq 'Active' and price ge 10 and price le 100"
      )
    })

    it('should handle boolean values', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('isActive').eq(true))
      expect(fb.build()).toBe('isActive eq true')
    })

    it('should handle numeric values', () => {
      const fb = new FilterBuilder()
      fb.add(fb.field('quantity').ge(0))
      fb.add(fb.field('quantity').le(1000))
      expect(fb.build()).toBe('quantity ge 0 and quantity le 1000')
    })
  })
})

// ==========================================================================
// ExpandBuilder
// ==========================================================================

describe('ExpandBuilder', () => {
  describe('select()', () => {
    it('should build $select option', () => {
      const eb = new ExpandBuilder()
      eb.select('Id', 'name', 'email')
      expect(eb.build()).toBe('$select=Id,name,email')
    })
  })

  describe('filter()', () => {
    it('should build $filter option from callback', () => {
      const eb = new ExpandBuilder()
      eb.filter(f => f.add(f.field('total').gt(100)))
      expect(eb.build()).toBe('$filter=total gt 100')
    })
  })

  describe('orderBy()', () => {
    it('should build $orderby option', () => {
      const eb = new ExpandBuilder()
      eb.orderBy('createdAt', 'desc')
      expect(eb.build()).toBe('$orderby=createdAt desc')
    })

    it('should default to asc direction', () => {
      const eb = new ExpandBuilder()
      eb.orderBy('name')
      expect(eb.build()).toBe('$orderby=name asc')
    })
  })

  describe('top() / skip()', () => {
    it('should build $top option', () => {
      const eb = new ExpandBuilder()
      eb.top(5)
      expect(eb.build()).toBe('$top=5')
    })

    it('should build $skip option', () => {
      const eb = new ExpandBuilder()
      eb.skip(10)
      expect(eb.build()).toBe('$skip=10')
    })

    it('should combine top and skip', () => {
      const eb = new ExpandBuilder()
      eb.top(5).skip(10)
      const result = eb.build()
      expect(result).toContain('$top=5')
      expect(result).toContain('$skip=10')
    })
  })

  describe('nested expand()', () => {
    it('should build nested expand without options', () => {
      const eb = new ExpandBuilder()
      eb.expand('Address')
      expect(eb.build()).toBe('$expand=Address')
    })

    it('should build nested expand with options', () => {
      const eb = new ExpandBuilder()
      eb.expand('Address', a => a.select('city', 'country'))
      expect(eb.build()).toBe('$expand=Address($select=city,country)')
    })

    it('should build double-nested expand', () => {
      const eb = new ExpandBuilder()
      eb.expand('Customer', c =>
        c.select('name').expand('Address', a => a.select('city'))
      )

      const result = eb.build()
      expect(result).toBe(
        '$expand=Customer($select=name;$expand=Address($select=city))'
      )
    })
  })

  describe('combined options', () => {
    it('should join multiple options with semicolons', () => {
      const eb = new ExpandBuilder()
      eb.select('Id', 'total')
        .filter(f => f.add(f.field('total').gt(100)))
        .orderBy('total', 'desc')
        .top(5)
        .skip(0)

      const result = eb.build()
      expect(result).toContain('$select=Id,total')
      expect(result).toContain('$filter=total gt 100')
      expect(result).toContain('$orderby=total desc')
      expect(result).toContain('$top=5')
      expect(result).toContain('$skip=0')

      // Options are separated by semicolons
      const parts = result.split(';')
      expect(parts.length).toBe(5)
    })

    it('should return empty string with no options set', () => {
      const eb = new ExpandBuilder()
      expect(eb.build()).toBe('')
    })
  })

  describe('chaining', () => {
    it('should return this from all builder methods', () => {
      const eb = new ExpandBuilder()
      const result = eb
        .select('Id')
        .filter(f => f.add(f.field('x').eq(1)))
        .orderBy('Id')
        .top(10)
        .skip(0)
        .expand('Child')

      // If chaining were broken, we would not reach this point
      expect(result).toBe(eb)
    })
  })
})

// ==========================================================================
// Integration: ODataQuery with nested expand + filter
// ==========================================================================

describe('Integration scenarios', () => {
  it('should build a complex real-world query', () => {
    const opts = ODataQuery.from('Customers')
      .select('Id', 'name', 'email', 'status')
      .filter(f => {
        f.add(f.field('status').eq('Active'))
        f.add(f.field('email').isNotNull())
      })
      .expand('Orders', e =>
        e
          .select('Id', 'total', 'createdAt')
          .filter(f => f.add(f.field('total').gt(100)))
          .orderBy('createdAt', 'desc')
          .top(10)
      )
      .orderBy('name', 'asc')
      .top(50)
      .skip(0)
      .count()
      .build()

    expect(opts.$select).toBe('Id,name,email,status')
    expect(opts.$filter).toBe("status eq 'Active' and email ne null")
    expect(opts.$orderby).toBe('name asc')
    expect(opts.$top).toBe(50)
    expect(opts.$skip).toBe(0)
    expect(opts.$count).toBe(true)
    expect(opts.$expand).toContain('Orders(')
    expect(opts.$expand).toContain('$select=Id,total,createdAt')
    expect(opts.$expand).toContain('$filter=total gt 100')
    expect(opts.$expand).toContain('$orderby=createdAt desc')
    expect(opts.$expand).toContain('$top=10')
  })

  it('should build multiple expands with different options', () => {
    const opts = ODataQuery.from('Orders')
      .expand('Customer', c =>
        c.select('name', 'email').expand('Address')
      )
      .expand('Items', i =>
        i
          .filter(f => f.add(f.field('quantity').gt(0)))
          .orderBy('lineNumber')
      )
      .build()

    expect(opts.$expand).toBeDefined()
    // Should contain both Customer and Items
    expect(opts.$expand).toContain('Customer(')
    expect(opts.$expand).toContain('Items(')
    expect(opts.$expand).toContain('$select=name,email')
    expect(opts.$expand).toContain('$expand=Address')
    expect(opts.$expand).toContain('$filter=quantity gt 0')
    expect(opts.$expand).toContain('$orderby=lineNumber asc')
  })

  it('should build URL with complex query', () => {
    const url = ODataQuery.from('Products')
      .select('Id', 'name', 'price')
      .filter(f => {
        f.add(f.field('price').between(10, 100))
      })
      .orderBy('price', 'desc')
      .page(2, 20)
      .count()
      .toUrl('/odata/catalog')

    expect(url).toContain('/odata/catalog/Products?')
    const params = new URLSearchParams(url.split('?')[1])
    expect(params.get('$filter')).toBe('price ge 10 and price le 100')
    expect(params.get('$select')).toBe('Id,name,price')
    expect(params.get('$orderby')).toBe('price desc')
    expect(params.get('$top')).toBe('20')
    expect(params.get('$skip')).toBe('20')
    expect(params.get('$count')).toBe('true')
  })

  it('should handle apply with temporal params', () => {
    const opts = ODataQuery.from('SalesHistory')
      .apply('groupby((Region),aggregate(Amount with sum as TotalSales))')
      .temporal({ asOf: '2025-01-01', includeHistory: true })
      .build()

    expect(opts.$apply).toBe(
      'groupby((Region),aggregate(Amount with sum as TotalSales))'
    )
    expect(opts.asOf).toBe('2025-01-01')
    expect(opts.includeHistory).toBe(true)
  })

  it('should handle or-filter with in-expression via raw', () => {
    const fb = new FilterBuilder()
    fb.or()
    fb.add(fb.field('name').contains('John'))
    fb.raw(fb.field('status').in(['Active', 'Pending']))

    const filterStr = fb.build()
    expect(filterStr).toBe(
      "contains(name, 'John') or status in ('Active','Pending')"
    )
  })
})
