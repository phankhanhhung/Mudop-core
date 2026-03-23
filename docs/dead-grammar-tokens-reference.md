# Dead Grammar Tokens — Use Case Reference

> Created: 2026-02-15
> Source: `Grammar/BmmdlLexer.g4` — 44 tokens defined with no parser rules consuming them

This document describes the intended purpose, importance, and BMMDL usage examples for every dead token in the lexer. Tokens are grouped by category and ranked by implementation value.

---

## Category 1: Window Functions (16 tokens)

**Implementation Value: HIGH**
Window functions are essential for analytics, reporting, and computed views. SAP CDS and OData Analytics both support them. These would enable ranking, pagination, running totals, and time-series analysis directly in BMMDL views and computed fields.

---

### `ROW_NUMBER`
**Lexer:** Line 252 — `'row_number'`

Assigns a unique sequential integer to each row within a partition. The most commonly used window function — critical for pagination, deduplication, and row-based logic.

**Use case:** Number invoices within each customer, pick the latest record per group, implement server-side keyset pagination.

```bmmdl
view CustomerInvoiceRanking as
    SELECT
        row_number() over (partition by CustomerId order by CreatedAt desc) as RowNum,
        InvoiceNumber,
        CustomerId,
        Amount
    FROM Invoice;

// Get only the latest invoice per customer
view LatestCustomerInvoice as
    SELECT * FROM CustomerInvoiceRanking WHERE RowNum = 1;
```

---

### `RANK`
**Lexer:** Line 253 — `'rank'`

Assigns a rank to each row within a partition, with gaps after ties. Two rows with the same value get the same rank; the next rank is skipped.

**Use case:** Sales leaderboards, competition standings, grading where ties share a position.

```bmmdl
view SalesLeaderboard as
    SELECT
        rank() over (order by TotalRevenue desc) as SalesRank,
        SalesRepName,
        TotalRevenue,
        Region
    FROM SalesPerformance;

// Result: 1, 2, 2, 4 (rank 3 skipped because two reps tied at 2nd)
```

---

### `DENSE_RANK`
**Lexer:** Line 254 — `'dense_rank'`

Like `RANK` but without gaps — consecutive ranks even when there are ties. Preferred when you need to filter by rank number (e.g., "top 3 categories").

**Use case:** Top-N reports, tier classification, dense ranking for UI display.

```bmmdl
view ProductCategoryRanking as
    SELECT
        dense_rank() over (order by TotalSales desc) as CategoryRank,
        CategoryName,
        TotalSales
    FROM CategorySummary;

// Result: 1, 2, 2, 3 (no gap — rank 3 still exists)
// Useful: WHERE CategoryRank <= 3 returns exactly the top 3 tiers
```

---

### `NTILE`
**Lexer:** Line 255 — `'ntile'`

Distributes rows into N roughly equal buckets. Returns the bucket number (1 to N) for each row.

**Use case:** Percentile grouping, quartile analysis, load balancing distribution.

```bmmdl
view CustomerSpendQuartile as
    SELECT
        ntile(4) over (order by LifetimeSpend desc) as SpendQuartile,
        CustomerName,
        LifetimeSpend
    FROM Customer;

// Quartile 1 = top 25% spenders, Quartile 4 = bottom 25%
```

---

### `LAG`
**Lexer:** Line 256 — `'lag'`

Accesses a value from a previous row (default: 1 row back) within the same partition. Essential for time-series comparisons.

**Use case:** Month-over-month change, previous period comparison, detecting trend changes.

```bmmdl
view MonthlySalesComparison as
    SELECT
        Month,
        Revenue,
        lag(Revenue, 1) over (order by Month) as PreviousMonthRevenue,
        Revenue - lag(Revenue, 1) over (order by Month) as MonthOverMonthChange
    FROM MonthlySales;
```

---

### `LEAD`
**Lexer:** Line 257 — `'lead'`

Accesses a value from a subsequent row (default: 1 row ahead) within the same partition. The forward-looking counterpart to `LAG`.

**Use case:** Next delivery date, upcoming renewal, forward gap analysis.

```bmmdl
view SubscriptionRenewals as
    SELECT
        CustomerId,
        RenewalDate,
        lead(RenewalDate, 1) over (partition by CustomerId order by RenewalDate) as NextRenewalDate
    FROM Subscription;

// Shows each renewal alongside when the next one is due
```

