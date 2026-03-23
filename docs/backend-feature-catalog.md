# BMMDL Backend Feature Catalog

**Generated from test analysis — last updated 2026-03-11**

| Test Suite | Tests |
|---|---|
| BMMDL.Tests (Unit) | 2,878+ |
| BMMDL.Tests.New (E2E) | 656 |
| **Total** | **3,534+** |

---

## 1. DSL / Compiler (~680 tests)

### 1.1 ANTLR4 Grammar Parsing (~180 tests)

**Parser Integration** (21 tests)
- Entity definition parsing (fields, key fields)
- Aspect definition parsing
- Custom type definition parsing
- Enum definition parsing
- Service definition parsing (actions, functions)
- Event definition parsing
- Annotation parsing on entities and fields
- Namespace block parsing
- Error handling: syntax errors, missing semicolons, file not found, empty files, whitespace-only, comment-only

**Model Builder** (43 tests)
- All primitive types: String(n), Integer, Decimal(p,s), Boolean, Date, Time, DateTime, Timestamp, UUID, Binary
- Associations with cardinality
- Compositions with cardinality
- Aspect declarations and references
- Virtual (computed) fields with expressions
- Stored computed fields
- Index definitions (single, composite, unique)
- Check constraints, unique constraints, foreign key constraints
- Struct types (custom types)
- Projection entities with excluding, alias, wildcard
- View SELECT statement parsing

**Expression Builder** (31 tests)
- Literal expressions: string, integer, decimal, boolean, null, enum values
- Identifier expressions: simple and path-based
- Binary operators: arithmetic (+, -, *, /, %), comparison (=, !=, <, >, <=, >=), logical (AND, OR), string concat
- Unary operators: NOT, negation
- Function calls with arguments
- Aggregate expressions (COUNT, SUM, AVG, MIN, MAX)
- CASE/WHEN expressions (searched and simple forms)
- IS NULL / IS NOT NULL
- IN expressions
- BETWEEN expressions
- LIKE expressions
- Cast expressions
- Ternary expressions
- Nested/parenthesized expressions

**Cardinality Parsing** (8 tests)
- OneToOne `[1..1]`
- ManyToOne `[0..1]`
- OneToMany `[*]` and `[0..*]`
- Explicit vs implicit cardinality
- Composition default cardinality

**Temporal Annotation Parsing** (23 tests)
- `@Temporal` annotation parsing
- Strategy values: InlineHistory, SeparateTables
- `@Temporal.ValidTime` bitemporal annotations
- Combined temporal + other annotations

**Temporal Interval Operators** (22 tests)
- AST construction for OVERLAPS, CONTAINS, PRECEDES, MEETS
- Source location tracking
- Combining with logical expressions (AND)
- ToExpressionString formatting
- SQL generation: `&&` (overlaps), `@>` (contains), `<<` (precedes), `-|-` (meets)
- Runtime evaluation with DateTime values
- Null value handling

**View SELECT AST** (24 tests)
- Basic SELECT parsing: wildcard, explicit columns, aliases, DISTINCT
- FROM clause with aliases, qualified entity references
- WHERE clause with condition expressions
- JOIN: INNER JOIN, LEFT JOIN, multiple joins
- GROUP BY single/multiple fields
- HAVING clause
- ORDER BY: ASC, DESC, NULLS LAST
- Qualified wildcard (e.g., `o.*`)
- Complex SELECT combining join/where/orderby
- Raw string preservation alongside AST
- SQL generation from AST
- Entity name resolution via entity cache
- Dependency extraction from AST

**BmModel Operations** (34 tests)
- Model creation with entities, types, enums, services, events, views
- FindEntity by short name and qualified name
- FindType by short name and qualified name
- Cross-namespace resolution
- Model merging (entities, types, enums, services, events, views, rules, access controls)
- Edge cases: duplicate names, empty model, null namespace

### 1.2 Semantic Validation (~120 tests)

**Semantic Validation** (39 tests)
- Association target entity resolution
- Composition target entity resolution
- Aspect reference resolution
- Duplicate entity/field/enum value detection
- Type resolution: primitive, custom, enum, cross-namespace
- Rule target entity validation
- Index field validation
- Constraint field validation
- Error codes SEM060-SEM066: enum value uniqueness, index column existence, cardinality format, default value types, field type validity, constraint expression refs, operation parameter types

**Computed Field Validation** (10 tests)
- Valid computed field expressions
- Unknown field references in expressions
- Circular dependencies: 2-way and 3-way cycles
- Chain dependency resolution
- Missing expression detection
- Nested expression validation
- Function call validation in expressions

**Computed Field Type Checking** (7 tests)
- Advanced type inference for computed fields

**Computed Field Advanced Validation** (18 tests)
- Extended validation scenarios for computed expressions

