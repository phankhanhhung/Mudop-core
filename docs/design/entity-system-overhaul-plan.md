# BMMDL Entity System Overhaul — Complete Plan

## Mục tiêu

Biến entity/aspect system từ "copy-paste có quản lý" thành hệ thống hoàn chỉnh:
- **Behavioral Aspects (AOP)**: Aspects mang theo cả data + behavior
- **Entity Inheritance (is-a)**: Table-per-type với polymorphic query
- **Entity Extension**: Module mở rộng entity module khác
- **Entity Modification**: Sửa/xóa/rename fields đã có

---

## ~~Phase 0: Fix Recursive Aspect Chains (Bug Fix)~~ — DONE

> ~~Aspect chain hiện tại chỉ inline 1 cấp.~~ **FIXED** — `ResolveAspectChain()` uses DFS post-order traversal with cycle detection.

### Implementation (completed)

- `OptimizationPass.ResolveAspectChain()`: Recursive DFS with `visited` HashSet for circular detection
- `InlineAspects()`: Resolves full transitive chain, inlines fields + associations with dedup
- `InlineAspectBehaviors()`: Inlines rules, access controls, compositions from full chain
- Error code: `OPT_CIRCULAR_ASPECT` for circular includes
- **49 tests**: 21 compiler unit + 11 DDL codegen + 17 pipeline E2E

---

## Phase 1: Behavioral Aspects (AOP) — **DONE**

> Aspect chứa cả fields lẫn rules. Khi entity dùng aspect, compiler inline TẤT CẢ.
>
> **Implementation status**: COMPLETE. Grammar `aspectElement` with `ruleDef`/`accessControlDef`, `BmAspect.Rules`/`AccessControls`, `InlineAspectBehaviors()` in OptimizationPass, `$old` in RuleEngine, `reject` statement (grammar + BmRejectStatement + short-circuit execution). 46 tests (16 reject + 30 behavioral aspect).

### 1.1 Grammar — Cho phép rules trong aspects

| File | Change |
|------|--------|
| `Grammar/BmmdlParser.g4` | Thêm `aspectElement` rule mới |

**Current:**
```antlr
aspectDef
    : ASPECT IDENTIFIER (COLON identifierReference (COMMA identifierReference)*)?
      LBRACE entityElement* RBRACE
    ;
```

**New:**
```antlr
aspectDef
    : ASPECT IDENTIFIER (COLON identifierReference (COMMA identifierReference)*)?
      LBRACE aspectElement* RBRACE
    ;

aspectElement
    : entityElement         // fields, associations, compositions, actions, indexes...
    | aspectRuleDef         // inline behavior rules
    | accessControlInline   // inline access control
    ;

// Rule inside aspect — no "for Entity" clause (target determined at inline time)
aspectRuleDef
    : annotation* ON triggerTiming triggerOp (COMMA triggerOp)* LBRACE ruleStatement* RBRACE
    ;

// Inline access control — no "for Entity" clause
accessControlInline
    : GRANT opList TO principal (WHERE expression)? SEMICOLON
    | DENY opList TO principal SEMICOLON
    ;
```

This enables:
```bmmdl
aspect Auditable {
    createdAt: Timestamp;
    createdBy: String(100);
    modifiedAt: Timestamp;
    modifiedBy: String(100);

    on before create {
        set createdAt = $now;
        set createdBy = $user.name;
        set modifiedAt = $now;
        set modifiedBy = $user.name;
    }

    on before update {
        set modifiedAt = $now;
        set modifiedBy = $user.name;
    }
}
```

### 1.2 MetaModel — BmAspect gains behavior

| File | Change |
|------|--------|
| `src/BMMDL.MetaModel/BmModel.cs` | Add `Rules`, `AccessControls` to `BmAspect` |