---

### `FIRST_VALUE`
**Lexer:** Line 258 — `'first_value'`

Returns the first value in the window frame. Useful for comparing each row against the baseline (first entry).

**Use case:** Original price vs. current price, first order date, baseline comparison.

```bmmdl
view PriceChangeFromOriginal as
    SELECT
        ProductId,
        EffectiveDate,
        Price,
        first_value(Price) over (partition by ProductId order by EffectiveDate) as OriginalPrice,
        Price - first_value(Price) over (partition by ProductId order by EffectiveDate) as PriceChangeSinceLaunch
    FROM PriceHistory;
```

---

### `LAST_VALUE`
**Lexer:** Line 259 — `'last_value'`

Returns the last value in the window frame. Requires proper frame specification (`ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING`) to be useful.

**Use case:** Most recent value in a range, latest known price, end-of-period snapshot.

```bmmdl
view EmployeeSalaryRange as
    SELECT
        DepartmentId,
        EmployeeName,
        Salary,
        last_value(Salary) over (
            partition by DepartmentId order by Salary
            rows between unbounded preceding and unbounded following
        ) as HighestSalaryInDept
    FROM Employee;
```

---

### `OVER`
**Lexer:** Line 241 — `'over'`

The clause that turns an aggregate or ranking function into a window function. Without `OVER`, these are just regular aggregates. This is the syntactic foundation for all window functions.

**Use case:** Required by every window function — defines the window (partition + order + frame).

```bmmdl
// Without OVER: regular aggregate (collapses rows)
SELECT SUM(Amount) FROM OrderItem;

// With OVER: window function (preserves rows, adds running total)
SELECT
    OrderItemId,
    Amount,
    SUM(Amount) over (order by OrderItemId) as RunningTotal
FROM OrderItem;
```

---

### `PARTITION`
**Lexer:** Line 242 — `'partition'`

Used in `PARTITION BY` within a window `OVER` clause. Divides the result set into groups (partitions) for independent window calculations.

**Use case:** Per-department rankings, per-customer aggregations, per-category analytics.

```bmmdl
// Running total per customer (resets for each customer)
SELECT
    CustomerId,
    OrderDate,
    Amount,
    SUM(Amount) over (partition by CustomerId order by OrderDate) as CustomerRunningTotal
FROM SalesOrder;
```

---

### `ROWS`
**Lexer:** Line 245 — `'rows'`

Specifies a physical row-based window frame. Counts actual rows regardless of value. Used with `PRECEDING`/`FOLLOWING`/`UNBOUNDED`.

**Use case:** Moving averages over fixed row counts, sliding windows.

```bmmdl
// 3-row moving average
SELECT
    Date,
    Revenue,
    AVG(Revenue) over (order by Date rows between 2 preceding and current row) as MovingAvg3Day
FROM DailySales;
```

---

### `RANGE`
**Lexer:** Line 246 — `'range'`

Specifies a value-based window frame. Groups rows by value proximity rather than physical position. Used with `PRECEDING`/`FOLLOWING`/`UNBOUNDED`.

**Use case:** Value-range grouping, logical proximity windows (e.g., all rows within $100 of current row).

```bmmdl
// Sum of all orders within the same price range (same OrderDate value)
SELECT
    OrderDate,
    Amount,
    SUM(Amount) over (order by OrderDate range between current row and current row) as SameDayTotal
FROM SalesOrder;
```

---

### `ROW`
**Lexer:** Line 251 — `'row'`

Used in `CURRENT ROW` within frame specifications. Defines the anchor point for `ROWS BETWEEN ... AND CURRENT ROW` or `RANGE BETWEEN CURRENT ROW AND ...`.

**Use case:** All frame-based window functions need `CURRENT ROW` as a boundary reference.

```bmmdl
// Cumulative sum from start to current row
SELECT
    Date,
    Revenue,
    SUM(Revenue) over (order by Date rows between unbounded preceding and current row) as CumulativeRevenue
FROM DailySales;
```

---

### `UNBOUNDED`
**Lexer:** Line 247 — `'unbounded'`

Specifies the beginning or end of a partition as a frame boundary. `UNBOUNDED PRECEDING` = from the first row; `UNBOUNDED FOLLOWING` = to the last row.

**Use case:** Running totals (from start), reverse running totals (to end), full partition aggregates.