**Temporal Validation** (21 tests)
- TEMP001: Missing ValidTime.From field
- TEMP002: Missing ValidTime.To field
- TEMP003: Column not found in entity
- TEMP004: Wrong type for temporal columns (must be Date/DateTime/Timestamp)
- TEMP005: Reserved column names (system_start, system_end)
- TEMP006: Invalid strategy value
- TEMP007: Key computed validation
- TEMP008: Bitemporal with SeparateTables restriction
- TEMP009: Unique index warning
- TEMP010: Same from/to column
- Valid temporal entity compilation

**File Validation** (11 tests)
- Valid source compilation
- Invalid syntax detection
- Missing semicolon detection
- File not found handling
- Empty file handling
- Whitespace-only and comment-only files

**Subquery in Rule Validation** (7 tests)
- IN with subquery expressions in rules

### 1.3 Compiler Passes & Optimization (~200 tests)

**Recursive Aspect Chains** (21 tests)
- 2/3/4 level deep aspect chain resolution
- Field ordering: DFS post-order (base fields first)
- Circular detection: direct, indirect, 3-node cycles (OPT_CIRCULAR_ASPECT)
- Diamond pattern deduplication
- Entity field overrides aspect field
- Case-insensitive field deduplication
- Association and composition inlining through chains
- Behavioral inlining: rules and access controls retargeted to entity
- Multiple aspects per entity
- Shared aspect chains: each entity gets cloned fields
- Empty aspect handling
- Missing aspect graceful handling
- Annotation preservation through chains

**Aspect Chain Pipeline** (17 tests)
- Full E2E pipeline compilation with aspect chains
- Optimization pass behavioral inlining (rules, access controls, compositions)
- Edge cases in aspect chain processing

**Aspect Chain DDL** (11 tests)
- DDL generation for entities with inlined aspect fields

**Behavioral Aspects** (30 tests)
- Rules, events, access controls defined in aspects
- Behavioral inheritance through aspect chains

**Cross-Aspect Views** (15 tests)
- Views referencing entities with aspect-inlined fields

**Cross-Aspect View DDL** (23 tests)
- DDL for views that span aspect-enriched entities

**Extension Merge Pass** (39 tests)
- Extend entity: add fields, associations, compositions, indexes, constraints
- Extend type: add fields to custom types
- Extend aspect: add fields, rules, access controls
- Extend service: add actions, functions
- Extension across namespaces

**Extend Entity Pipeline** (21 tests)
- Full pipeline E2E for entity extension

**Extend Enum / Modify Service Enum** (20 tests)
- Extending enums with new values
- Modifying service operations with new parameters

**Modification Pass** (40 tests)
- Remove fields from entities
- Rename fields
- Change field types
- Add new fields via modification
- Modify field attributes (nullable, default, etc.)

**Modify Entity DDL** (17 tests)
- DDL generation for entity modifications (ALTER TABLE)

**Reject Statements** (16 tests)
- Reject statement parsing and compilation
- Reject in rules: block operations based on conditions

**Module Dependency Resolution** (11 tests)
- Module parsing with version declarations
- Linear dependency chain resolution
- Diamond dependency resolution
- Missing dependency detection
- Circular dependency detection
- Complex ERP-like dependency graph

**Model Diff Engine** (6 tests)
- No changes detection
- Added/removed entity detection
- Added/removed field detection
- Enum value changes
- Breaking change detection

**Migration Definitions** (22 tests)
- Migration definition parsing from DSL
- Migration step types: addColumn, dropColumn, renameColumn, alterType

**Service Event Handlers** (25 tests)
- Event handler parsing in services
- Emit/emits clause compilation
- when/let/foreach/raise statements in handlers
- Circular call detection

### 1.4 Expression System Bugfixes (14 tests)
- Simple CASE expression form
- Searched CASE expression form
- STDDEV/VARIANCE aggregate resolution
- COUNT/SUM with WHERE condition filters
- IN expression subquery form
- NOT IN subquery form
- SQL visitor subquery generation

---

## 2. Code Generation / DDL (~401 tests)

### 2.1 DDL Generation (~100 tests)

**DDL Generation Gap Tests** (18 tests)
- Complete DDL generation coverage tests

**Abstract Entity DDL** (4 tests)
- Abstract entities: no table creation in DDL
- Abstract entity in CSDL: `Abstract=true` attribute
- OData exclusion of abstract entities

**HasStream DDL** (8 tests)
- HasStream media entity DDL generation
- 8 metadata columns expansion for FileReference type

**Temporal DDL** (11 tests)
- System columns (system_start, system_end, version) generation
- InlineHistory strategy: compound PK, exclude constraint, partial index
- SeparateTables strategy: history table, versioning trigger, simple PK
- Bitemporal: valid time columns in PK
- btree_gist extension generation
- Deferrable FK constraints for temporal entities

**Computed Field DDL** (4 tests)
- Generated column DDL for stored computed fields
- Virtual computed fields (no DDL, computed at runtime)

**Computed Strategy Tests** (7 tests)
- Stored strategy: PostgreSQL generated columns
- Virtual strategy: no DDL generation
- Application strategy: regular column with triggers