```csharp
public class BmAspect : INamedElement, IAnnotatable
{
    // existing...
    public List<BmField> Fields { get; } = new();
    public List<BmAssociation> Associations { get; } = new();
    public List<string> Includes { get; } = new();

    // NEW: behavioral
    public List<BmAspectRule> Rules { get; } = new();           // ← ADD
    public List<BmAccessRule> AccessControls { get; } = new();  // ← ADD
}

/// <summary>
/// Rule defined inside an aspect. No TargetEntity — resolved at inline time.
/// </summary>
public class BmAspectRule
{
    public List<BmTriggerEvent> Triggers { get; } = new();
    public List<BmRuleStatement> Statements { get; } = new();
    public List<BmAnnotation> Annotations { get; } = new();
}
```

### 1.3 Model Builder — Parse aspect rules

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` | Update `VisitAspectDef` to parse `aspectRuleDef` |

```
VisitAspectDef:
  for each aspectElement:
    if entityElement → existing logic (fields, associations...)
    if aspectRuleDef → parse triggers + statements → add to aspect.Rules
    if accessControlInline → parse grant/deny → add to aspect.AccessControls
```

### 1.4 OptimizationPass — Inline rules alongside fields

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Pipeline/Passes/OptimizationPass.cs` | New phase: InlineAspectRules |

```
Phase 1a: Resolve aspect chains (Phase 0 fix)
Phase 1b: Inline aspect fields into entities (existing)
Phase 1c: Inline aspect rules into model.Rules  ← NEW
Phase 1d: Inline aspect access controls         ← NEW
```

Inlining rules:
```csharp
private int InlineAspectRules(BmModel model, CompilationContext context)
{
    foreach (var entity in model.Entities)
    {
        foreach (var aspectName in entity.Aspects)
        {
            if (aspectLookup.TryGetValue(aspectName, out var aspect))
            {
                foreach (var aspectRule in aspect.Rules)
                {
                    // Create concrete rule targeted at this entity
                    var rule = new BmRule
                    {
                        Name = $"__{aspectName}_{entity.Name}_{trigger}",
                        TargetEntity = entity.QualifiedName,
                        Triggers = aspectRule.Triggers.ToList(),
                        Statements = aspectRule.Statements.ToList()
                    };
                    model.Rules.Add(rule);
                }
            }
        }
    }
}
```

### 1.5 RuleEngine — New statement types for AOP

| File | Change |
|------|--------|
| `src/BMMDL.MetaModel/BmModel.cs` | Add `BmRejectStatement` |
| `src/BMMDL.Runtime/Rules/RuleEngine.cs` | Handle `reject`, support `$old` |
| `src/BMMDL.Runtime/Expressions/EvaluationContext.cs` | Add `OldEntityData` |
| `src/BMMDL.Runtime/Expressions/RuntimeExpressionEvaluator.cs` | Handle `$old.fieldName` |

**New statement type:**
```csharp
public class BmRejectStatement : BmRuleStatement
{
    public string? Message { get; set; }
}
```

**`$old` context for update rules:**
```csharp
// EvaluationContext
public Dictionary<string, object?>? OldEntityData { get; set; }

// RuntimeExpressionEvaluator — handle $old.fieldName
case "old":
    var oldField = contextVar.Path.Skip(1).First();
    return context.OldEntityData?.GetValueOrDefault(oldField);
```

**RuleEngine changes:**
```csharp
// In ExecuteBeforeUpdateAsync — pass old data separately
evalContext.OldEntityData = existingData;  // ← NEW
evalContext.EntityData = mergedData;       // current (existing behavior)

// In ExecuteStatementAsync — handle reject
case BmRejectStatement reject:
    result.IsRejected = true;
    result.AddError("", reject.Message ?? "Operation rejected by rule", BmSeverity.Error);
    return result;  // short-circuit
```

### 1.6 Remove hardcoded audit/soft-delete logic

| File | Change |
|------|--------|
| `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` | Remove hardcoded field name checks |
| `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` | Remove hardcoded field skip logic |

**Before** (hardcoded):
```csharp
// DynamicEntityController.cs:984
if (fieldName.Equals("createdAt", StringComparison.OrdinalIgnoreCase) || ...)
    continue;
```