```bmmdl
// Full running total from beginning of partition
SELECT
    OrderId,
    Amount,
    SUM(Amount) over (order by OrderId rows between unbounded preceding and current row) as RunningTotal
FROM SalesOrder;
```

---

### `PRECEDING`
**Lexer:** Line 248 — `'preceding'`

Specifies rows before the current row in a window frame. `N PRECEDING` = N rows back; `UNBOUNDED PRECEDING` = from the start.

**Use case:** Lookback windows, trailing averages, historical comparisons.

```bmmdl
// 7-day trailing average
SELECT
    Date,
    Revenue,
    AVG(Revenue) over (order by Date rows between 6 preceding and current row) as TrailingAvg7Day
FROM DailySales;
```

---

### `FOLLOWING`
**Lexer:** Line 249 — `'following'`

Specifies rows after the current row in a window frame. `N FOLLOWING` = N rows ahead; `UNBOUNDED FOLLOWING` = to the end.

**Use case:** Forward-looking windows, forecast smoothing, centered moving averages.

```bmmdl
// Centered 5-day moving average (2 before, current, 2 after)
SELECT
    Date,
    Revenue,
    AVG(Revenue) over (order by Date rows between 2 preceding and 2 following) as CenteredAvg5Day
FROM DailySales;
```

---

## Category 2: Temporal / History (6 tokens)

**Implementation Value: MEDIUM**
BMMDL already supports bitemporal entities (`@Temporal` annotation). These tokens would extend temporal capabilities with richer query syntax and fiscal calendar support for financial reporting.

---

### `HISTORY`
**Lexer:** Line 129 — `'history'`

Marks a query as accessing historical (past) versions of temporal entities rather than only current data.

**Use case:** Audit trails, point-in-time reporting, temporal data exploration.

```bmmdl
// Query all historical versions of a price record
view PriceAuditTrail as
    SELECT history *
    FROM PriceHistory
    WHERE ProductId = :productId
    ORDER BY system_start DESC;

// Alternative: as a temporal qualifier
SELECT * FROM Employee TEMPORAL HISTORY ALL;
```

---

### `TRANSACTION`
**Lexer:** Line 131 — `'transaction'`

Refers to transaction time (system time) in bitemporal entities — when the database recorded the change, as opposed to when the change was valid in the real world.

**Use case:** Distinguishing "when was this recorded?" from "when was this effective?" in bitemporal queries.

```bmmdl
@Temporal(strategy: 'InlineHistory')
entity PriceHistory {
    key ID: UUID;
    price: Decimal(18,2);
    // transaction time: system_start, system_end (auto-managed)
    // valid time: effectiveFrom, effectiveTo (user-managed)
}

// Query by transaction time: "what did the system know as of Jan 1?"
SELECT * FROM PriceHistory
    TEMPORAL TRANSACTION ASOF '2026-01-01';
```

---

### `VALID`
**Lexer:** Line 130 — `'valid'`

Refers to valid time (business time) in bitemporal entities — when the data is effective in the real world, independent of when it was recorded.

**Use case:** Effective dating, retroactive corrections, future-dated entries.

```bmmdl
// Query by valid time: "what prices were effective on March 15?"
SELECT * FROM PriceHistory
    TEMPORAL VALID ASOF '2026-03-15';

// Bitemporal: "what did we know on Jan 1 about prices effective on March 15?"
SELECT * FROM PriceHistory
    TEMPORAL TRANSACTION ASOF '2026-01-01'
    TEMPORAL VALID ASOF '2026-03-15';
```

---

### `PERIOD`
**Lexer:** Line 136 — `'period'`

Defines a named temporal period (a pair of from/to columns). SQL:2011 standard concept for temporal tables.

**Use case:** Explicit period declaration for temporal entities, enabling `OVERLAPS`/`CONTAINS`/`PRECEDES` on named periods.

```bmmdl
@Temporal(strategy: 'InlineHistory')
entity EmployeeContract {
    key ID: UUID;
    employeeId: UUID;
    salary: Decimal(18,2);
    period ContractPeriod (startDate, endDate);
    startDate: Date;
    endDate: Date;
}

// Find overlapping contracts
SELECT * FROM EmployeeContract a, EmployeeContract b
    WHERE a.ContractPeriod OVERLAPS b.ContractPeriod
    AND a.employeeId = b.employeeId
    AND a.ID != b.ID;
```

---