**Computed Strategy Edge Cases** (7 tests)

**Sequence Generator** (9 tests)
- Sequence table DDL: core.__sequences with tenant_id, company_id, current_value, pattern
- Sequence lookup index generation
- Unique constraint on sequence name + tenant + company + date
- Sequence function: get_next_sequence_value with reset logic (Daily, Monthly, Yearly)

### 2.2 Constraints & Triggers (~12 tests)

**Constraint Generation** (7 tests)
- CHECK constraints with AST expression translation
- UNIQUE constraints: single and multi-column
- FOREIGN KEY constraints
- FK column generation: required (NOT NULL) vs optional
- Naming conventions for constraints

**Trigger Generation** (5 tests)
- Computed field triggers: function + trigger creation
- Non-computed fields return empty
- UPDATE OF clause with dependent fields
- Multiple trigger generation

### 2.3 Expression Translation (~100 tests)

**Function Mapping** (47 tests)
- String: UPPER, LOWER, TRIM, LENGTH, SUBSTRING (FROM/FOR), CONCAT (||), REPLACE
- Numeric: ABS, ROUND, CEIL, FLOOR, POWER, SQRT, MOD
- Date/Time: NOW, CURRENT_DATE, YEAR, MONTH, DAY, ADDDAYS, ADDMONTHS
- Aggregate: COUNT, SUM, AVG, MIN, MAX
- Conditional: COALESCE, NULLIF, IIF
- STDDEV, VARIANCE
- INSTR, DECODE, IFNULL
- TO_INTEGER, TO_DECIMAL, TO_DATE, TO_STRING, TO_TIME, TO_TIMESTAMP
- LPAD/RPAD

**Expression Translator** (17 tests)
- Identifier to snake_case conversion
- Nested expression translation
- Literal preservation
- SQL injection prevention
- NULL literal translation
- Context variables ($now -> NOW(), $user -> current_setting)
- CASE expression without ELSE
- Type casting (::type syntax)
- Boolean literal translation
- Concat operator (||)

**Binary/Unary Expression Advanced** (18 tests)
- Operator precedence: multiplication before addition, AND before OR
- Parenthesization rules
- Left associativity for subtraction/division
- Unary with binary combinations
- Stress tests: 10-level nesting, 50 operations, 20 CASE clauses

**AST Translation Integration** (17 tests)
- Complete e-commerce pipeline translation
- Multi-tenant access control translation
- Financial calculation with functions and casts
- Performance benchmarks

**Type Casting Advanced** (24 tests)
- PostgreSQL :: cast syntax generation
- Cross-type casting scenarios

**Association Navigation** (4 tests)
- Single-hop association subquery generation
- Multi-hop association subquery generation
- Unknown association fallback to snake_case

### 2.4 Schema Diff & Migrations (~80 tests)

**Schema Differ** (30 tests)
- Detect new/dropped tables
- Detect new/dropped columns
- Detect data type changes, nullability changes, default value changes
- Detect computed expression changes
- Detect new/dropped indexes (regular, unique)
- Detect new/dropped constraints (PK, FK, CHECK)
- Case-insensitive table name comparison
- Complex multi-table, multi-column changes
- Empty/identical schema comparison

**Migration Script Generator** (23 tests)
- CREATE TABLE, DROP TABLE
- ADD COLUMN, DROP COLUMN
- ALTER COLUMN type, nullability
- CREATE INDEX, DROP INDEX, CREATE UNIQUE INDEX
- ADD PRIMARY KEY, FOREIGN KEY constraints, DROP CONSTRAINT
- Computed column migrations
- UP and DOWN scripts
- Checksum computation
- Complex schema migrations
- Migration ordering
- Empty diff produces empty scripts
- Default value handling

**Migration Executor** (10 tests)
- Migration table creation
- Migration execution and recording
- Duplicate prevention
- Rollback on error
- Dry run mode
- Rollback execution
- Applied migrations listing (descending order)

**Migration Restoration** (5 tests)
- Safe migration: backup on DROP TABLE, DROP COLUMN, type change
- No backup for safe changes
- Restore script generation

**Computed Field Migrations** (15 tests)
- Formula change: V2 column + swap
- Strategy conversion: stored-to-virtual, virtual-to-application
- Chain conversions: application -> virtual -> stored
- Data preservation during formula changes
- Null-safe migrations
- Idempotent migration support
- Transaction boundary
- Lock strategy minimization

**Schema Model** (14 tests)
- TableInfo, ColumnInfo, IndexInfo, ConstraintInfo creation
- Fully qualified name generation
- Schema snapshot aggregation

**PostgreSQL Schema Reader** (11 tests)
- Read tables, columns, nullable columns, default values
- Generated columns detection
- Primary key, regular, and unique index reading
- PK, FK, and CHECK constraint reading

### 2.5 Naming Conventions (9 tests)
- Check/unique/FK constraint naming format
- Column name: camelCase to snake_case conversion
- Computed field index integration

---

## 3. Runtime / OData (~820 tests)