**After** (aspect-driven):
```csharp
// Use @Core.Computed annotation from aspect instead
if (field.HasAnnotation("Core.Computed") || field.IsComputed)
    continue;  // Handled by rules, not client input
```

This way, the `@Core.Computed` annotation on aspect fields drives the behavior, not hardcoded field names.

### 1.7 SoftDeletable auto-detection

| File | Change |
|------|--------|
| `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` | Auto-detect soft delete from aspect |
| `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` | Auto-filter `isDeleted eq false` |

```csharp
// In Delete action — detect SoftDeletable aspect
var hasSoftDelete = entityDef.Aspects.Contains("SoftDeletable")
    || entityDef.Fields.Any(f => f.Name.Equals("isDeleted", StringComparison.OrdinalIgnoreCase));
var soft = hasSoftDelete;  // Instead of hardcoded false

// In BuildSelectQuery — auto-filter deleted records
if (entity.Fields.Any(f => f.Name == "isDeleted"))
{
    whereClauses.Add($"{alias}.is_deleted = false");
}
```

But with behavioral aspects, this becomes **unnecessary** — the aspect's `on before delete` rule handles everything via `reject` + `compute`.

### 1.8 Update sample aspects

| File | Change |
|------|--------|
| `samples/common.bmmdl` | Add behavioral rules to Auditable, SoftDeletable, Versioned |

---

## Phase 2: Entity Extension (`extend entity`) — **DONE**

> Module B extends entity from Module A without modifying Module A's source.
>
> **Implementation status**: COMPLETE. Grammar `extendDef` (EXTEND ENTITY/TYPE/ASPECT/SERVICE), BmExtension model class, VisitExtendDef in ModelBuilder, ExtensionMergePass (order 55) merges fields/associations/compositions/WITH-aspects, EXT_KEY_REDEFINITION validation. 49 tests (28 unit + 21 pipeline).

### 2.1 Model Builder — VisitExtendDef

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` | Add `VisitExtendDef` |

```csharp
private void VisitExtendDef(BmmdlParser.ExtendDefContext context, List<BmAnnotation> annotations)
{
    var targetName = context.identifierReference(0).GetText();

    // Create extension entity (marked as IsExtension)
    var extension = new BmEntity
    {
        Name = targetName,
        ExtendsFrom = targetName,
        // Parse fields, associations from LBRACE entityElement* RBRACE
    };

    // Parse "WITH aspect1, aspect2" clause
    for (int i = 1; i < context.identifierReference().Length; i++)
    {
        extension.Aspects.Add(context.identifierReference(i).GetText());
    }

    // Parse inline fields
    foreach (var elem in context.entityElement())
    {
        VisitEntityElement(elem, extension, annotations);
    }

    context.Model.Extensions.Add(extension);  // New collection
}
```

### 2.2 MetaModel — Extensions collection

| File | Change |
|------|--------|
| `src/BMMDL.MetaModel/BmModel.cs` | Add `Extensions` list |
| `src/BMMDL.MetaModel/Structure/BmEntity.cs` | `IsExtension` already exists |

```csharp
public class BmModel
{
    // existing...
    public List<BmEntity> Extensions { get; } = new();  // ← ADD
}
```

### 2.3 New Compiler Pass — ExtensionMergePass (order 55)

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Pipeline/Passes/ExtensionMergePass.cs` | NEW FILE |
| `src/BMMDL.Compiler/Pipeline/CompilerPipeline.cs` | Register new pass |

```
Pass order: 55 (after SemanticValidation=50, before Optimization=60)

Algorithm:
1. For each extension in model.Extensions:
   a. Find target entity in model.Entities by QualifiedName
   b. If not found → error EXT_TARGET_NOT_FOUND
   c. Merge extension fields into target (check no duplicate names)
   d. Merge extension aspects into target
   e. Merge extension associations into target
2. Clear model.Extensions after merge
```