### `FISCAL_YEAR`
**Lexer:** Line 376 — `'fiscalYear'`

Represents a fiscal year boundary for sequence resets and financial reporting. Many enterprises have fiscal years that don't align with calendar years (e.g., April-March, July-June).

**Use case:** Fiscal year-based sequence numbering, financial period filtering, budget cycles.

```bmmdl
sequence InvoiceNumber for Invoice.InvoiceNo {
    pattern: "INV-{YYYY}-{SEQ}";
    start: 1;
    increment: 1;
    padding: 6;
    scope: tenant;
    reset on fiscalYear;  // Reset numbering at fiscal year boundary
}

// Configuration: fiscal year starts April 1
@FiscalCalendar(startMonth: 4)
```

---

### `FISCAL_PERIOD`
**Lexer:** Line 377 — `'fiscalPeriod'`

Represents a fiscal period (month/quarter within the fiscal year). Enterprises often have non-standard period definitions (13-period calendars, 4-4-5 weeks, etc.).

**Use case:** Period-based sequence resets, financial period reporting, closing calendars.

```bmmdl
sequence JournalEntryNumber for JournalEntry.EntryNo {
    pattern: "JE-{PERIOD}-{SEQ}";
    start: 1;
    increment: 1;
    padding: 5;
    scope: tenant;
    reset on fiscalPeriod;  // Reset each fiscal period (month/quarter)
}

// Period-based budget rollup
view BudgetByFiscalPeriod as
    SELECT
        fiscalPeriod(PostingDate) as Period,
        SUM(Amount) as TotalBudget
    FROM BudgetEntry
    GROUP BY fiscalPeriod(PostingDate);
```

---

## Category 3: Operators (8 tokens)

**Implementation Value: LOW-MEDIUM**
Most of these are syntactic sugar or reserved for future expression types. The most valuable would be `ARROW`/`DOUBLE_ARROW` for lambda expressions and `PIPE` for pipeline transforms.

---

### `ARROW` (`->`)
**Lexer:** Line 413 — `'->'`

Arrow operator for member access, lambda expressions, or type mapping. Common in modern languages for lambda syntax.

**Use case:** Lambda expressions in transformations, type-safe navigation, pipeline step syntax.

```bmmdl
// Lambda in collection operations
rule ValidateLineItems for SalesOrder on before create {
    validate items -> item.quantity > 0
        message 'All line items must have positive quantity'
        severity error;
}

// Type mapping in migration transforms
migration "v2.0 restructure" {
    transform Customer {
        map OldField -> NewField;
    }
}
```

---

### `DOUBLE_ARROW` (`=>`)
**Lexer:** Line 414 — `'=>'`

Fat arrow operator, commonly used for mapping, projection, or return type declaration. In many DSLs this denotes "produces" or "maps to".

**Use case:** Computed field shorthand, projection mapping, event handler binding.

```bmmdl
// Shorthand computed field definition
entity Order {
    key ID: UUID;
    items: composition [*] of OrderItem;
    total => sum(items.amount);  // Short syntax for computed
}

// Event handler mapping
on OrderCreated => NotifyWarehouse;
on OrderShipped => UpdateTracking;
```

---

### `DOUBLE_COLON` (`::`)
**Lexer:** Line 422 — `'::'`

Scope resolution operator. Used in many languages for namespace-qualified access (C++, Ruby, Rust).

**Use case:** Explicit namespace-qualified symbol references, disambiguating same-named types across modules.

```bmmdl
// Disambiguate types from different modules
entity Order {
    key ID: UUID;
    billingAddress: finance::Address;    // Address from finance module
    shippingAddress: logistics::Address;  // Address from logistics module
}

// Explicit enum value reference
status: hr::EmployeeStatus::Active;
```

---

### `PIPE` (`|`)
**Lexer:** Line 418 — `'|'`

Pipe operator for chaining transformations, union types, or bitwise OR. Most modern DSLs use this for pipeline composition.

**Use case:** Union types for polymorphic fields, transformation pipelines, filter chaining.

```bmmdl
// Union type for polymorphic reference
entity Notification {
    key ID: UUID;
    target: Customer | Supplier | Employee;  // Can reference any of these
    message: String(500);
}

// Pipeline transform in migration
migration "v2.0 cleanup" {
    transform Customer {
        FullName | trim | upper -> NormalizedName;
    }
}
```

---