### 3.1 OData Query Parsing (~120 tests)

**Filter Expression Parser** (60 tests)
- Comparison operators: eq, ne, gt, ge, lt, le
- Null checks: eq null -> IS NULL, ne null -> IS NOT NULL
- Boolean: eq true, eq false
- String functions: contains -> ILIKE, startswith, endswith
- Case functions: tolower, toupper, trim
- Logical: and, or, not
- Complex nested expressions
- PascalCase to snake_case field conversion
- In operator: string and numeric values
- String functions: length, indexof, substring, concat
- Date functions: year, month, day, now
- Has operator: string enum -> equality, numeric flags -> bitwise AND
- Lambda: any -> EXISTS subquery, all -> NOT EXISTS subquery
- Pattern matching: matchesPattern -> PostgreSQL regex
- Arithmetic: add, sub, mul, div, mod
- Math functions: round, floor, ceiling
- Date/time: date cast, time cast
- Empty/whitespace filter handling

**Expand Expression Parser** (20 tests)
- Single and multiple navigation parsing
- Nested options: $select, $filter, $top, $skip, $orderby
- Mixed simple and nested expansions
- Case-insensitive navigation and option names
- $levels: number, max, case-insensitive
- $levels with other options combined
- Invalid levels values (zero, negative)

**Apply Expression Parser** (13 tests)
- GroupBy: single field, multiple fields
- Aggregate: sum, avg, min, max, count, $count
- GroupBy with aggregates, multiple aggregates
- Filter transformation (adds WHERE clause)
- Empty/whitespace apply handling

**Search Expression Parser** (14 tests)
- Contains on array fields -> ANY operator
- Contains on string fields -> ILIKE
- Any/all lambda on array fields -> ANY/unnest/NOT EXISTS
- Navigation property lambda -> EXISTS
- Localized field handling

### 3.2 Dynamic SQL Builder (34 tests)
- SELECT: simple entity, with ID, tenant-scoped, with filter, orderby, pagination
- Soft delete: exclude deleted by default, include deleted when requested
- SELECT with $select: column limiting
- INSERT: basic, tenant-scoped, auto-generate ID, respect provided ID
- UPDATE: basic, tenant-scoped, skip ID field, add updated_at, empty data throws
- DELETE: hard delete, soft delete (UPDATE is_deleted), tenant-scoped
- COUNT: basic, with filter
- EXISTS query generation
- Table name: with/without namespace
- Temporal: SeparateTables with AsOf (UNION subquery), InlineHistory with AsOf, current-only
- Temporal count with AsOf
- Expand with temporal AsOf (UNION in subquery)

### 3.3 Query Infrastructure (~25 tests)

**Query Plan Cache** (15 tests)
- Cache miss returns null
- Set then get returns cached plan
- Parameter cloning (avoids mutation)
- LRU eviction when max size exceeded
- Invalidation of matching keys
- Clear all entries
- Cache key creation: with/without options, same/different options, tenant ID exclusion
- Cache statistics tracking

### 3.4 OData Compliance (~65 tests)

**OData Compliance Fixes** (26 tests)
- URL rewrite: key-in-parentheses to segment format
- Service document: @odata.context
- Entity response: @odata.id
- Count endpoint: plain text
- CSDL metadata: facets, navigation property bindings
- Prefer header: return=representation, return=minimal
- Batch: camelCase properties, atomicity groups
- OrderBy: invalid field returns 400
- Metadata endpoint: valid XML

**CSDL Inheritance** (9 tests)
- Entity inheritance in CSDL metadata

**Composable Functions & Delta Tokens** (14 tests)
- Composable function metadata
- Delta token tracking and delta link generation

**ETag Generator** (12 tests)
- Weak ETag generation
- ETag comparison for concurrency control

**Expand & CSDL** (13 tests)
- $expand with LEFT JOIN (ManyToOne) and batch sub-queries (OneToMany)
- CSDL NavigationProperty, ReferentialConstraint, ContainsTarget

**Expand Nested Options** (12 tests)
- Nested $select, $filter, $top, $skip, $orderby within $expand

**Field Filtering & Enum Validation** (15 tests)

**Query Improvements** (17 tests)

### 3.5 OData E2E Tests (~350 tests)

**CRUD Operations** (13 tests)
- Create 201, Read 200, Update PATCH, Delete 204
- Read non-existent 404, Delete non-existent 404
- List with OData response format
- Missing required field, duplicate unique, invalid GUID, string too long
- Breaking unique constraint on update

**OData Advanced Query** (13 tests)
- $expand: single, nested, with $select
- $select specific fields
- $orderby: ascending, descending, multiple fields
- $filter: boolean, string contains, numeric comparison, AND
- Combined options
- $top/$skip pagination

**OData Filter Advanced** (10 tests)
- IN operator, NOT operator
- Null equality/inequality
- Greater than, less than or equal
- StartsWith, EndsWith
- Complex nested AND/OR
- Enum field filtering