**Validation rules:**
- Target entity must exist: `EXT_TARGET_NOT_FOUND`
- No duplicate field names: `EXT_DUPLICATE_FIELD`
- No removing key fields: `EXT_CANNOT_REMOVE_KEY`
- Extension cannot redefine key: `EXT_KEY_REDEFINITION`

### 2.4 DDL Migration awareness

| File | Change |
|------|--------|
| `src/BMMDL.CodeGen/Schema/MigrationScriptGenerator.cs` | `ALTER TABLE ADD COLUMN` for new fields |

When recompiling with extensions, the migration generator must detect new fields and generate:
```sql
ALTER TABLE platform.orders ADD COLUMN new_field_from_extension varchar(100);
```

---

## Phase 3: Entity Modification (`modify entity`) — **DONE** ✓

> Change existing fields: rename, remove, change type, modify properties.

### 3.1 Model Builder — VisitModifyDef

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` | Add `VisitModifyDef` |

Grammar already supports (BmmdlParser.g4:449-465):
```antlr
modifyDef
    : MODIFY (ENTITY | TYPE) identifierReference LBRACE modifyAction* RBRACE ;

modifyAction
    : MODIFY IDENTIFIER LBRACE modifyProp* RBRACE SEMICOLON?
    | REMOVE IDENTIFIER SEMICOLON
    | RENAME IDENTIFIER TO IDENTIFIER SEMICOLON
    | CHANGE TYPE OF IDENTIFIER TO typeReference SEMICOLON
    | ADD fieldDef SEMICOLON
    ;
```

### 3.2 MetaModel — Modifications

| File | Change |
|------|--------|
| `src/BMMDL.MetaModel/BmModel.cs` | Add modification model classes |

```csharp
public class BmModification
{
    public string TargetEntity { get; set; } = "";
    public List<BmModifyAction> Actions { get; } = new();
}

public abstract class BmModifyAction { }
public class BmModifyField : BmModifyAction {
    public string FieldName { get; set; } = "";
    public string? NewType { get; set; }
    public string? NewDefault { get; set; }
    public List<BmAnnotation> Annotations { get; } = new();
}
public class BmRemoveField : BmModifyAction { public string FieldName { get; set; } = ""; }
public class BmRenameField : BmModifyAction {
    public string OldName { get; set; } = "";
    public string NewName { get; set; } = "";
}
public class BmChangeType : BmModifyAction {
    public string FieldName { get; set; } = "";
    public string NewType { get; set; } = "";
}
public class BmAddField : BmModifyAction { public BmField Field { get; set; } = new(); }
```

### 3.3 New Compiler Pass — ModificationPass (order 56)

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Pipeline/Passes/ModificationPass.cs` | NEW FILE |

```
Pass order: 56 (after ExtensionMerge=55, before Optimization=60)

Algorithm:
1. For each modification in model.Modifications:
   a. Find target entity
   b. Apply each action:
      - MODIFY: update field properties
      - REMOVE: check no FK references, remove field
      - RENAME: update field name + update all references (rules, access controls)
      - CHANGE TYPE: validate type compatibility, update field
      - ADD: same as extend (add new field)
```

**Validation rules:**
- Cannot remove key fields: `MOD_CANNOT_REMOVE_KEY`
- Cannot remove fields with FK references: `MOD_FIELD_IN_USE`
- Type change compatibility check: `MOD_TYPE_INCOMPATIBLE`
- Rename must update all references: compiler tracks

### 3.4 DDL Migration for modifications

| File | Change |
|------|--------|
| `src/BMMDL.CodeGen/Schema/MigrationScriptGenerator.cs` | ALTER TABLE for rename, type change, drop column |

```sql
-- RENAME
ALTER TABLE platform.orders RENAME COLUMN old_name TO new_name;

-- CHANGE TYPE
ALTER TABLE platform.orders ALTER COLUMN price TYPE numeric(18,4);

-- REMOVE
ALTER TABLE platform.orders DROP COLUMN legacy_field;

-- ADD
ALTER TABLE platform.orders ADD COLUMN new_field varchar(100);
```