### `AMPERSAND` (`&`)
**Lexer:** Line 417 — `'&'`

Intersection operator or bitwise AND. In type systems, used for intersection types (must satisfy all).

**Use case:** Intersection types (combining aspect requirements), bitwise flag operations, composite annotations.

```bmmdl
// Intersection type: entity must satisfy both aspects
entity AuditedTenantEntity : Auditable & TenantAware {
    key ID: UUID;
    name: String(100);
}

// Bitwise permission flags
access control for Document {
    grant read to permission & 0x01;   // Read bit
    grant write to permission & 0x02;  // Write bit
}
```

---

### `CARET` (`^`)
**Lexer:** Line 419 — `'^'`

Exponentiation operator or bitwise XOR. In mathematical contexts, represents power.

**Use case:** Mathematical computed fields, compound interest calculations, scientific formulas.

```bmmdl
entity Investment {
    key ID: UUID;
    principal: Decimal(18,2);
    annualRate: Decimal(5,4);
    years: Integer;

    // Compound interest: A = P * (1 + r)^n
    virtual maturityValue: Decimal(18,2) computed =
        principal * (1 + annualRate) ^ years;
}
```

---

### `TILDE` (`~`)
**Lexer:** Line 420 — `'~'`

Bitwise NOT (complement) or fuzzy/approximate matching operator. In search contexts, often means "similar to".

**Use case:** Fuzzy text search, approximate matching, bitwise complement for flag manipulation.

```bmmdl
// Fuzzy search in $search-like contexts
rule FuzzyMatchCustomer for Customer on before read {
    // Approximate name matching (Levenshtein distance)
    validate searchTerm ~ name within 2
        message 'No approximate match found'
        severity warning;
}

// Bitwise complement for permission masking
access control for SensitiveData {
    deny all to ~role('SecurityOfficer');  // Everyone except SecurityOfficer
}
```

---

### `EXCLAIM` (`!`)
**Lexer:** Line 421 — `'!'`

Logical NOT operator. Alternative syntax to `NOT` keyword — more concise for boolean expressions.

**Use case:** Shorthand negation in expressions, boolean field checks, guard conditions.

```bmmdl
rule PreventInactiveOrders for SalesOrder on before create {
    // Shorthand for: NOT customer.isActive
    validate !customer.isActive = false
        message 'Cannot create order for inactive customer'
        severity error;
}

// Guard condition in action
action SubmitOrder(orderId: UUID) returns SalesOrder {
    requires !$context.entity.isSubmitted
        message 'Order already submitted';
}
```

---

## Category 4: Reserved / Miscellaneous (14 tokens)

**Implementation Value: VARIES**
These range from critical future features (`IF`, `TABLE`, `REF`) to encoding artifacts (`BOM`). Each has a specific intended purpose.

---

### `IF`
**Lexer:** Line 65 — `'if'`

Conditional branching keyword. BMMDL currently uses `when` for conditional rule statements, but `if` is the more universal conditional syntax.

**Use case:** Conditional logic in action bodies, migration scripts, computed field expressions.

```bmmdl
action ProcessPayment(orderId: UUID, method: String) returns PaymentResult {
    let order = SELECT * FROM SalesOrder WHERE ID = :orderId;

    if method = 'credit' {
        call ValidateCreditCard(order.paymentDetails);
        compute order.paymentStatus = #Processing;
    } else if method = 'invoice' {
        call GenerateInvoice(order);
        compute order.paymentStatus = #Invoiced;
    } else {
        raise error 'Unknown payment method';
    }

    return order;
}
```

---

### `TABLE`
**Lexer:** Line 97 — `'table'`

Refers to database table-level constructs. BMMDL already uses `tableDef` in the parser for views/projections, but the `TABLE` token itself is never referenced in parser rules.

**Use case:** Explicit table annotations, inheritance strategy declaration, table-level constraints.

```bmmdl
// Inheritance strategy annotation (currently uses annotations instead)
@Inheritance(strategy: 'table_per_type')
entity Vehicle {
    key ID: UUID;
    make: String(50);
}

// Explicit table mapping for legacy database integration
@Table(name: 'legacy_customers', schema: 'dbo')
entity Customer {
    key ID: UUID;
    name: String(100);
}
```

---

### `JOINED`
**Lexer:** Line 143 — `'joined'`

Table-per-type inheritance strategy keyword (also called "joined table" inheritance). Each entity in the hierarchy gets its own table, JOINed at query time.