**OData Apply/Aggregation** (19 tests)
- $apply: count, sum, average, min, max
- Multiple aggregates in single query
- GroupBy: single property, with aggregate, with sum
- GroupBy multiple properties
- Filter then aggregate / GroupBy
- Inline $count
- Virtual property count
- Empty dataset, null values handling
- Invalid syntax error handling

**Deep Insert** (6 tests)
- Nested collection creation with auto FK population
- Normal create without nested objects
- Missing required field validation
- Verify children via $expand
- Multiple children creation

**Deep Update** (5 tests)
- Update existing nested objects (with ID)
- Create new nested objects (without ID)
- Mixed update and create
- Normal PATCH without nested objects
- ETag concurrency respect

**Batch Operations** (9 tests)
- Multiple GETs, POST+PATCH+DELETE mix
- PATCH and PUT requests
- Atomicity group: failure rolls back group
- DependsOn: sequential execution
- Empty request handling

**Bound Operations** (8 tests)
- Bound action execution and entity modification
- Bound function execution and return value
- Non-existent action/entity 404

**Unbound Operations** (8 tests)
- Non-existent service/action/function 404
- Metadata includes action/function imports
- Service document includes imports

**$ref Endpoints** (5 tests)
- CreateRef: link entities
- DeleteRef: unlink entities
- UpdateRef: replace link
- Invalid nav property 404
- Missing @odata.id 400

**Containment Navigation** (4 tests)
- GET contained entities (children)
- POST contained entity (create child)
- Filter on contained entities
- Parent not found 404

**Recursive $levels Expand** (6 tests)
- $levels=N parsing
- $levels=max parsing
- Multiple options with levels
- $levels=0 (no expand)
- Invalid/negative levels rejected

**ETag Concurrency** (7 tests)
- ETag in GET response header
- PATCH with correct ETag succeeds
- PATCH with stale ETag returns 412
- PATCH without If-Match succeeds
- Wildcard ETag succeeds
- ETag changes after update
- @odata.etag in response body

**Singleton Entities** (3 tests)
- GET singleton returns single record
- PATCH singleton updates record
- List endpoint returns single result

**Media Streams** (14 tests)
- HasStream in CSDL metadata
- Upload and download media
- Media annotations in entity response
- Delete media content
- Not exposed in normal GET
- MaxSize returns 413
- ETag: If-None-Match returns 304
- Download returns ETag header

**Delta Tokens** (5 tests)
- TrackChanges header support
- DeltaToken parameter acceptance
- Standard query excludes delta link
- Combined with filter
- Invalid delta token handling

**OData Search** (5 tests)
- Single term matches string fields
- Case-insensitive search
- No match returns empty collection
- Combined with $filter
- Combined with $top/$skip

**OData $compute** (4 tests)
- Simple expression adds property
- Multiple expressions
- Combined with $select
- Invalid expression handling

**Temporal Queries** (6 tests)
- GetVersions for temporal entity
- Non-temporal entity returns 400
- includeHistory returns all versions
- asOf returns point-in-time snapshot
- system_start/system_end auto-populated
- Computed fields work with temporal

**Async Operations** (4 tests)
- Operation status for non-existent returns 404
- Delete non-existent returns 404
- Async endpoint accessible
- Invalid GUID returns 404

**Error Response Consistency** (4 tests)
- 404, 400, 401 in OData error format
- 405 Method Not Allowed

---

## 4. Business Rules (~200 tests)

### 4.1 Rule Engine Core (19 tests)
- Before create: validate pass/fail
- Before update: validate pass/fail
- Before delete rules
- After create/update rules
- Compute statements: field value computation
- Multiple rules: ordered execution
- Rule priority ordering
- Warning severity (non-blocking)
- Error severity (blocking)
- Set statement: field modification

### 4.2 Rule Engine Gaps & Extensions (~35 tests)
- Rule engine gap coverage
- Action execution within rules
- Call statements to service actions
- Emit events from rules (Rule -> UoW enqueue -> EventPublisher dispatch)
- Foreach statement result collection and merging

### 4.3 Read Rules & OnChange (23 tests)
- Before read: validation rejects/allows read
- Before read: multiple rules execute all
- Before read: ignores non-read rules
- After read: computes fields per row
- After read: validation reports errors
- After read: empty results returns OK
- OnChange: triggers when field actually changed
- OnChange: does not trigger when unchanged
- OnChange: multiple change fields, partial match
- OnChange: null-to-value, value-to-null, null-to-null
- OnChange: combined with regular update rules
- OnChange: numeric type coercion (same value)
- OnChange: case-insensitive field matching
- OnChange: empty changeFields does not trigger

### 4.4 Default Value Evaluation (26 tests)
- String, integer, decimal, boolean, enum default values
- Context variable defaults ($now, $user, $tenantId)
- Expression-based defaults
- Null handling
- Immutable and read-only field enforcement

### 4.5 Deep Handler Rule Firing (11 tests)
- Rules fire during deep insert
- Rules fire during deep update
- Child entity rules execute during nested operations