---

## Phase 4: Entity Inheritance — Table-Per-Type (is-a)

> True entity hierarchy: `entity Manager extends Employee` creates 2 tables linked by PK/FK.

### 4.1 Grammar — Distinguish `extends` from aspects

| File | Change |
|------|--------|
| `Grammar/BmmdlParser.g4` | New `entityInheritance` rule |
| `Grammar/BmmdlLexer.g4` | `EXTENDS` token (already have it? check — if not, add) |

**New grammar:**
```antlr
entityDef
    : ENTITY IDENTIFIER entityInheritance? LBRACE entityElement* RBRACE
    ;

entityInheritance
    : EXTENDS identifierReference                                        // entity inheritance (is-a)
      (COLON identifierReference (COMMA identifierReference)*)?          // + optional aspects (has-a)
    | COLON identifierReference (COMMA identifierReference)*             // aspects only (current)
    ;
```

This allows:
```bmmdl
// Aspect only (current, unchanged)
entity Order : Auditable, SoftDeletable { ... }

// Entity inheritance + aspects
entity Manager extends Employee : Auditable {
    department: String(100);
    directReports: composition [*] of Employee;
}

// Entity inheritance only
entity PremiumCustomer extends Customer {
    loyaltyTier: String(20);
    discountRate: Decimal(5,2);
}
```

### 4.2 MetaModel — Entity parent reference

| File | Change |
|------|--------|
| `src/BMMDL.MetaModel/Structure/BmEntity.cs` | Add inheritance properties |

```csharp
public class BmEntity : INamedElement, IAnnotatable
{
    // existing...

    /// <summary>
    /// Parent entity for table-per-type inheritance (is-a).
    /// Null means no parent (root entity).
    /// </summary>
    public string? ParentEntity { get; set; }

    /// <summary>
    /// True if this entity is a parent with child entities inheriting from it.
    /// </summary>
    public bool HasDerivedEntities { get; set; }

    /// <summary>
    /// Discriminator column value for this entity type.
    /// Default: entity name.
    /// </summary>
    public string? DiscriminatorValue { get; set; }

    /// <summary>
    /// If true, this entity cannot be instantiated directly (only via children).
    /// Maps to ABSTRACT keyword.
    /// </summary>
    public bool IsAbstract { get; set; }
}
```

### 4.3 Model Builder — Parse `extends`

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` | Update `VisitEntityDef` for inheritance |

```csharp
// In VisitEntityDef:
if (context.entityInheritance()?.EXTENDS() != null)
{
    // Entity inheritance (is-a)
    entity.ParentEntity = context.entityInheritance().identifierReference(0).GetText();

    // Optional aspects after colon
    var aspectRefs = context.entityInheritance().identifierReference().Skip(1);
    foreach (var aspectRef in aspectRefs)
        entity.Aspects.Add(aspectRef.GetText());
}
else if (context.entityInheritance()?.identifierReference() != null)
{
    // Aspect-only (current behavior, unchanged)
    foreach (var ref in context.entityInheritance().identifierReference())
        entity.Aspects.Add(ref.GetText());
}
```

### 4.4 New Compiler Pass — InheritanceResolutionPass (order 44)

| File | Change |
|------|--------|
| `src/BMMDL.Compiler/Pipeline/Passes/InheritanceResolutionPass.cs` | NEW FILE |

```
Pass order: 44 (after SymbolResolution=40, before DependencyGraph=45)

Algorithm:
1. Build inheritance tree from ParentEntity references
2. Detect circular inheritance → error INH_CIRCULAR
3. For each child entity:
   a. Validate parent entity exists
   b. Child inherits parent's key definition (MUST be same key)
   c. Child does NOT duplicate parent fields (table-per-type)
   d. Mark parent: HasDerivedEntities = true
   e. Set DiscriminatorValue = entity.Name (default)