**Use case:** Declaring inheritance mapping strategy explicitly in the DSL rather than via annotations.

```bmmdl
// Current approach (annotation):
@Inheritance(strategy: 'table_per_type')
entity Vehicle { ... }

// Potential DSL syntax with JOINED keyword:
entity Vehicle joined {
    key ID: UUID;
    make: String(50);
}

entity Car extends Vehicle {
    doorCount: Integer;  // Stored in separate 'car' table, joined to 'vehicle'
}
```

---

### `SEALED`
**Lexer:** Line 81 — `'sealed'`

Prevents an entity from being extended or inherited. Opposite of `abstract`. Common in C#/Java type systems.

**Use case:** Lock down core entities from modification by downstream modules, prevent extension abuse.

```bmmdl
// No other entity can extend this
sealed entity SystemConfiguration {
    key ID: UUID;
    parameterName: String(100);
    parameterValue: String(500);
}

// This would be a compile error:
// entity CustomConfig extends SystemConfiguration { ... }
// ERROR: Cannot extend sealed entity 'SystemConfiguration'
```

---

### `ONE`
**Lexer:** Line 76 — `'one'`

Readable cardinality keyword for single-valued relationships. Alternative to `[1..1]` or `[0..1]` notation.

**Use case:** More readable association/composition declarations.

```bmmdl
entity Order {
    key ID: UUID;
    // Readable cardinality syntax (alternative to [0..1])
    customer: association one to Customer;
    items: composition many of OrderItem;
}
```

---

### `MANY`
**Lexer:** Line 70 — `'many'`

Readable cardinality keyword for multi-valued relationships. Alternative to `[*]` or `[0..*]` notation.

**Use case:** More readable association/composition declarations.

```bmmdl
entity Customer {
    key ID: UUID;
    name: String(100);
    // Readable cardinality syntax (alternative to [*])
    orders: composition many of Order;
    contacts: association many to Contact;
}
```

---

### `REF`
**Lexer:** Line 368 — `'ref'`

Reference type qualifier. Indicates a field holds a reference (foreign key) to another entity rather than an inline value.

**Use case:** Explicit FK declaration, distinguishing value types from reference types, OData `$ref` navigation.

```bmmdl
entity OrderItem {
    key ID: UUID;
    // Explicit reference (FK) to Product — different from embedding
    product: ref Product;  // Generates product_id FK column
    quantity: Integer;
    unitPrice: Decimal(18,2);
}

// Contrast with inline:
entity Address {
    street: String(200);  // Inline value, no FK
}
```

---

### `SESSION`
**Lexer:** Line 347 — `'session'`

Session-scoped context. In multi-tenancy, session can carry tenant context, user preferences, and temporary state.

**Use case:** Session-level variables, session-scoped caching, user session context in expressions.

```bmmdl
// Session-scoped sequence (unique per user session)
sequence TempReferenceNumber {
    scope: session;
    start: 1;
    reset on never;
}

// Access session context in expressions
rule ApplyUserPreferences for SalesOrder on before read {
    compute displayCurrency = $session.preferredCurrency;
    compute locale = $session.locale;
}
```

---

### `SHARED`
**Lexer:** Line 350 — `'shared'`

Indicates a resource or entity is shared across tenants. Opposite of tenant-scoped — global/common data accessible by all tenants.

**Use case:** Master data shared across tenants (currencies, countries, units of measure), shared configuration.

```bmmdl
// Shared master data — all tenants can read, only admin can write
shared entity Currency {
    key Code: String(3);   // EUR, USD, JPY
    name: String(50);
    symbol: String(5);
    decimalPlaces: Integer;
}

// Tenant-scoped entity can reference shared entity
@TenantScoped
entity Invoice {
    key ID: UUID;
    amount: Decimal(18,2);
    currency: association [0..1] to shared Currency;
}
```

---

### `RESET_SEQUENCE`
**Lexer:** Line 371 — `'resetSequence'`

Imperative command to reset a sequence counter. Used in actions or migration scripts to programmatically reset sequences.

**Use case:** Manual sequence reset in admin actions, test setup, year-end processing.

```bmmdl
action ResetFiscalYearSequences() {
    resetSequence InvoiceNumber to 1;
    resetSequence PurchaseOrderNumber to 1;
    emit FiscalYearReset(fiscalYear: $context.currentFiscalYear);
}
```