### 4.6 Auth & Rule Context (12 tests)
- User context available in rules
- Tenant context in rule evaluation
- Role-based rule execution

### 4.7 Service Actions (~70 tests)

**Service Action Runtime** (36 tests)
- Action execution with parameters
- Action return values
- Action validation
- Bound vs unbound actions

**Interpreted Action Executor** (30 tests)
- Let statement execution
- Compute statement execution
- Validate statement execution
- When/else conditional execution
- Foreach iteration
- Emit event execution
- Raise error/warning
- Return statement
- Call to other service actions

### 4.8 E2E Business Rules (~45 tests)
- Before create field modification
- Before create validation failure
- Before update partial validation
- Before update immutable field blocking
- Before delete active/inactive record
- Computed field division by zero, negative result, large numbers
- Validate profit margin
- Validate stock levels
- Compute product defaults
- Rule engine edge cases (19 tests)
- Rule engine advanced E2E (4 tests)

---

## 5. Expression System (~170 tests)

### 5.1 Runtime Expression Evaluator (55 tests)
- Literals: string, integer, decimal, boolean, null
- Identifiers: simple, case-insensitive, nested path, unknown returns null
- Arithmetic: +, -, *, /, % (division by zero returns null)
- Comparison: =, !=, <
- Logical: AND, OR
- String concat
- Unary: NOT, negate
- Functions: UPPER, LOWER, LENGTH, CONCAT, SUBSTRING, ROUND
- COALESCE: first non-null
- IIF: conditional
- CASE/WHEN: searched and simple forms
- Ternary expressions
- IN / NOT IN
- BETWEEN (inclusive boundaries)
- LIKE / NOT LIKE (with wildcards)
- IS NULL / IS NOT NULL
- Context variables: $now, $user, $tenantId
- Parameter expressions
- Complex nested expressions

### 5.2 Function Registry (94 tests)
- **String (12)**: UPPER, LOWER, TRIM, LENGTH, SUBSTRING, CONCAT, REPLACE, LEFT, RIGHT, CONTAINS, STARTSWITH, ENDSWITH
- **Numeric (8)**: ROUND, FLOOR, CEILING, ABS, POWER, SQRT, MIN, MAX, MOD
- **Date/Time (8)**: NOW, TODAY, YEAR, MONTH, DAY, ADDDAYS, ADDMONTHS, DATEDIFF
- **Conditional (5)**: COALESCE, NULLIF, IIF, ISNULL
- **GUID (3)**: NEWGUID, TOGUID (valid/invalid)
- **Custom functions**: Register, HasFunction, Invoke unknown throws
- **Extended (20+)**: INSTR, DECODE, IFNULL, TO_INTEGER, TO_DECIMAL, TO_DATE, TO_STRING, TO_TIME, TO_TIMESTAMP, STDDEV, VARIANCE, CURRENCY_CONVERSION, UNIT_CONVERSION, TRUNC, SIGN, CEIL alias, WEEK_OF_YEAR, CURRENT_DATE, CURRENT_TIME, CURRENT_TIMESTAMP, ADD_DAYS/MONTHS/YEARS, PAD_LEFT/RIGHT, NEXT_SEQUENCE, CURRENT_SEQUENCE, FORMAT_SEQUENCE

### 5.3 Aggregate Expression Resolver (19 tests)
- COUNT composition: correct SQL, null returns zero, COUNT DISTINCT
- SUM field: correct SQL, null returns zero, without field throws
- AVG, MIN, MAX field resolution
- Error cases: no entity name, unknown entity, unknown navigation, no parent ID
- Integration: evaluator with aggregate resolver
- COUNT/SUM with WHERE condition filters

### 5.4 MetaModel Expression Types (55 tests)
- All expression AST node types: Literal, Identifier, ContextVariable, Parameter, Binary, Unary, FunctionCall, Aggregate, Case, IsNull, In, Between, Like, Cast, Ternary, Paren
- Source location tracking
- Inferred type setting
- Complex deeply nested expressions

---

## 6. Authorization / Access Control (~95 tests)

### 6.1 Authentication Services (21 tests)
- JWT generation from UserContext (userId, username, email, tenantId, roles, permissions)
- JWT validation: valid token returns ClaimsPrincipal with IsAuthenticated
- Invalid/empty/malformed/expired token handling
- Refresh token generation
- Token claims extraction
- Password hasher: generation, verification, wrong password, unique hashes (salt)

### 6.2 OAuth Validator (18 tests)
- External provider token validation (Google, etc.)

### 6.3 E2E Authorization (~30 tests)
- Entity with no access rules behavior
- Unauthenticated client denied
- Tenant isolation: own data only
- Role-based read/write access
- Admin endpoint elevated role requirement
- Permission fail-close: no rules = DENY policy
- Login with valid/invalid credentials
- Protected endpoint with/without token
- External login (Google) with new/invalid user
- Permission: read-only, write, delete
- Security: SQL injection, XSS, path traversal, large payload