4. Validate: abstract entities cannot be used in services directly
5. Store resolved inheritance graph in CompilationContext
```

**Validation rules:**
- `INH_CIRCULAR` — Circular inheritance detected
- `INH_PARENT_NOT_FOUND` — Parent entity doesn't exist
- `INH_KEY_MISMATCH` — Child redefines key differently from parent
- `INH_ABSTRACT_IN_SERVICE` — Abstract entity exposed in service directly

### 4.5 DDL Generation — Table-per-type

| File | Change |
|------|--------|
| `src/BMMDL.CodeGen/PostgresDdlGenerator.cs` | Generate inheritance tables |

**Parent table:**
```sql
CREATE TABLE platform.employees (
    id UUID PRIMARY KEY,
    _entity_type VARCHAR(100) NOT NULL DEFAULT 'Employee',  -- discriminator
    name VARCHAR(100) NOT NULL,
    email VARCHAR(255),
    hire_date DATE,
    tenant_id UUID  -- from TenantAware aspect
);
CREATE INDEX idx_employees_type ON platform.employees(_entity_type);
```

**Child table:**
```sql
CREATE TABLE platform.managers (
    id UUID PRIMARY KEY REFERENCES platform.employees(id) ON DELETE CASCADE,
    -- NO _entity_type here (lives in parent)
    -- NO name, email, hire_date (inherited from parent)
    department VARCHAR(100),
    budget NUMERIC(15,2)
);
```

**Insert trigger** (auto-set discriminator):
```sql
CREATE OR REPLACE FUNCTION platform.set_employee_type()
RETURNS TRIGGER AS $$
BEGIN
    -- Set on parent table when child is inserted
    UPDATE platform.employees SET _entity_type = TG_ARGV[0] WHERE id = NEW.id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_managers_set_type
AFTER INSERT ON platform.managers
FOR EACH ROW EXECUTE FUNCTION platform.set_employee_type('Manager');
```

### 4.6 DynamicSqlBuilder — Inheritance-aware queries

| File | Change |
|------|--------|
| `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` | JOIN parent/child tables |

**Query child entity (Manager):**
```sql
-- SELECT from child automatically JOINs parent
SELECT p.id, p.name, p.email, p.hire_date,      -- parent fields
       m.department, m.budget                     -- child fields
FROM platform.managers m
INNER JOIN platform.employees p ON m.id = p.id
WHERE p.tenant_id = @tenantId
  AND p._entity_type = 'Manager'
```

**Query parent entity (Employee — polymorphic):**
```sql
-- SELECT from parent returns ALL types (Employee, Manager, etc.)
SELECT p.id, p.name, p.email, p.hire_date, p._entity_type
FROM platform.employees p
WHERE p.tenant_id = @tenantId
```

**Query parent with child expansion:**
```sql
-- $expand=managerDetails (virtual navigation to child table)
SELECT p.id, p.name, p._entity_type,
       m.department, m.budget
FROM platform.employees p
LEFT JOIN platform.managers m ON p.id = m.id
WHERE p.tenant_id = @tenantId
```

**INSERT child entity:**
```sql
-- Transaction: insert parent first, then child
BEGIN;
INSERT INTO platform.employees (id, name, email, _entity_type, tenant_id)
VALUES (@id, @name, @email, 'Manager', @tenantId)
RETURNING *;

INSERT INTO platform.managers (id, department, budget)
VALUES (@id, @department, @budget);
COMMIT;
```

### 4.7 MetaModelCache — Inheritance resolution

| File | Change |
|------|--------|
| `src/BMMDL.Runtime/MetaModelCache.cs` | Resolve parent fields for child entities |

```csharp
// New method: get ALL fields (own + inherited)
public IReadOnlyList<BmField> GetAllFields(string entityName)
{
    var entity = GetEntity(entityName);
    if (entity?.ParentEntity == null)
        return entity?.Fields ?? [];

    var parentFields = GetAllFields(entity.ParentEntity);
    return parentFields.Concat(entity.Fields).ToList();
}

// New method: get inheritance chain
public IReadOnlyList<BmEntity> GetInheritanceChain(string entityName) { ... }

