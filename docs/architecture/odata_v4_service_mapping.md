# OData v4 Service Mapping Design

## Overview

This document defines how BMMDL `service` keyword maps to OData v4 standards, ensuring full compatibility with OData clients while maintaining BMMDL's expressive power.

**Design Principle**: OData v4 is the center of the runtime API. The `service` keyword defines an OData EntityContainer, providing a standards-compliant API surface.

## OData v4 Concepts Mapping

| BMMDL Concept | OData v4 Term | Description |
|---------------|---------------|-------------|
| `service OrderService` | EntityContainer | Named container for EntitySets and operations |
| `entity Orders as Order` | EntitySet | Collection of entities exposed via the service |
| `entity Order { action confirm() }` | Bound Action | Action invoked on a specific entity instance |
| `entity Order { function getCost() }` | Bound Function | Function invoked on a specific entity instance |
| `service { action bulkProcess() }` | ActionImport (Unbound) | Action not bound to any entity |
| `service { function getStats() }` | FunctionImport (Unbound) | Function not bound to any entity |

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         OData v4 API Layer                              │
├─────────────────────────────────────────────────────────────────────────┤
│  GET  /odata/{service}/$metadata          → EDMX/CSDL Document          │
│  GET  /odata/{service}/                   → Service Document            │
├─────────────────────────────────────────────────────────────────────────┤
│                         EntitySet Operations                            │
├─────────────────────────────────────────────────────────────────────────┤
│  GET    /odata/{service}/{entitySet}                    → Query         │
│  GET    /odata/{service}/{entitySet}({key})             → Read          │
│  POST   /odata/{service}/{entitySet}                    → Create        │
│  PATCH  /odata/{service}/{entitySet}({key})             → Update        │
│  DELETE /odata/{service}/{entitySet}({key})             → Delete        │
│  POST   /odata/{service}/$batch                         → Batch         │
├─────────────────────────────────────────────────────────────────────────┤
│                         Bound Operations (on Entity)                    │
├─────────────────────────────────────────────────────────────────────────┤
│  POST /odata/{service}/{entitySet}({key})/{action}      → Bound Action  │
│  GET  /odata/{service}/{entitySet}({key})/{function}()  → Bound Function│
├─────────────────────────────────────────────────────────────────────────┤
│                         Unbound Operations (on Service)                 │
├─────────────────────────────────────────────────────────────────────────┤
│  POST /odata/{service}/{actionImport}                   → Unbound Action│
│  GET  /odata/{service}/{functionImport}()               → Unbound Func  │
└─────────────────────────────────────────────────────────────────────────┘
```

## BMMDL Syntax Specification

### Entity with Bound Operations

```bmmdl
entity Order {
    key id: UUID;
    orderNumber: String(20);
    status: OrderStatus;
    totalAmount: Decimal(15,2);
    customerId: UUID;

    // Associations
    customer: association to Customer on customer.id = customerId;
    items: composition [*] of OrderItem;

    // ═══════════════════════════════════════════════════════════════
    // BOUND ACTIONS - State-changing operations on entity instance
    // Route: POST /odata/{service}/{entitySet}({id})/{actionName}
    // ═══════════════════════════════════════════════════════════════

    action confirm() returns Order {
        validate status = #Draft
            message 'Only draft orders can be confirmed'
            severity error;

        compute status = #Confirmed;
        compute confirmedAt = $now;

        emit OrderConfirmed;
    }

    action cancel(reason: String) returns Order {
        validate status in (#Draft, #Confirmed)
            message 'Cannot cancel order in current status'
            severity error;

        compute status = #Cancelled;
        compute cancelReason = reason;
        compute cancelledAt = $now;

        emit OrderCancelled;
    }

    action ship(trackingNumber: String, carrier: String) returns Order {
        validate status = #Confirmed
            message 'Only confirmed orders can be shipped'
            severity error;

        compute status = #Shipped;
        compute trackingNumber = trackingNumber;
        compute carrier = carrier;
        compute shippedAt = $now;

        emit OrderShipped;
    }

    // ═══════════════════════════════════════════════════════════════
    // BOUND FUNCTIONS - Read-only operations on entity instance
    // Route: GET /odata/{service}/{entitySet}({id})/{functionName}()
    // ═══════════════════════════════════════════════════════════════

    function getShippingCost(destinationZip: String) returns Decimal {
        // Function body - read-only computation
        return calculateShipping(totalAmount, items.count(), destinationZip);
    }

    function getEstimatedDelivery() returns DateTime {
        return addDays($now,
            case carrier
                when 'Express' then 2
                when 'Standard' then 5
                else 7
            end
        );
    }

    function canCancel() returns Boolean {
        return status in (#Draft, #Confirmed);
    }
}
```

### Service Definition (EntityContainer)

```bmmdl
service OrderService {
    // ═══════════════════════════════════════════════════════════════
    // ENTITY SETS - Expose entities with optional projection
    // Route: /odata/OrderService/{entitySetName}
    // ═══════════════════════════════════════════════════════════════

    // Full entity exposure
    entity Orders as Order;

    // Projection - expose subset of fields (security/performance)
    entity OrderSummaries as projection on Order {
        id,
        orderNumber,
        status,
        totalAmount,
        customer.name as customerName  // flattened navigation
    };

    // Read-only view exposure
    entity CustomerOrders as projection on Order {
        *,
        excluding { internalNotes, costPrice }
    } where customerId = $user.customerId;  // Row-level security

    entity Customers as Customer;
    entity Products as Product;

    // ═══════════════════════════════════════════════════════════════
    // UNBOUND ACTIONS (ActionImport) - Not tied to specific entity
    // Route: POST /odata/OrderService/{actionName}
    // ═══════════════════════════════════════════════════════════════

    action bulkConfirmOrders(orderIds: [UUID]) returns [Order] {
        // Iterate and confirm each order
        foreach orderId in orderIds {
            call Orders(orderId).confirm();
        }
        return Orders where id in orderIds;
    }

    action processEndOfDay() returns ProcessingResult {
        // Complex business logic
        compute pendingCount = count(Orders where status = #Pending);

        foreach order in Orders where status = #Pending and createdAt < addDays($now, -7) {
            call order.cancel('Auto-cancelled: expired');
        }

        return new ProcessingResult {
            processedAt = $now,
            cancelledCount = count(Orders where status = #Cancelled and cancelledAt = $today)
        };
    }

    action importOrders(file: Binary, format: String) returns ImportResult {
        // File processing action
        validate format in ('CSV', 'JSON', 'XML')
            message 'Unsupported format'
            severity error;

        // Implementation delegated to handler
        call OrderImportHandler.process(file, format);
    }

    // ═══════════════════════════════════════════════════════════════
    // UNBOUND FUNCTIONS (FunctionImport) - Read-only, not tied to entity
    // Route: GET /odata/OrderService/{functionName}()
    // ═══════════════════════════════════════════════════════════════

    function getOrderStatistics(
        fromDate: Date,
        toDate: Date
    ) returns OrderStatistics {
        return new OrderStatistics {
            totalOrders = count(Orders where createdAt between fromDate and toDate),
            totalRevenue = sum(Orders.totalAmount where status = #Completed),
            averageOrderValue = avg(Orders.totalAmount),
            topProducts = Products orderby salesCount desc limit 10
        };
    }

    function healthCheck() returns HealthStatus {
        return new HealthStatus {
            status = 'healthy',
            timestamp = $now,
            version = $app.version
        };
    }

    function searchOrders(
        query: String,
        status: OrderStatus?,
        fromDate: Date?,
        toDate: Date?
    ) returns [Order] {
        return Orders
            where (orderNumber like '%' + query + '%' or customer.name like '%' + query + '%')
            and (status is null or Orders.status = status)
            and (fromDate is null or createdAt >= fromDate)
            and (toDate is null or createdAt <= toDate);
    }
}
```

## Generated Route Table

Given the above definitions, the following OData v4 compliant routes are generated:

### Service Metadata
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/odata/OrderService/$metadata` | EDMX/CSDL schema document |
| GET | `/odata/OrderService/` | OData service document |

### EntitySet CRUD (Orders)
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/odata/OrderService/Orders` | Query orders with $filter, $select, $expand, etc. |
| GET | `/odata/OrderService/Orders({id})` | Get single order by key |
| POST | `/odata/OrderService/Orders` | Create new order |
| PATCH | `/odata/OrderService/Orders({id})` | Update order (partial) |
| PUT | `/odata/OrderService/Orders({id})` | Replace order (full) |
| DELETE | `/odata/OrderService/Orders({id})` | Delete order |
| POST | `/odata/OrderService/$batch` | Batch operations |

### Bound Actions (on Order instance)
| Method | Route | Description |
|--------|-------|-------------|
| POST | `/odata/OrderService/Orders({id})/confirm` | Confirm specific order |
| POST | `/odata/OrderService/Orders({id})/cancel` | Cancel with reason |
| POST | `/odata/OrderService/Orders({id})/ship` | Ship with tracking |

### Bound Functions (on Order instance)
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/odata/OrderService/Orders({id})/getShippingCost(destinationZip='12345')` | Calculate shipping |
| GET | `/odata/OrderService/Orders({id})/getEstimatedDelivery()` | Get delivery date |
| GET | `/odata/OrderService/Orders({id})/canCancel()` | Check if cancellable |

### Unbound Actions (ActionImport)
| Method | Route | Request Body |
|--------|-------|--------------|
| POST | `/odata/OrderService/bulkConfirmOrders` | `{"orderIds": ["...", "..."]}` |
| POST | `/odata/OrderService/processEndOfDay` | `{}` |
| POST | `/odata/OrderService/importOrders` | `{"file": "...", "format": "CSV"}` |

### Unbound Functions (FunctionImport)
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/odata/OrderService/getOrderStatistics(fromDate=2024-01-01,toDate=2024-12-31)` | Get statistics |
| GET | `/odata/OrderService/healthCheck()` | Health check |
| GET | `/odata/OrderService/searchOrders(query='laptop',status=null,...)` | Search orders |

## Code Generation Scope

### 1. $metadata (EDMX/CSDL) Generation

The compiler generates OData v4 compliant metadata document:

```xml
<?xml version="1.0" encoding="utf-8"?>
<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
  <edmx:DataServices>
    <Schema Namespace="OrderModule" xmlns="http://docs.oasis-open.org/odata/ns/edm">

      <!-- EntityType with Bound Actions/Functions -->
      <EntityType Name="Order">
        <Key>
          <PropertyRef Name="id"/>
        </Key>
        <Property Name="id" Type="Edm.Guid" Nullable="false"/>
        <Property Name="orderNumber" Type="Edm.String" MaxLength="20"/>
        <Property Name="status" Type="OrderModule.OrderStatus"/>
        <Property Name="totalAmount" Type="Edm.Decimal" Precision="15" Scale="2"/>
        <NavigationProperty Name="customer" Type="OrderModule.Customer"/>
        <NavigationProperty Name="items" Type="Collection(OrderModule.OrderItem)" ContainsTarget="true"/>
      </EntityType>

      <!-- Bound Action -->
      <Action Name="confirm" IsBound="true">
        <Parameter Name="bindingParameter" Type="OrderModule.Order"/>
        <ReturnType Type="OrderModule.Order"/>
      </Action>

      <Action Name="cancel" IsBound="true">
        <Parameter Name="bindingParameter" Type="OrderModule.Order"/>
        <Parameter Name="reason" Type="Edm.String" Nullable="false"/>
        <ReturnType Type="OrderModule.Order"/>
      </Action>

      <!-- Bound Function -->
      <Function Name="getShippingCost" IsBound="true">
        <Parameter Name="bindingParameter" Type="OrderModule.Order"/>
        <Parameter Name="destinationZip" Type="Edm.String" Nullable="false"/>
        <ReturnType Type="Edm.Decimal"/>
      </Function>

      <!-- Unbound Action (in EntityContainer) -->
      <Action Name="bulkConfirmOrders">
        <Parameter Name="orderIds" Type="Collection(Edm.Guid)" Nullable="false"/>
        <ReturnType Type="Collection(OrderModule.Order)"/>
      </Action>

      <!-- Unbound Function -->
      <Function Name="getOrderStatistics">
        <Parameter Name="fromDate" Type="Edm.Date" Nullable="false"/>
        <Parameter Name="toDate" Type="Edm.Date" Nullable="false"/>
        <ReturnType Type="OrderModule.OrderStatistics"/>
      </Function>

      <!-- EntityContainer = Service -->
      <EntityContainer Name="OrderService">
        <EntitySet Name="Orders" EntityType="OrderModule.Order">
          <NavigationPropertyBinding Path="customer" Target="Customers"/>
        </EntitySet>
        <EntitySet Name="Customers" EntityType="OrderModule.Customer"/>

        <!-- ActionImport for unbound actions -->
        <ActionImport Name="bulkConfirmOrders" Action="OrderModule.bulkConfirmOrders"/>
        <ActionImport Name="processEndOfDay" Action="OrderModule.processEndOfDay"/>

        <!-- FunctionImport for unbound functions -->
        <FunctionImport Name="getOrderStatistics" Function="OrderModule.getOrderStatistics"/>
        <FunctionImport Name="healthCheck" Function="OrderModule.healthCheck"/>
      </EntityContainer>

    </Schema>
  </edmx:DataServices>
</edmx:Edmx>
```

### 2. PostgreSQL Code Generation

#### Stored Procedures for Actions

```sql
-- ═══════════════════════════════════════════════════════════════════════
-- Bound Action: Order.confirm
-- Generated from BMMDL action definition
-- ═══════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION platform.order_confirm(
    p_tenant_id UUID,
    p_order_id UUID,
    p_user_id UUID
) RETURNS JSONB AS $$
DECLARE
    v_order RECORD;
    v_result JSONB;
BEGIN
    -- Fetch current entity state
    SELECT * INTO v_order
    FROM platform.orders
    WHERE tenant_id = p_tenant_id AND id = p_order_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Order not found: %', p_order_id;
    END IF;

    -- VALIDATE: status = #Draft
    IF v_order.status != 'Draft' THEN
        RAISE EXCEPTION 'Only draft orders can be confirmed';
    END IF;

    -- COMPUTE: status = #Confirmed, confirmedAt = $now
    UPDATE platform.orders
    SET status = 'Confirmed',
        confirmed_at = NOW(),
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE tenant_id = p_tenant_id AND id = p_order_id
    RETURNING to_jsonb(orders.*) INTO v_result;

    -- EMIT: OrderConfirmed event
    INSERT INTO platform.domain_events (
        tenant_id, event_type, aggregate_type, aggregate_id, payload, created_at
    ) VALUES (
        p_tenant_id, 'OrderConfirmed', 'Order', p_order_id, v_result, NOW()
    );

    RETURN v_result;
END;
$$ LANGUAGE plpgsql;

-- ═══════════════════════════════════════════════════════════════════════
-- Bound Action: Order.cancel
-- ═══════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION platform.order_cancel(
    p_tenant_id UUID,
    p_order_id UUID,
    p_reason TEXT,
    p_user_id UUID
) RETURNS JSONB AS $$
DECLARE
    v_order RECORD;
    v_result JSONB;
BEGIN
    SELECT * INTO v_order
    FROM platform.orders
    WHERE tenant_id = p_tenant_id AND id = p_order_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Order not found: %', p_order_id;
    END IF;

    -- VALIDATE: status in (#Draft, #Confirmed)
    IF v_order.status NOT IN ('Draft', 'Confirmed') THEN
        RAISE EXCEPTION 'Cannot cancel order in current status';
    END IF;

    -- COMPUTE statements
    UPDATE platform.orders
    SET status = 'Cancelled',
        cancel_reason = p_reason,
        cancelled_at = NOW(),
        updated_at = NOW(),
        updated_by = p_user_id
    WHERE tenant_id = p_tenant_id AND id = p_order_id
    RETURNING to_jsonb(orders.*) INTO v_result;

    -- EMIT: OrderCancelled event
    INSERT INTO platform.domain_events (
        tenant_id, event_type, aggregate_type, aggregate_id, payload, created_at
    ) VALUES (
        p_tenant_id, 'OrderCancelled', 'Order', p_order_id,
        jsonb_build_object('order', v_result, 'reason', p_reason), NOW()
    );

    RETURN v_result;
END;
$$ LANGUAGE plpgsql;

-- ═══════════════════════════════════════════════════════════════════════
-- Bound Function: Order.getShippingCost (read-only)
-- ═══════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION platform.order_get_shipping_cost(
    p_tenant_id UUID,
    p_order_id UUID,
    p_destination_zip TEXT
) RETURNS DECIMAL AS $$
DECLARE
    v_order RECORD;
    v_item_count INTEGER;
    v_base_cost DECIMAL;
BEGIN
    SELECT o.*, COUNT(oi.id) as item_count
    INTO v_order
    FROM platform.orders o
    LEFT JOIN platform.order_items oi ON oi.order_id = o.id
    WHERE o.tenant_id = p_tenant_id AND o.id = p_order_id
    GROUP BY o.id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Order not found: %', p_order_id;
    END IF;

    -- Calculate shipping based on order value and items
    v_base_cost := CASE
        WHEN v_order.total_amount > 100 THEN 0  -- Free shipping over $100
        WHEN v_order.item_count <= 2 THEN 5.99
        WHEN v_order.item_count <= 5 THEN 9.99
        ELSE 14.99
    END;

    -- Zone adjustment (simplified)
    RETURN v_base_cost * CASE
        WHEN LEFT(p_destination_zip, 1) IN ('0', '1', '2') THEN 1.0
        WHEN LEFT(p_destination_zip, 1) IN ('3', '4', '5') THEN 1.2
        ELSE 1.5
    END;
END;
$$ LANGUAGE plpgsql STABLE;  -- STABLE = read-only, can be optimized

-- ═══════════════════════════════════════════════════════════════════════
-- Unbound Action: bulkConfirmOrders
-- ═══════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION platform.bulk_confirm_orders(
    p_tenant_id UUID,
    p_order_ids UUID[],
    p_user_id UUID
) RETURNS JSONB AS $$
DECLARE
    v_order_id UUID;
    v_results JSONB := '[]'::JSONB;
    v_result JSONB;
BEGIN
    FOREACH v_order_id IN ARRAY p_order_ids LOOP
        -- Call bound action for each order
        v_result := platform.order_confirm(p_tenant_id, v_order_id, p_user_id);
        v_results := v_results || v_result;
    END LOOP;

    RETURN v_results;
END;
$$ LANGUAGE plpgsql;

-- ═══════════════════════════════════════════════════════════════════════
-- Unbound Function: getOrderStatistics
-- ═══════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION platform.get_order_statistics(
    p_tenant_id UUID,
    p_from_date DATE,
    p_to_date DATE
) RETURNS JSONB AS $$
BEGIN
    RETURN jsonb_build_object(
        'totalOrders', (
            SELECT COUNT(*) FROM platform.orders
            WHERE tenant_id = p_tenant_id
            AND created_at BETWEEN p_from_date AND p_to_date
        ),
        'totalRevenue', (
            SELECT COALESCE(SUM(total_amount), 0) FROM platform.orders
            WHERE tenant_id = p_tenant_id
            AND status = 'Completed'
            AND created_at BETWEEN p_from_date AND p_to_date
        ),
        'averageOrderValue', (
            SELECT COALESCE(AVG(total_amount), 0) FROM platform.orders
            WHERE tenant_id = p_tenant_id
            AND created_at BETWEEN p_from_date AND p_to_date
        ),
        'byStatus', (
            SELECT jsonb_object_agg(status, cnt)
            FROM (
                SELECT status, COUNT(*) as cnt
                FROM platform.orders
                WHERE tenant_id = p_tenant_id
                AND created_at BETWEEN p_from_date AND p_to_date
                GROUP BY status
            ) s
        )
    );
END;
$$ LANGUAGE plpgsql STABLE;
```

### 2b. C# Code Generation (Alternative Strategy)

Instead of (or in addition to) stored procedures, generate C# classes that can be compiled and loaded into the .NET runtime. This provides:

- **Debuggability**: Step through action logic in IDE
- **Flexibility**: Call external services, use dependency injection
- **Testability**: Unit test actions in isolation
- **Hot Reload**: Update logic without database migration

#### Generated Project Structure

```
Generated/
├── OrderModule/
│   ├── Entities/
│   │   ├── Order.cs                    # Entity class
│   │   ├── OrderItem.cs
│   │   └── Customer.cs
│   ├── Actions/
│   │   ├── OrderActions.cs             # Bound actions for Order
│   │   ├── OrderServiceActions.cs      # Unbound actions
│   │   └── IOrderActions.cs            # Interface for DI
│   ├── Functions/
│   │   ├── OrderFunctions.cs           # Bound functions
│   │   ├── OrderServiceFunctions.cs    # Unbound functions
│   │   └── IOrderFunctions.cs
│   ├── Events/
│   │   ├── OrderConfirmed.cs           # Domain event classes
│   │   ├── OrderCancelled.cs
│   │   └── OrderShipped.cs
│   ├── Validators/
│   │   └── OrderValidators.cs          # Validation logic
│   └── OrderModuleRegistration.cs      # DI registration
```

#### Generated Entity Class

```csharp
// Generated/OrderModule/Entities/Order.cs
namespace Generated.OrderModule.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("orders", Schema = "platform")]
public class Order
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("tenant_id")]
    public Guid TenantId { get; set; }

    [Column("order_number")]
    [MaxLength(20)]
    public string OrderNumber { get; set; } = string.Empty;

    [Column("status")]
    public OrderStatus Status { get; set; }

    [Column("total_amount")]
    public decimal TotalAmount { get; set; }

    [Column("customer_id")]
    public Guid CustomerId { get; set; }

    [Column("confirmed_at")]
    public DateTime? ConfirmedAt { get; set; }

    [Column("cancelled_at")]
    public DateTime? CancelledAt { get; set; }

    [Column("cancel_reason")]
    public string? CancelReason { get; set; }

    // Navigation properties
    [ForeignKey(nameof(CustomerId))]
    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public enum OrderStatus
{
    Draft = 1,
    Confirmed = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5
}
```

#### Generated Bound Action Class

```csharp
// Generated/OrderModule/Actions/OrderActions.cs
namespace Generated.OrderModule.Actions;

using Generated.OrderModule.Entities;
using Generated.OrderModule.Events;
using BMMDL.Runtime.Abstractions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Bound actions for Order entity.
/// Generated from BMMDL action definitions.
/// </summary>
public class OrderActions : IOrderActions
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrderActions> _logger;

    public OrderActions(
        IDbContextFactory<PlatformDbContext> dbFactory,
        IEventPublisher eventPublisher,
        ICurrentUser currentUser,
        ILogger<OrderActions> logger)
    {
        _dbFactory = dbFactory;
        _eventPublisher = eventPublisher;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Confirm order - transitions from Draft to Confirmed.
    /// Route: POST /odata/{service}/Orders({id})/confirm
    /// </summary>
    /// <remarks>
    /// Generated from BMMDL:
    /// <code>
    /// action confirm() returns Order {
    ///     validate status = #Draft message 'Only draft orders can be confirmed';
    ///     compute status = #Confirmed;
    ///     compute confirmedAt = $now;
    ///     emit OrderConfirmed;
    /// }
    /// </code>
    /// </remarks>
    public async Task<ActionResult<Order>> ConfirmAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // Load entity with row lock
            var order = await db.Orders
                .Where(o => o.TenantId == tenantId && o.Id == orderId)
                .FirstOrDefaultAsync(ct);

            if (order == null)
            {
                return ActionResult<Order>.NotFound($"Order not found: {orderId}");
            }

            // ═══════════════════════════════════════════════════════════
            // VALIDATE: status = #Draft
            // ═══════════════════════════════════════════════════════════
            if (order.Status != OrderStatus.Draft)
            {
                return ActionResult<Order>.ValidationError(
                    "Only draft orders can be confirmed");
            }

            // ═══════════════════════════════════════════════════════════
            // COMPUTE: status = #Confirmed, confirmedAt = $now
            // ═══════════════════════════════════════════════════════════
            order.Status = OrderStatus.Confirmed;
            order.ConfirmedAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;
            order.UpdatedBy = _currentUser.UserId;

            await db.SaveChangesAsync(ct);

            // ═══════════════════════════════════════════════════════════
            // EMIT: OrderConfirmed
            // ═══════════════════════════════════════════════════════════
            await _eventPublisher.PublishAsync(new OrderConfirmed
            {
                TenantId = tenantId,
                OrderId = orderId,
                OrderNumber = order.OrderNumber,
                ConfirmedAt = order.ConfirmedAt.Value,
                ConfirmedBy = _currentUser.UserId
            }, ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation("Order {OrderId} confirmed", orderId);
            return ActionResult<Order>.Success(order);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to confirm order {OrderId}", orderId);
            throw;
        }
    }

    /// <summary>
    /// Cancel order with reason.
    /// Route: POST /odata/{service}/Orders({id})/cancel
    /// </summary>
    public async Task<ActionResult<Order>> CancelAsync(
        Guid tenantId,
        Guid orderId,
        string reason,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var order = await db.Orders
                .Where(o => o.TenantId == tenantId && o.Id == orderId)
                .FirstOrDefaultAsync(ct);

            if (order == null)
            {
                return ActionResult<Order>.NotFound($"Order not found: {orderId}");
            }

            // VALIDATE: status in (#Draft, #Confirmed)
            if (order.Status != OrderStatus.Draft && order.Status != OrderStatus.Confirmed)
            {
                return ActionResult<Order>.ValidationError(
                    "Cannot cancel order in current status");
            }

            // COMPUTE statements
            order.Status = OrderStatus.Cancelled;
            order.CancelReason = reason;
            order.CancelledAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;
            order.UpdatedBy = _currentUser.UserId;

            await db.SaveChangesAsync(ct);

            // EMIT: OrderCancelled
            await _eventPublisher.PublishAsync(new OrderCancelled
            {
                TenantId = tenantId,
                OrderId = orderId,
                Reason = reason,
                CancelledAt = order.CancelledAt.Value,
                CancelledBy = _currentUser.UserId
            }, ct);

            await tx.CommitAsync(ct);
            return ActionResult<Order>.Success(order);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Ship order with tracking information.
    /// Route: POST /odata/{service}/Orders({id})/ship
    /// </summary>
    public async Task<ActionResult<Order>> ShipAsync(
        Guid tenantId,
        Guid orderId,
        string trackingNumber,
        string carrier,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var order = await db.Orders
                .Where(o => o.TenantId == tenantId && o.Id == orderId)
                .FirstOrDefaultAsync(ct);

            if (order == null)
            {
                return ActionResult<Order>.NotFound($"Order not found: {orderId}");
            }

            // VALIDATE: status = #Confirmed
            if (order.Status != OrderStatus.Confirmed)
            {
                return ActionResult<Order>.ValidationError(
                    "Only confirmed orders can be shipped");
            }

            // COMPUTE statements
            order.Status = OrderStatus.Shipped;
            order.TrackingNumber = trackingNumber;
            order.Carrier = carrier;
            order.ShippedAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;
            order.UpdatedBy = _currentUser.UserId;

            await db.SaveChangesAsync(ct);

            // EMIT: OrderShipped
            await _eventPublisher.PublishAsync(new OrderShipped
            {
                TenantId = tenantId,
                OrderId = orderId,
                TrackingNumber = trackingNumber,
                Carrier = carrier,
                ShippedAt = order.ShippedAt.Value
            }, ct);

            await tx.CommitAsync(ct);
            return ActionResult<Order>.Success(order);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
```

#### Generated Bound Function Class

```csharp
// Generated/OrderModule/Functions/OrderFunctions.cs
namespace Generated.OrderModule.Functions;

using Generated.OrderModule.Entities;
using BMMDL.Runtime.Abstractions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Bound functions for Order entity (read-only).
/// Generated from BMMDL function definitions.
/// </summary>
public class OrderFunctions : IOrderFunctions
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;

    public OrderFunctions(IDbContextFactory<PlatformDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Calculate shipping cost for order.
    /// Route: GET /odata/{service}/Orders({id})/getShippingCost(destinationZip='...')
    /// </summary>
    public async Task<decimal> GetShippingCostAsync(
        Guid tenantId,
        Guid orderId,
        string destinationZip,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var order = await db.Orders
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && o.Id == orderId)
            .FirstOrDefaultAsync(ct);

        if (order == null)
        {
            throw new EntityNotFoundException($"Order not found: {orderId}");
        }

        // Calculate base cost
        decimal baseCost = order.TotalAmount switch
        {
            > 100 => 0m,  // Free shipping over $100
            _ when order.Items.Count <= 2 => 5.99m,
            _ when order.Items.Count <= 5 => 9.99m,
            _ => 14.99m
        };

        // Zone multiplier based on destination
        decimal zoneMultiplier = destinationZip[0] switch
        {
            '0' or '1' or '2' => 1.0m,
            '3' or '4' or '5' => 1.2m,
            _ => 1.5m
        };

        return baseCost * zoneMultiplier;
    }

    /// <summary>
    /// Get estimated delivery date.
    /// Route: GET /odata/{service}/Orders({id})/getEstimatedDelivery()
    /// </summary>
    public async Task<DateTime> GetEstimatedDeliveryAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var order = await db.Orders
            .Where(o => o.TenantId == tenantId && o.Id == orderId)
            .Select(o => new { o.Carrier })
            .FirstOrDefaultAsync(ct);

        if (order == null)
        {
            throw new EntityNotFoundException($"Order not found: {orderId}");
        }

        int daysToAdd = order.Carrier switch
        {
            "Express" => 2,
            "Standard" => 5,
            _ => 7
        };

        return DateTime.UtcNow.AddDays(daysToAdd);
    }

    /// <summary>
    /// Check if order can be cancelled.
    /// Route: GET /odata/{service}/Orders({id})/canCancel()
    /// </summary>
    public async Task<bool> CanCancelAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var status = await db.Orders
            .Where(o => o.TenantId == tenantId && o.Id == orderId)
            .Select(o => o.Status)
            .FirstOrDefaultAsync(ct);

        return status == OrderStatus.Draft || status == OrderStatus.Confirmed;
    }
}
```

#### Generated Unbound Action Class (Service-level)

```csharp
// Generated/OrderModule/Actions/OrderServiceActions.cs
namespace Generated.OrderModule.Actions;

using Generated.OrderModule.Entities;
using BMMDL.Runtime.Abstractions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Unbound actions for OrderService (not tied to specific entity).
/// Generated from BMMDL service action definitions.
/// </summary>
public class OrderServiceActions : IOrderServiceActions
{
    private readonly IDbContextFactory<PlatformDbContext> _dbFactory;
    private readonly IOrderActions _orderActions;  // Inject bound actions
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OrderServiceActions> _logger;

    public OrderServiceActions(
        IDbContextFactory<PlatformDbContext> dbFactory,
        IOrderActions orderActions,
        IEventPublisher eventPublisher,
        ILogger<OrderServiceActions> logger)
    {
        _dbFactory = dbFactory;
        _orderActions = orderActions;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Bulk confirm multiple orders.
    /// Route: POST /odata/OrderService/bulkConfirmOrders
    /// </summary>
    public async Task<ActionResult<List<Order>>> BulkConfirmOrdersAsync(
        Guid tenantId,
        List<Guid> orderIds,
        CancellationToken ct = default)
    {
        var results = new List<Order>();
        var errors = new List<string>();

        foreach (var orderId in orderIds)
        {
            var result = await _orderActions.ConfirmAsync(tenantId, orderId, ct);

            if (result.IsSuccess)
            {
                results.Add(result.Value!);
            }
            else
            {
                errors.Add($"Order {orderId}: {result.Error}");
            }
        }

        if (errors.Any())
        {
            _logger.LogWarning("Bulk confirm completed with {ErrorCount} errors", errors.Count);
        }

        return ActionResult<List<Order>>.Success(results);
    }

    /// <summary>
    /// Process end of day operations.
    /// Route: POST /odata/OrderService/processEndOfDay
    /// </summary>
    public async Task<ActionResult<ProcessingResult>> ProcessEndOfDayAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var cutoffDate = DateTime.UtcNow.AddDays(-7);

        // Find expired pending orders
        var expiredOrders = await db.Orders
            .Where(o => o.TenantId == tenantId)
            .Where(o => o.Status == OrderStatus.Draft)
            .Where(o => o.CreatedAt < cutoffDate)
            .ToListAsync(ct);

        int cancelledCount = 0;
        foreach (var order in expiredOrders)
        {
            var result = await _orderActions.CancelAsync(
                tenantId, order.Id, "Auto-cancelled: expired", ct);

            if (result.IsSuccess)
            {
                cancelledCount++;
            }
        }

        var result = new ProcessingResult
        {
            ProcessedAt = DateTime.UtcNow,
            CancelledCount = cancelledCount,
            TotalExpired = expiredOrders.Count
        };

        _logger.LogInformation(
            "End of day processing: {Cancelled}/{Total} orders cancelled",
            cancelledCount, expiredOrders.Count);

        return ActionResult<ProcessingResult>.Success(result);
    }

    /// <summary>
    /// Import orders from file.
    /// Route: POST /odata/OrderService/importOrders
    /// </summary>
    public async Task<ActionResult<ImportResult>> ImportOrdersAsync(
        Guid tenantId,
        byte[] file,
        string format,
        CancellationToken ct = default)
    {
        // VALIDATE: format in ('CSV', 'JSON', 'XML')
        if (!new[] { "CSV", "JSON", "XML" }.Contains(format.ToUpper()))
        {
            return ActionResult<ImportResult>.ValidationError("Unsupported format");
        }

        // Delegate to import handler (injected service)
        var handler = format.ToUpper() switch
        {
            "CSV" => new CsvOrderImporter(),
            "JSON" => new JsonOrderImporter(),
            "XML" => new XmlOrderImporter(),
            _ => throw new InvalidOperationException()
        };

        var imported = await handler.ImportAsync(tenantId, file, ct);

        return ActionResult<ImportResult>.Success(new ImportResult
        {
            ImportedCount = imported.Count,
            FailedCount = 0,
            ImportedAt = DateTime.UtcNow
        });
    }
}
```

#### Generated Domain Events

```csharp
// Generated/OrderModule/Events/OrderConfirmed.cs
namespace Generated.OrderModule.Events;

using BMMDL.Runtime.Abstractions;

/// <summary>
/// Domain event emitted when an order is confirmed.
/// Generated from BMMDL: emit OrderConfirmed
/// </summary>
public record OrderConfirmed : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string EventType => nameof(OrderConfirmed);

    // Aggregate info
    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;

    // Event-specific data
    public DateTime ConfirmedAt { get; init; }
    public Guid ConfirmedBy { get; init; }
}

// Generated/OrderModule/Events/OrderCancelled.cs
public record OrderCancelled : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string EventType => nameof(OrderCancelled);

    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime CancelledAt { get; init; }
    public Guid CancelledBy { get; init; }
}