### 6.4 Token Refresh (5 tests)
- Refresh with valid token returns new tokens
- Token rotation: old token invalidated
- Expired/malformed refresh token handling

---

## 7. Events / Audit (~169 tests)

### 7.1 Event System Core

**Emit Pipeline** (37 tests)
- Compiler: event definition parsing (fields, types)
- Compiler: multiple event definitions
- Compiler: action emits clause
- Compiler: emit statement in rules
- Runtime: RuleEngine emit -> UoW enqueue
- Runtime: EventPublisher dispatch
- Runtime: EventSchemaValidator
- Integration: full compile -> model -> emit flow

**Event Publisher** (11 tests)
- Event publishing to registered handlers
- Multiple handlers
- Handler errors do not propagate
- Schema validation integration

**Event Schema Validator** (17 tests)
- Payload validation against BmEvent.Fields
- Missing required fields
- Extra fields handling
- Type mismatch detection
- Advisory mode (non-blocking)

**Outbox Store** (17 tests)
- Outbox pattern: store events for reliable delivery
- Pending event retrieval
- Event acknowledgment

**Outbox Processor/Broker** (12 tests)
- Background outbox processing
- Broker-based event delivery

**Integration Events** (16 tests)
- Cross-boundary event communication

**Service Event Handler** (11 tests)
- Service-level event handling
- Handler registration and dispatch

### 7.2 Event Tracing & Metrics

**Event Tracing** (24 tests)
- Event trace recording
- Trace correlation IDs
- Trace filtering by event type, time range
- Trace retention policies

**Event Metrics** (15 tests)
- Event count tracking
- Event latency measurement
- Metrics aggregation
- Error rate tracking

### 7.3 MetaModel Cache Events (11 tests)
- Cache event index: _eventsByName
- AddEvent, HasEvent, GetEvent, Events, EventNames

### 7.4 E2E Audit (~14 tests)
- Audit field auto-population: CreatedAt, UpdatedAt, CreatedBy
- Bulk create efficiency
- Bulk query pagination
- Get audit log entries
- Filter by entity, date range, action

---

## 8. Registry / Admin (~143 tests)

### 8.1 Change Detection & Versioning

**Change Detector** (16 tests)
- First installation: all entities are new
- New/removed entity detection (Minor/Major)
- Field changes: add optional (Minor), add required (Major), remove (Major)
- Type changes: widen string (Minor), narrow string (Major)
- Nullable changes: make required (Major), make nullable (Minor)
- Enum changes: add value (Minor), remove value (Major)
- Overall category determination
- Breaking changes filter

**Version Parser** (15 tests)
- Semantic versioning: parse, compare, bump (major/minor/patch)
- Version satisfaction: operators, caret (^), tilde (~)
- Equality, comparison, toString
- Invalid/empty version handling

**Definition Hasher** (17 tests)
- Entity/field/enum/expression/access rule hashing
- Field order independence
- SHA256 format validation

### 8.2 Version-Aware Routing & Upgrades

**Version Aware Router** (7 tests)
- No active upgrade: base table
- Preparing/dual version/cutover phase routing

**Upgrade Job Service** (8 tests)
- Execute upgrade with valid window
- Validation result accumulation
- V1 cleanup: grace period, not found

**Dual Version Sync Service** (8 tests)
- Sync trigger generation between V1/V2 schemas

### 8.3 Code Generation for Registry

**PgSql Action Generator** (20 tests)
- Action function generation with parameters, security context, entity context
- Statement types: validate, warn, compute, let, emit, return, raise, when
- Type mapping, schema usage

**Migration Generator** (11 tests)
- Add/drop column, alter type, nullable changes
- Up/down script generation

**Sync Trigger Generator** (7 tests)
- V2-to-V1 sync: field mappings, string widening, decimal precision

### 8.4 Persistence & Performance (8 tests)
- Child identity preservation (field/index/entity IDs)
- EF Core optimization benchmarks

### 8.5 E2E Module Compilation (~26 tests)
- Compile valid module
- Return entity count
- PublishFalse does not persist
- Invalid syntax returns errors
- Missing dependency warning
- Auth required (401 without/with invalid key)
- Same module twice updates version
- Edge cases (18 tests)

---

## 9. Multi-Tenancy (~19 tests)

### 9.1 E2E Tenant Isolation (8 tests)
- Query returns only own tenant data
- Create user associated with current tenant
- Update/delete other tenant data blocked
- Header spoof does not grant access
- Filter still respects tenant
- Empty result for new tenant
- Update own tenant data works

### 9.2 Tenant Management (5 tests)
- Get tenant details
- Update tenant fields
- List my tenants / modules
- Duplicate tenant code rejected

### 9.3 Legacy E2E (6 tests)
- Create/list/update/delete tenant
- Tenant-module-versioning workflow

---

## 10. Temporal Data (~77 tests)