// New method: get all child entities
public IReadOnlyList<BmEntity> GetDerivedEntities(string entityName) { ... }
```

### 4.8 DynamicEntityController — Inheritance CRUD

| File | Change |
|------|--------|
| `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` | Multi-table CRUD |

**CREATE:** Insert parent row first (with discriminator), then child row. Transaction.
**READ:** Auto-JOIN parent + child tables. $select can reference parent fields.
**UPDATE:** Detect which fields belong to which table, issue separate UPDATEs.
**DELETE:** Delete child row (CASCADE deletes parent via FK, or reverse).

### 4.9 Polymorphic API

When querying parent entity, response includes `_entity_type` discriminator:
```json
{
    "@odata.context": "$metadata#Employees",
    "value": [
        {
            "@odata.type": "#Platform.Employee",
            "id": "...",
            "name": "Alice",
            "_entity_type": "Employee"
        },
        {
            "@odata.type": "#Platform.Manager",
            "id": "...",
            "name": "Bob",
            "_entity_type": "Manager",
            "department": "Engineering"  // via auto-expand
        }
    ]
}
```

### 4.10 Abstract entity support

Now `abstract` gets a clear purpose:

```bmmdl
abstract entity Document : Auditable {
    key ID: UUID;
    title: String(200);
    status: DocumentStatus;
}

entity Invoice extends Document {
    invoiceNumber: String(50);
    total: Decimal(15,2);
}

entity PurchaseOrder extends Document {
    poNumber: String(50);
    vendor: association [0..1] to Vendor;
}
```

- `abstract` → table IS created (parent table for inheritance), but **no direct CRUD via API**
- Querying `Document` returns all Invoice + PurchaseOrder rows (polymorphic)
- POST to `Document` → error 400: "Cannot create instances of abstract entity"

---

## Phase 5: Cross-Aspect Views (Polymorphic Query cho Aspects) — **DONE** ✓

> Nếu muốn query "tất cả entities có Auditable mà modifiedAt > hôm qua".

### 5.1 Auto-generated UNION views

| File | Change |
|------|--------|
| `src/BMMDL.CodeGen/PostgresDdlGenerator.cs` | Generate UNION views for annotated aspects |

```bmmdl
@Query.CrossAspect
aspect Auditable { ... }
```

Compiler auto-generates:
```sql
CREATE OR REPLACE VIEW platform.__all_auditable AS
  SELECT 'Order' AS _entity_type, id, created_at, created_by, modified_at, modified_by
    FROM platform.orders
  UNION ALL
  SELECT 'Invoice' AS _entity_type, id, created_at, created_by, modified_at, modified_by
    FROM platform.invoices
  -- ... for each entity with : Auditable
;
```

**This is OPTIONAL and opt-in.** Most aspects don't need cross-entity queries.

---

## Execution Order & Dependencies

```
Phase 0 ─── Fix recursive aspect chains
   │        (prerequisite for everything)
   ▼
Phase 1 ─── Behavioral Aspects (AOP)
   │        1.1 Grammar
   │        1.2 MetaModel
   │        1.3 ModelBuilder
   │        1.4 OptimizationPass
   │        1.5 RuleEngine ($old, reject)
   │        1.6 Remove hardcoded audit logic
   │        1.7 SoftDeletable auto-detection
   │        1.8 Sample aspects
   │
   ├──────→ Phase 2 ─── extend entity (independent of Phase 4)
   │           2.1 ModelBuilder
   │           2.2 MetaModel
   │           2.3 ExtensionMergePass
   │           2.4 DDL migration
   │
   ├──────→ Phase 3 ─── modify entity (needs Phase 2 first)
   │           3.1 ModelBuilder
   │           3.2 MetaModel
   │           3.3 ModificationPass
   │           3.4 DDL migration
   │
   └──────→ Phase 4 ─── Entity Inheritance (independent of 2,3)
               4.1 Grammar
               4.2 MetaModel
               4.3 ModelBuilder
               4.4 InheritanceResolutionPass
               4.5 DDL generation
               4.6 DynamicSqlBuilder
               4.7 MetaModelCache
               4.8 DynamicEntityController
               4.9 Polymorphic API
               4.10 Abstract entity