// Generated/OrderModule/Events/OrderShipped.cs
public record OrderShipped : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string EventType => nameof(OrderShipped);

    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public string TrackingNumber { get; init; } = string.Empty;
    public string Carrier { get; init; } = string.Empty;
    public DateTime ShippedAt { get; init; }
}
```

#### Generated DI Registration

```csharp
// Generated/OrderModule/OrderModuleRegistration.cs
namespace Generated.OrderModule;

using Generated.OrderModule.Actions;
using Generated.OrderModule.Functions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Dependency injection registration for OrderModule.
/// Generated from BMMDL service definition.
/// </summary>
public static class OrderModuleRegistration
{
    public static IServiceCollection AddOrderModule(this IServiceCollection services)
    {
        // Register bound actions
        services.AddScoped<IOrderActions, OrderActions>();

        // Register bound functions
        services.AddScoped<IOrderFunctions, OrderFunctions>();

        // Register unbound actions (service-level)
        services.AddScoped<IOrderServiceActions, OrderServiceActions>();

        // Register unbound functions (service-level)
        services.AddScoped<IOrderServiceFunctions, OrderServiceFunctions>();

        return services;
    }
}
```

#### Runtime Integration

```csharp
// In ODataController - dispatch to generated actions
[HttpPost("{entitySet}({id})/{actionName}")]
public async Task<IActionResult> InvokeBoundAction(
    string service, string entitySet, Guid id, string actionName,
    [FromBody] JsonElement? parameters)
{
    var tenantId = HttpContext.GetTenantId();

    // Resolve action handler from DI
    var actionType = _actionRegistry.GetBoundActionType(service, entitySet, actionName);
    var handler = HttpContext.RequestServices.GetRequiredService(actionType);

    // Build parameters
    var method = actionType.GetMethod(actionName + "Async");
    var args = BuildMethodArguments(method, tenantId, id, parameters);

    // Invoke
    var result = await (Task<IActionResult>)method.Invoke(handler, args)!;
    return result;
}
```

#### Comparison: Stored Procedure vs C# Class

| Aspect | Stored Procedure | C# Class |
|--------|------------------|----------|
| **Performance** | ✅ Best (runs in DB) | ⚠️ Good (network round-trips) |
| **Atomicity** | ✅ Single transaction | ✅ Managed transaction |
| **Debugging** | ❌ Hard | ✅ Easy (IDE breakpoints) |
| **External Calls** | ❌ Limited | ✅ Full (HTTP, gRPC, etc.) |
| **Dependency Injection** | ❌ None | ✅ Full support |
| **Unit Testing** | ❌ Hard | ✅ Easy (mock dependencies) |
| **Hot Reload** | ❌ Requires migration | ✅ Recompile & restart |
| **Complex Logic** | ⚠️ Verbose PL/pgSQL | ✅ Clean C# |
| **Type Safety** | ⚠️ Runtime errors | ✅ Compile-time |

#### Recommended Strategy: Hybrid

```csharp
// Configuration per action
public enum ExecutionStrategy
{
    StoredProcedure,  // Simple CRUD, high performance
    CSharpClass,      // Complex logic, external calls
    Interpreted       // Dynamic, no regeneration needed
}