### 10.1 E2E Temporal (31 tests)
- system_start/system_end auto-set on creation
- Update creates new version, preserves fields, entity ID constant
- Rapid updates produce distinct versions
- AsOf query (point-in-time), before creation, future time
- Versions endpoint
- Closed system_end, contiguous time ranges
- Delete handling for temporal entities
- Historical records remain queryable
- Separate tables strategy
- Include/exclude history
- Bulk temporal operations
- Boundary time precision

### 10.2 Temporal DDL (11 tests)
- See Section 2.1

### 10.3 Temporal Validation (21 tests)
- See Section 1.2

### 10.4 Temporal Interval Operators (22 tests)
- See Section 1.1

---

## 11. Other Features

### 11.1 Unit of Work / Transactions (~50 tests)
- IsStarted lifecycle
- PendingEvents: enqueue, read-only list, order preservation
- Commit: dispatches events, handles dispatch failure
- Rollback: clears pending events
- Query executor UoW routing
- Deep insert/update handler UoW
- E2E transaction safety: atomicity, concurrent isolation, read independence

### 11.2 MetaModel Core (~145 tests)
- Entity: defaults, qualified names, fields, associations, compositions, aspects, indexes, constraints, annotations
- Field: key, nullable, virtual, readonly, immutable, stored computed, default value, type ref
- Type references: all primitives, custom, entity, array, localized
- Edge cases: empty name, very long name, special chars, unicode, self-referencing

### 11.3 On-Delete Behavior (21 tests)
- DeleteAction: Cascade, Restrict, SetNull, NoAction
- Check delete: default restricts when references exist
- Cascade: deletes referencing rows
- SetNull: nullifies FK
- Composition cascade vs restrict

### 11.4 Platform Bootstrap (10 tests)
- Platform module bootstrapped
- Identity, Tenant, Role entities exist
- User management: list, create, get, update, delete, assign/remove role
- Duplicate email rejected

### 11.5 Data Integrity (~35 tests)
- Unique constraints: PK, SKU
- FK: valid/invalid parent, delete parent with children
- Required fields enforcement
- Data types validation
- Concurrent updates, transaction rollback, orphan prevention

### 11.6 Advanced Features (~40 tests)

**Inheritance** (6 tests)
- Create child entities (Car, Truck)
- Polymorphic GET returns all derived
- Abstract entity direct create fails

**Array Types** (4 tests)
- String and integer arrays
- Update/empty array

**Many-to-Many** (4 tests)
- Junction table CRUD
- Expand linked entities
- Delete link/entity cleanup

**Action Contracts** (3 tests)
- Contract enforcement in metadata

### 11.7 Sequences (5 tests)
- List, get next (increment), sequential calls, get current, reset

### 11.8 Dynamic Views (3 tests)
- List defined views
- Query view data with OData params

### 11.9 Bulk Import (5 tests)
- Multiple records, duplicate key error, empty array 400, computed fields stripped, large dataset

### 11.10 Health Checks (4 tests)
- /health, /health/ready, /health/live (no auth required)

### 11.11 Cache Management (~23 tests)
- Reload success, idempotency
- Invalidation after create/update/delete/bulk
- Coherence: same query, different clients, after reload
- Concurrent access: read while write, multiple writes, high load

### 11.12 Schema Management (26 tests)
- Platform/core schema existence
- Table column verification
- All field types, reserved keywords, decimal precision, unicode
- Self-referencing FK, cross-schema FK
- Migration idempotency, data preservation

### 11.13 Performance Benchmarks (17 tests)
- Query benchmarks: simple, list, create, update, delete, filter, expand
- Bulk create 100, bulk query 1000
- Concurrent reads 50, concurrent writes 20
- Cache hit performance, pagination

### 11.14 Edge Cases (47 tests)
- DateTime ISO format, timezone handling
- Null optional fields, very long strings, special characters
- Zero values, GUID formats

### 11.15 Entity Relationships (9 tests)
- Create child with valid/invalid parent
- Expand parent from child, nested expand
- Filter on expanded property

### 11.16 Versioning (Legacy) (12 tests)
- Schedule/rollback upgrade
- Dual version transitions
- Version history
- Breaking change detection workflow

---

## Summary by Domain

| Domain | Unit Tests | E2E Tests | Total |
|---|---|---|---|
| 1. DSL / Compiler | ~650 | ~30 | ~680 |
| 2. Code Generation / DDL | ~375 | ~26 | ~401 |
| 3. Runtime / OData | ~470 | ~350 | ~820 |
| 4. Business Rules | ~155 | ~45 | ~200 |
| 5. Expression System | ~170 | - | ~170 |
| 6. Authorization | ~51 | ~44 | ~95 |
| 7. Events / Audit | ~155 | ~14 | ~169 |
| 8. Registry / Admin | ~117 | ~26 | ~143 |
| 9. Multi-Tenancy | - | ~19 | ~19 |
| 10. Temporal Data | ~40 | ~37 | ~77 |
| 11. Other Features | ~47+ | ~140+ | ~187+ |
| **Total** | **~2,230** | **~700** | **~2,930** |