Phase 5 ─── Cross-Aspect Views (optional, after Phase 1)
```

## Compiler Pass Order (Final)

| Order | Pass | Status |
|-------|------|--------|
| 1 | LexicalPass | existing |
| 2 | SyntacticPass | existing |
| 3 | ModelBuildPass | MODIFIED (extend, modify, inheritance) |
| 40 | SymbolResolutionPass | existing |
| 44 | **InheritanceResolutionPass** | **NEW** |
| 45 | DependencyGraphPass | existing |
| 46 | ExpressionDependencyPass | existing |
| 47 | BindingPass | existing |
| 48 | TenantIsolationPass | MODIFIED (inheritance-aware) |
| 49 | FileStorageValidationPass | existing |
| 49 | TemporalValidationPass | existing |
| 50 | SemanticValidationPass | MODIFIED (inheritance validation) |
| 55 | **ExtensionMergePass** | **NEW** |
| 56 | **ModificationPass** | **NEW** |
| 60 | OptimizationPass | MODIFIED (behavioral aspect inlining) |

## Files Changed Summary

### New Files (5)
- `src/BMMDL.Compiler/Pipeline/Passes/InheritanceResolutionPass.cs`
- `src/BMMDL.Compiler/Pipeline/Passes/ExtensionMergePass.cs`
- `src/BMMDL.Compiler/Pipeline/Passes/ModificationPass.cs`
- Test files for each phase

### Modified Files (~15)
- `Grammar/BmmdlLexer.g4` — EXTENDS token
- `Grammar/BmmdlParser.g4` — aspectElement, entityInheritance
- `src/BMMDL.MetaModel/BmModel.cs` — BmAspectRule, BmModification, Extensions collection
- `src/BMMDL.MetaModel/Structure/BmEntity.cs` — ParentEntity, IsAbstract, DiscriminatorValue
- `src/BMMDL.Compiler/Parsing/BmmdlModelBuilder.cs` — VisitExtendDef, VisitModifyDef, VisitAspectDef update
- `src/BMMDL.Compiler/Pipeline/CompilerPipeline.cs` — Register new passes
- `src/BMMDL.Compiler/Pipeline/Passes/OptimizationPass.cs` — Recursive aspect, behavioral inlining
- `src/BMMDL.CodeGen/PostgresDdlGenerator.cs` — Inheritance tables, cross-aspect views
- `src/BMMDL.CodeGen/Schema/MigrationScriptGenerator.cs` — ALTER TABLE for extend/modify
- `src/BMMDL.Runtime/DataAccess/DynamicSqlBuilder.cs` — Inheritance JOINs
- `src/BMMDL.Runtime/MetaModelCache.cs` — Aspect indexing, inheritance resolution
- `src/BMMDL.Runtime/Rules/RuleEngine.cs` — reject, $old
- `src/BMMDL.Runtime/Expressions/EvaluationContext.cs` — OldEntityData
- `src/BMMDL.Runtime/Expressions/RuntimeExpressionEvaluator.cs` — $old handling
- `src/BMMDL.Runtime.Api/Controllers/DynamicEntityController.cs` — Inheritance CRUD, remove hardcoded
- `samples/common.bmmdl` — Behavioral aspects

## Risk Assessment

| Phase | Risk | Mitigation |
|-------|------|------------|
| 0 | Low | Simple fix, well-scoped |
| 1 | Medium | Grammar change + runtime change. Test aspect inlining thoroughly |
| 2 | Medium | Extension merge ordering matters. Test cross-module dependencies |
| 3 | High | Schema migration for rename/type change can break data. Need dry-run mode |
| 4 | High | Multi-table CRUD is complex. JOIN performance. Transaction management |
| 5 | Low | View generation is simple, opt-in |