// In BMMDL annotation
entity Order {
    @ExecutionStrategy(#StoredProcedure)  // Fast path
    action confirm() returns Order { ... }

    @ExecutionStrategy(#CSharp)  // Needs external service
    action processPayment() returns PaymentResult {
        call PaymentGateway.charge(totalAmount);
        ...
    }
}
```

### 3. Runtime Execution Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Request Flow                                    │
└─────────────────────────────────────────────────────────────────────────┘

POST /odata/OrderService/Orders(123)/confirm
     │
     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ ODataController.InvokeBoundAction()                                     │
│   - Parse route: service=OrderService, entitySet=Orders, id=123         │
│   - Parse action: confirm                                               │
│   - Validate request body (parameters)                                  │
└─────────────────────────────────────────────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ MetaModelCache.GetAction("Order", "confirm")                            │
│   - Load action definition from registry                                │
│   - Get parameter definitions, return type                              │
│   - Get action body (statements)                                        │
└─────────────────────────────────────────────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ Execution Strategy Selection                                            │
├─────────────────────────────────────────────────────────────────────────┤
│ Option A: Stored Procedure (if generated)                               │
│   SELECT platform.order_confirm($1, $2, $3)                             │
│   - Best performance                                                    │
│   - Atomic transaction                                                  │
│   - Used for: simple actions without external calls                     │
├─────────────────────────────────────────────────────────────────────────┤
│ Option B: RuleEngine.ExecuteActionAsync() (interpreted)                 │
│   - Statement-by-statement execution                                    │
│   - Supports: CALL external services, complex logic                     │
│   - Used for: actions with external dependencies                        │
└─────────────────────────────────────────────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ RuleEngine.ExecuteActionAsync()                                         │
├─────────────────────────────────────────────────────────────────────────┤
│ 1. Load entity data                                                     │
│    SELECT * FROM orders WHERE id = 123                                  │
│                                                                         │
│ 2. Execute VALIDATE statements                                          │
│    - Evaluate: status = #Draft                                          │
│    - If false: throw ValidationException with message                   │
│                                                                         │
│ 3. Execute COMPUTE statements                                           │
│    - Collect field updates: {status: 'Confirmed', confirmedAt: now()}   │
│                                                                         │
│ 4. Execute WHEN statements (conditional logic)                          │
│    - Evaluate conditions, execute nested statements                     │
│                                                                         │
│ 5. Execute CALL statements                                              │
│    - Invoke other actions/functions/external services                   │
│                                                                         │
│ 6. Apply updates                                                        │
│    UPDATE orders SET status = 'Confirmed', ... WHERE id = 123           │
│                                                                         │
│ 7. Execute EMIT statements                                              │
│    - Publish domain events to event store/bus                           │
│                                                                         │
│ 8. Return result                                                        │
└─────────────────────────────────────────────────────────────────────────┘
     │
     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ Response                                                                │
│ {                                                                       │
│   "@odata.context": "/odata/OrderService/$metadata#Orders/$entity",     │
│   "id": "123",                                                          │
│   "status": "Confirmed",                                                │
│   "confirmedAt": "2024-01-15T10:30:00Z",                                │
│   ...                                                                   │
│ }                                                                       │
└─────────────────────────────────────────────────────────────────────────┘
```

## Action/Function Body Language Specification

> **Decisions**: Full control flow, keep `compute` keyword, add `$this`, PostgreSQL first

### Supported Statements in Action Body

| Statement | Syntax | Description | PostgreSQL Mapping |
|-----------|--------|-------------|-------------------|
| `validate` | `validate <expr> message '<msg>' severity <level>` | Pre-condition check, aborts on failure | `IF NOT ... THEN RAISE EXCEPTION` |
| `compute` | `compute <field> = <expr>` | Assign value to entity field | `field := expr;` |
| `when` | `when <expr> then { ... } else { ... }` | Conditional execution | `IF ... THEN ... ELSE ... END IF;` |
| `call` | `call <action/function>(args)` | Invoke another operation | `PERFORM func(...);` |
| `emit` | `emit <EventName> with { ... }` | Publish domain event | `INSERT INTO events...` |
| `foreach` | `foreach <var> in <collection> { ... }` | Iterate collection | `FOR ... IN ... LOOP ... END LOOP;` |
| `return` | `return <expr>` | Return value | `RETURN expr;` |
| `let` | `let <var> = <expr>` | Declare local variable | `DECLARE var := expr;` |
| `raise` | `raise '<message>' severity <level>` | Throw exception | `RAISE EXCEPTION '<msg>';` |

### Supported Statements in Function Body

| Statement | Syntax | Description |
|-----------|--------|-------------|
| `return` | `return <expr>` | Return computed value |
| `when` | `when <expr> then { ... } else { ... }` | Conditional return |
| `let` | `let <var> = <expr>` | Declare local variable |

**Key Difference**: Functions are **read-only** (marked `STABLE` in PostgreSQL), cannot use `compute`, `emit`, or `call` to mutating actions.

### Context Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `$this` | Current entity (bounded actions) | `$this.status`, `$this.id` |
| `$old` | Previous values (on update trigger) | `$old.status` (before change) |
| `$user` | Current authenticated user | `$user.id`, `$user.name`, `$user.roles` |
| `$tenant` | Current tenant context | `$tenant.id`, `$tenant.settings` |
| `$now` | Current UTC timestamp | `compute createdAt = $now;` |
| `$today` | Current UTC date | `compute dueDate = $today + 7;` |
| `$params` | All action parameters as object | `$params.orderId` |

### Exception Handling

**Simple approach (recommended)**: Use `validate` with severity levels

```bmmdl
action confirm() {
    // Validation errors abort the action
    validate $this.status = #Draft 
        message 'Only draft orders can be confirmed' 
        severity #Error;
    
    // Warnings log but don't abort
    validate $this.totalAmount > 0 
        message 'Order has zero amount' 
        severity #Warning;
    
    // Manual raise for complex conditions
    when $this.customer.creditLimit < $this.totalAmount then {
        raise 'Customer credit limit exceeded' severity #Error;
    }
}
```

**Severity levels**:
- `#Error` → Abort action, rollback transaction
- `#Warning` → Log warning, continue execution
- `#Info` → Log info, continue execution

### Complete Example

```bmmdl
service OrderService {
    entity Orders as Order;
    
    // Bounded action on Order entity
    action confirm() {
        // Pre-conditions
        validate $this.status = #Draft message 'Invalid status';
        validate $this.lines.count > 0 message 'Order has no lines';
        
        // Local variable
        let totalAmount = sum($this.lines.amount);
        
        // Conditional logic
        when totalAmount > 10000 then {
            validate $this.customer.creditApproved = true 
                message 'Large orders require credit approval';
        }
        
        // State changes
        compute $this.status = #Confirmed;
        compute $this.confirmedAt = $now;
        compute $this.confirmedBy = $user.id;
        
        // Publish event
        emit OrderConfirmed with {
            orderId: $this.id,
            confirmedBy: $user.id
        };
    }
    
    // Unbound action (multi-entity orchestration)
    @edge
    action bulkConfirm(orderIds: [UUID]) returns [Order] {
        let results = [];
        
        foreach orderId in orderIds {
            // Each call is a separate transaction
            call Orders(orderId).confirm();
        }
        
        return Orders where id in orderIds;
    }
    
    // Function (read-only)
    function calculateShipping(destination: String) returns Decimal {
        let baseRate = 10.00;
        let weight = sum($this.lines.weight);
        
        when destination = 'International' then {
            return baseRate + (weight * 0.5);
        } else {
            return baseRate + (weight * 0.2);
        }
    }
}
```

### AST Storage Strategy

Action/function bodies are stored in MetaModel as AST nodes with lazy parsing:

```csharp
public class BmAction
{
    public string Name { get; set; }
    public List<BmParameter> Parameters { get; set; }
    public BmTypeReference? ReturnType { get; set; }
    
    // AST - lazy parsed from SourceBody
    public List<BmActionStatement>? Body { get; set; }
    
    // Raw source for display/debugging
    public string? SourceBody { get; set; }
    
    // Execution target annotation
    public ExecutionLayer ExecutionLayer { get; set; } // Server, Edge, StoredProc
}

public enum ExecutionLayer { Server, Edge, StoredProc }
```

**Storage impact**: ~200 bytes per statement, ~2KB per action, acceptable for metadata loaded once per tenant.


## Edge Execution Architecture

### Overview

Unbound actions/functions can run on **Cloudflare Edge** (globally distributed, low-latency) while bounded operations remain on the **.NET OData Server** (transactional, DB access).

The `@edge` annotation determines where code executes:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Cloudflare Edge (Global)                             │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  @edge Unbound Actions/Functions (TypeScript)                     │  │
│  │  - Orchestration of multiple entities                             │  │
│  │  - Calls bounded actions via OData HTTP                           │  │
│  │  - No direct DB access                                            │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                  │ HTTP/OData calls
                                  ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    .NET OData Server (Core)                             │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Bounded Actions/Functions (C#)                                   │  │
│  │  - Single entity state changes                                    │  │
│  │  - Database transactions                                          │  │
│  │  - Business rules execution                                       │  │
│  ├───────────────────────────────────────────────────────────────────┤  │
│  │  Unbound Actions WITHOUT @edge (C#)                               │  │
│  │  - Complex transactions                                           │  │
│  │  - Heavy DB operations                                            │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

### @edge Annotation

```bmmdl
service OrderService {
    entity Orders as Order;
    entity Customers as Customer;

    // ═══════════════════════════════════════════════════════════════
    // EDGE EXECUTION - Runs on Cloudflare Workers
    // Generated as TypeScript, calls bounded actions via OData
    // ═══════════════════════════════════════════════════════════════
    
    @edge
    action bulkConfirmOrders(orderIds: [UUID]) returns [Order] {
        foreach orderId in orderIds {
            call Orders(orderId).confirm();  // HTTP call to bounded action
        }
        return Orders where id in orderIds;
    }

    @edge
    function getOrderStatistics(fromDate: Date, toDate: Date) returns OrderStatistics {
        return new OrderStatistics {
            totalOrders = count(Orders where createdAt between fromDate and toDate),
            totalRevenue = sum(Orders.totalAmount where status = #Completed)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // SERVER EXECUTION - Runs on .NET (default, no annotation)
    // Complex transactions, heavy DB operations
    // ═══════════════════════════════════════════════════════════════
    
    action processEndOfDay() returns ProcessingResult {
        // Complex multi-table transaction - runs on .NET
        // ...
    }
}
```

### Execution Strategy Decision

| Annotation | Execution Layer | Generated Code | Use Case |
|------------|-----------------|----------------|----------|
| `@edge` | Cloudflare Workers | TypeScript | Orchestration, read-heavy, global latency |
| (none) | .NET Server | C# | Complex transactions, heavy DB, security-critical |
| `@storedproc` | PostgreSQL | PL/pgSQL | Ultra-performance, DB-centric logic |

### Generated Edge Function (TypeScript)

```typescript
// Generated: workers/OrderService/bulkConfirmOrders.ts
import { Hono } from 'hono'

const app = new Hono()

app.post('/odata/OrderService/bulkConfirmOrders', async (c) => {
  const { orderIds } = await c.req.json()
  const tenantId = c.req.header('X-Tenant-Id')
  const token = c.req.header('Authorization')
  
  // Call bounded actions via $batch for efficiency
  const batchRequest = {
    requests: orderIds.map((id: string, index: number) => ({
      id: String(index),
      method: 'POST',
      url: `Orders('${id}')/confirm`,
      headers: { 'Content-Type': 'application/json' }
    }))
  }
  
  const response = await fetch(`${c.env.ODATA_SERVER}/$batch`, {
    method: 'POST',
    headers: {
      'Authorization': token,
      'X-Tenant-Id': tenantId,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(batchRequest)
  })
  
  return c.json(await response.json())
})

export default app
```

### $metadata Unification

The .NET server remains the **source of truth** for $metadata:

```xml
<EntityContainer Name="OrderService">
  <EntitySet Name="Orders" EntityType="OrderModule.Order"/>
  
  <!-- ActionImport declared here, but @edge ones execute on Edge -->
  <ActionImport Name="bulkConfirmOrders" Action="OrderModule.bulkConfirmOrders">
    <Annotation Term="BMMDL.ExecutionLayer" String="edge"/>
  </ActionImport>
  
  <!-- Non-edge actions execute on .NET -->
  <ActionImport Name="processEndOfDay" Action="OrderModule.processEndOfDay"/>
</EntityContainer>
```

### Benefits

| Aspect | Benefit |
|--------|---------|
| **Global Latency** | Edge functions execute in 300+ locations worldwide |
| **Cost** | Pay-per-invocation, no idle server costs |
| **Scalability** | Auto-scale to millions of requests |
| **Separation** | Core bounded actions on .NET, orchestration on Edge |
| **$metadata** | Single unified OData service definition |
| **Transactions** | Bounded actions handle transactions, Edge orchestrates |

### When to Use @edge

✅ **Good for Edge:**
- Orchestrating multiple bounded actions
- Read-heavy aggregations (with caching)
- Simple transformations/validations
- Global user base needing low latency

❌ **Keep on Server:**
- Complex multi-table transactions
- Security-critical operations
- Operations requiring DB locks
- Heavy joins/aggregations

### Security Context (JWT Pass-through)

Edge functions forward the user's original JWT to OData endpoints:

```
┌──────────────────────────────────────────────────────────────────────────┐
│                         Security Flow                                    │
│                                                                          │
│  1. User → Edge: Request + Authorization: Bearer {JWT}                   │
│                                                                          │
│  2. Edge validates JWT (optional, for early rejection)                   │
│     - Cloudflare Access or custom validation                             │
│     - Extract tenant_id for routing                                      │
│                                                                          │
│  3. Edge → OData: Forward same Authorization header                      │
│     - Authorization: Bearer {JWT}  (pass-through)                        │
│     - X-Tenant-Id: {tenant_id}                                           │
│     - X-Edge-Request-Id: {uuid}  (traceability)                          │
│                                                                          │
│  4. OData server validates JWT as usual                                  │
│     - Same validation logic for direct and edge requests                 │
│     - Full user context preserved                                        │
└──────────────────────────────────────────────────────────────────────────┘
```

**Generated Edge Function with Security:**

```typescript
// Generated: workers/OrderService/bulkConfirmOrders.ts
import { Hono } from 'hono'
import { jwt } from 'hono/jwt'

const app = new Hono()

// Optional: Early JWT validation at Edge
app.use('/*', async (c, next) => {
  const authHeader = c.req.header('Authorization')
  if (!authHeader?.startsWith('Bearer ')) {
    return c.json({ error: 'Unauthorized' }, 401)
  }
  
  // Optional: Validate JWT structure/expiry (not signature - let OData do full validation)
  // This is for early rejection of obviously invalid tokens
  try {
    const token = authHeader.substring(7)
    const payload = JSON.parse(atob(token.split('.')[1]))
    
    if (payload.exp && payload.exp < Date.now() / 1000) {
      return c.json({ error: 'Token expired' }, 401)
    }
    
    c.set('tenantId', payload.tenant_id || c.req.header('X-Tenant-Id'))
    c.set('userId', payload.sub)
  } catch {
    return c.json({ error: 'Invalid token format' }, 401)
  }
  
  await next()
})

app.post('/odata/OrderService/bulkConfirmOrders', async (c) => {
  const { orderIds } = await c.req.json()
  
  // Pass-through: Forward original Authorization header
  const response = await fetch(`${c.env.ODATA_SERVER}/$batch`, {
    method: 'POST',
    headers: {
      'Authorization': c.req.header('Authorization')!,  // Forward JWT
      'X-Tenant-Id': c.get('tenantId'),
      'X-Edge-Request-Id': crypto.randomUUID(),  // Traceability
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      requests: orderIds.map((id: string, i: number) => ({
        id: String(i),
        method: 'POST',
        url: `Orders('${id}')/confirm`
      }))
    })
  })
  
  return c.json(await response.json())
})

export default app
```

**Why JWT Pass-through:**

| Aspect | Benefit |
|--------|---------|
| **Simplicity** | No token exchange, no service accounts |
| **Consistency** | OData validates JWT identically for all requests |
| **User Context** | Full permissions preserved in original token |
| **Audit Trail** | `X-Edge-Request-Id` for tracing edge-originated calls |

**Edge Considerations:**

- **Token Expiry**: Edge functions should be fast (<5s), so JWT expiry mid-request is unlikely
- **Retry Logic**: If OData returns 401, Edge should NOT retry (user needs to refresh token)
- **Caching**: Never cache responses that depend on user identity

---

## Implementation Phases

### Phase 1: Grammar Extension
- [ ] Add action/function body syntax to `BmmdlParser.g4`
- [ ] Add `emit`, `foreach`, `call` statement types
- [ ] Update `BmmdlModelBuilder.cs` to parse body statements

### Phase 2: MetaModel Enhancement
- [ ] Add `BmAction.Body: List<BmRuleStatement>`
- [ ] Add `BmFunction.Body: List<BmRuleStatement>`
- [ ] Add `BmEntitySet` for service projections
- [ ] Update `BmService` to use `EntitySets` instead of raw `Entities`

### Phase 3: Code Generation (PostgreSQL)
- [ ] Generate stored procedures for actions/functions
- [ ] Generate EDMX/CSDL $metadata document
- [ ] Generate OData service document

### Phase 3b: Code Generation (C# Classes - Server)
- [ ] Create `BMMDL.CodeGen.CSharp` project
- [ ] Generate Entity classes with EF Core attributes
- [ ] Generate Bound Action/Function classes
- [ ] Generate Unbound Action/Function classes (non-@edge)
- [ ] Generate Domain Event record classes
- [ ] Generate DI registration extension methods
- [ ] Add `@storedproc` annotation support

### Phase 3c: Code Generation (TypeScript - Edge)
- [ ] Create `BMMDL.CodeGen.Edge` project
- [ ] Generate TypeScript functions for `@edge` annotated actions
- [ ] Generate Cloudflare Workers entry points (Hono framework)
- [ ] Generate $batch calls for orchestrating bounded actions
- [ ] Generate wrangler.toml configuration
- [ ] Generate deployment scripts for edge functions
- [ ] Include OData response formatting

### Phase 4: Runtime Unification
- [ ] Merge `DynamicEntityController` + `DynamicServiceController` → `ODataController`
- [ ] Route pattern: `/odata/{service}/{entitySet}[({key})][/{operation}]`
- [ ] Implement `IActionRegistry` for action handler resolution
- [ ] Support multiple execution strategies:
  - [ ] `@storedproc` → PostgreSQL stored procedure execution
  - [ ] (default) → C# Class execution (via DI)
  - [ ] `@edge` → Cloudflare Workers (TypeScript)
- [ ] Add `IActionDispatcher` to route to correct strategy
- [ ] For @edge actions: return 307 redirect to Edge endpoint OR proxy

### Phase 5: Edge Infrastructure
- [ ] Setup Cloudflare Workers project structure
- [ ] Implement service-to-service authentication (Edge → .NET)
- [ ] Configure Hyperdrive for DB connection pooling (if needed)
- [ ] Add Edge function monitoring (logs, metrics)
- [ ] Implement caching layer (Cloudflare KV/Cache API)
- [ ] CI/CD pipeline for Edge deployments

### Phase 6: Validation & Testing
- [ ] Validate action body statements at compile time
- [ ] Type-check parameters and return types
- [ ] E2E tests for bounded operations (.NET)
- [ ] E2E tests for @edge operations (Workers)
- [ ] Integration tests for Edge ↔ OData communication


## Migration from Current Implementation

### Current State
```
/api/odata/{module}/{entity}              → DynamicEntityController (CRUD)
/api/odata/{module}/{entity}/{id}/{action} → DynamicEntityController (Bound)
/api/v1/services/{module}/{service}/...    → DynamicServiceController (Unbound)
```

### Target State
```
/odata/{service}/{entitySet}               → ODataController (CRUD)
/odata/{service}/{entitySet}({id})/{action} → ODataController (Bound Action)
/odata/{service}/{entitySet}({id})/{func}() → ODataController (Bound Function)
/odata/{service}/{actionImport}            → ODataController (Unbound Action)
/odata/{service}/{functionImport}()        → ODataController (Unbound Function)
/odata/{service}/$metadata                 → ODataController (Metadata)
```

### Backward Compatibility
- Keep old routes active during transition with deprecation warning
- Add `X-Deprecated-Route: true` header for old routes
- Remove after 2 major versions

## References

- [OData v4 Specification](https://docs.oasis-open.org/odata/odata/v4.01/odata-v4.01-part1-protocol.html)
- [OData CSDL Schema](https://docs.oasis-open.org/odata/odata-csdl-xml/v4.01/odata-csdl-xml-v4.01.html)
- [SAP CDS Service Definition](https://cap.cloud.sap/docs/cds/cdl#services)