---

### `SET_SEQUENCE`
**Lexer:** Line 372 — `'setSequence'`

Imperative command to set a sequence to a specific value. Used for data migration or manual adjustment.

**Use case:** Data migration (continue numbering from legacy system), correction after import, manual override.

```bmmdl
migration "v2.0 import legacy data" {
    // After importing 50,000 legacy invoices, continue numbering from 50001
    up {
        setSequence InvoiceNumber to 50001;
    }
    down {
        setSequence InvoiceNumber to 1;
    }
}
```

---

### `LOG`
**Lexer:** Line 206 — `'log'`

Logging statement for debugging and audit trails within action/rule bodies.

**Use case:** Debug logging in action execution, audit trail entries, development diagnostics.

```bmmdl
action RecalculatePricing(orderId: UUID) returns SalesOrder {
    let order = SELECT * FROM SalesOrder WHERE ID = :orderId;
    log 'Recalculating pricing for order: ' + order.OrderNumber;

    foreach item in order.Items {
        let oldPrice = item.unitPrice;
        compute item.unitPrice = call GetCurrentPrice(item.productId);
        log 'Item ' + item.ProductId + ': ' + oldPrice + ' -> ' + item.unitPrice;
    }

    return order;
}
```

---

### `ABORT`
**Lexer:** Line 204 — `'abort'`

Aborts the current transaction or operation. Stronger than `raise error` — implies immediate rollback without further rule processing.

**Use case:** Critical validation failures, data integrity emergencies, migration rollback.

```bmmdl
rule PreventNegativeInventory for StockMovement on before create {
    let currentStock = SELECT Quantity FROM Inventory
        WHERE ProductId = $context.entity.ProductId;

    when currentStock + $context.entity.Quantity < 0 {
        abort 'CRITICAL: Stock would go negative for product '
            + $context.entity.ProductId
            + '. Current: ' + currentStock
            + ', Requested: ' + $context.entity.Quantity;
    }
}
```

---

### `BOM`
**Lexer:** Line 23 — `'\uFEFF'` (skip)

Byte Order Mark — a Unicode character (U+FEFF) that appears at the beginning of UTF-8 files saved by some editors (notably Windows Notepad). The lexer already skips it.

**Use case:** Not a language feature — this is a lexer-level encoding artifact handler. It ensures BMMDL files with BOM are parsed correctly.

```
// This token is CORRECTLY dead in the parser.
// It exists solely in the lexer to skip the BOM character.
// No parser rule should ever reference it.
// Action: Keep in lexer, remove from "dead token" lists.
```

---

## Summary — Implementation Priority

| Priority | Tokens | Count | Value |
|----------|--------|-------|-------|
| **HIGH** | Window functions: `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`, `LAG`, `LEAD`, `FIRST_VALUE`, `LAST_VALUE`, `OVER`, `PARTITION`, `ROWS`, `RANGE`, `ROW`, `UNBOUNDED`, `PRECEDING`, `FOLLOWING` | 16 | Analytics, reporting, rankings, time-series |
| **MEDIUM** | Temporal: `HISTORY`, `TRANSACTION`, `VALID`, `PERIOD` | 4 | Bitemporal query richness |
| **MEDIUM** | Control flow: `IF`, `ABORT`, `LOG` | 3 | Action body expressiveness |
| **MEDIUM** | Type system: `SEALED`, `REF`, `SHARED` | 3 | Model safety + multi-tenancy |
| **LOW** | Readable syntax: `ONE`, `MANY`, `JOINED`, `TABLE` | 4 | Readability (alternatives exist) |
| **LOW** | Sequence ops: `RESET_SEQUENCE`, `SET_SEQUENCE`, `FISCAL_YEAR`, `FISCAL_PERIOD` | 4 | Niche admin/financial use cases |
| **LOW** | Operators: `ARROW`, `DOUBLE_ARROW`, `DOUBLE_COLON`, `PIPE`, `AMPERSAND`, `CARET`, `TILDE`, `EXCLAIM` | 8 | Syntactic sugar (workarounds exist) |
| **SKIP** | `BOM` | 1 | Encoding artifact — keep in lexer, not a real token |
| **LOW** | `SESSION` | 1 | Session scope (annotation covers it) |

**True dead count: 43** (excluding `BOM` which is correctly a lexer-only skip rule)
