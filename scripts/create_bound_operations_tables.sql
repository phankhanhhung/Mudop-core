-- Bound Operations and Statement Nodes tables for BMMDL Registry
-- Creates entity_bound_operations, bound_operation_parameters, bound_operation_emits, statement_nodes
-- For storing action/function bodies in normalized AST form

-- Entity Bound Operations (actions and functions on entities)
CREATE TABLE IF NOT EXISTS registry.entity_bound_operations (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "EntityId" UUID NOT NULL,
    "ModuleId" UUID,
    "Name" VARCHAR(255) NOT NULL,
    "OperationType" VARCHAR(20) NOT NULL,  -- 'action' | 'function'
    "ReturnType" VARCHAR(255),
    "BodyDefinitionHash" VARCHAR(64),
    "BodyRootStatementId" UUID,
    "Position" INT NOT NULL DEFAULT 0,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    FOREIGN KEY ("TenantId") REFERENCES registry.tenants("Id") ON DELETE CASCADE,
    FOREIGN KEY ("EntityId") REFERENCES registry.entities("Id") ON DELETE CASCADE,
    FOREIGN KEY ("ModuleId") REFERENCES registry.modules("Id") ON DELETE SET NULL,
    UNIQUE ("EntityId", "Name")
);

-- Bound Operation Parameters
CREATE TABLE IF NOT EXISTS registry.bound_operation_parameters (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OperationId" UUID NOT NULL,
    "Name" VARCHAR(255) NOT NULL,
    "TypeString" VARCHAR(255) NOT NULL,
    "Position" INT NOT NULL DEFAULT 0,
    FOREIGN KEY ("OperationId") REFERENCES registry.entity_bound_operations("Id") ON DELETE CASCADE,
    UNIQUE ("OperationId", "Name")
);

-- Bound Operation Emits (events emitted by actions)
CREATE TABLE IF NOT EXISTS registry.bound_operation_emits (
    "OperationId" UUID NOT NULL,
    "EventName" VARCHAR(255) NOT NULL,
    "Position" INT NOT NULL DEFAULT 0,
    PRIMARY KEY ("OperationId", "EventName"),
    FOREIGN KEY ("OperationId") REFERENCES registry.entity_bound_operations("Id") ON DELETE CASCADE
);

-- Statement Nodes (AST for action/function body)
CREATE TABLE IF NOT EXISTS registry.statement_nodes (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "OwnerType" VARCHAR(50) NOT NULL,  -- 'operation', 'when_then', 'when_else', 'foreach'
    "OwnerId" UUID NOT NULL,
    "NodeType" VARCHAR(50) NOT NULL,   -- 'validate', 'compute', 'let', 'emit', 'return', 'raise', 'when', 'foreach', 'call'
    "ParentId" UUID,
    "ParentRole" VARCHAR(50),          -- 'body', 'then', 'else'
    "Position" INT NOT NULL DEFAULT 0,
    "TargetField" VARCHAR(255),
    "Message" TEXT,
    "ConditionExprRootId" UUID,
    "ValueExprRootId" UUID,
    "VariableName" VARCHAR(255),
    "EventName" VARCHAR(255),
    "Severity" VARCHAR(20),            -- 'error', 'warning'
    "IteratorVariable" VARCHAR(255),
    "CollectionExprRootId" UUID,
    "CallTarget" VARCHAR(255),
    FOREIGN KEY ("ParentId") REFERENCES registry.statement_nodes("Id") ON DELETE CASCADE,
    FOREIGN KEY ("ConditionExprRootId") REFERENCES registry.expression_nodes("Id") ON DELETE SET NULL,
    FOREIGN KEY ("ValueExprRootId") REFERENCES registry.expression_nodes("Id") ON DELETE SET NULL,
    FOREIGN KEY ("CollectionExprRootId") REFERENCES registry.expression_nodes("Id") ON DELETE SET NULL
);

-- Add FK from operations to statement_nodes (deferred due to circular dependency)
ALTER TABLE registry.entity_bound_operations 
ADD CONSTRAINT fk_body_root_statement 
FOREIGN KEY ("BodyRootStatementId") REFERENCES registry.statement_nodes("Id") ON DELETE SET NULL;

-- Indexes
CREATE INDEX IF NOT EXISTS idx_bound_operations_entity ON registry.entity_bound_operations("EntityId");
CREATE INDEX IF NOT EXISTS idx_bound_operations_tenant_module ON registry.entity_bound_operations("TenantId", "ModuleId");
CREATE INDEX IF NOT EXISTS idx_bound_operations_hash ON registry.entity_bound_operations("BodyDefinitionHash");
CREATE INDEX IF NOT EXISTS idx_statement_nodes_owner ON registry.statement_nodes("OwnerType", "OwnerId");
CREATE INDEX IF NOT EXISTS idx_statement_nodes_type ON registry.statement_nodes("NodeType");

-- ============================================================
-- Service Operations (unbound actions/functions) Body Storage
-- ============================================================

-- Add body storage columns to service_operations
ALTER TABLE registry.service_operations 
ADD COLUMN IF NOT EXISTS "BodyDefinitionHash" VARCHAR(64),
ADD COLUMN IF NOT EXISTS "BodyRootStatementId" UUID,
ADD COLUMN IF NOT EXISTS "Position" INT NOT NULL DEFAULT 0;

-- Add FK for body root statement
ALTER TABLE registry.service_operations 
ADD CONSTRAINT fk_service_op_body_root_statement 
FOREIGN KEY ("BodyRootStatementId") REFERENCES registry.statement_nodes("Id") ON DELETE SET NULL;

-- Service Operation Emits (events emitted by unbound actions)
CREATE TABLE IF NOT EXISTS registry.service_operation_emits (
    "OperationId" UUID NOT NULL,
    "EventName" VARCHAR(255) NOT NULL,
    "Position" INT NOT NULL DEFAULT 0,
    PRIMARY KEY ("OperationId", "EventName"),
    FOREIGN KEY ("OperationId") REFERENCES registry.service_operations("Id") ON DELETE CASCADE
);

-- Indexes for service operations
CREATE INDEX IF NOT EXISTS idx_service_operations_hash ON registry.service_operations("BodyDefinitionHash");

