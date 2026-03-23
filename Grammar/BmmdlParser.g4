parser grammar BmmdlParser;

options { tokenVocab=BmmdlLexer; }

// ============================================================
// Root Rule
// ============================================================

compilationUnit
    : (moduleBlock | namespaceBlock | legacyCompilationUnit) EOF
    ;

// Legacy support for flat syntax (to be removed after migration)
legacyCompilationUnit
    : (moduleDecl)? (namespaceStmt)? (importStmt)* (definition)*
    ;

// ============================================================
// Module Declaration (optional, at file start)
// ============================================================

// New nested module block - contains namespace blocks
moduleBlock
    : MODULE IDENTIFIER moduleVersion? LBRACE 
        moduleProperty* 
        namespaceBlock* 
      RBRACE
    ;

// Legacy module declaration (flat syntax)
moduleDecl
    : MODULE IDENTIFIER moduleVersion? LBRACE moduleProperty* RBRACE
    ;

moduleVersion
    : VERSION STRING_LITERAL
    ;

moduleProperty
    : AUTHOR COLON STRING_LITERAL SEMICOLON
    | DESCRIPTION COLON STRING_LITERAL SEMICOLON
    | DEPENDS ON IDENTIFIER moduleVersionRange SEMICOLON
    | PUBLISHES identifierReference (COMMA identifierReference)* SEMICOLON
    | IMPORTS identifierReference (COMMA identifierReference)* SEMICOLON
    | TENANT MINUS AWARE COLON (TRUE | FALSE) SEMICOLON
    ;

moduleVersionRange
    : VERSION STRING_LITERAL
    ;

// ============================================================
// Structure Definitions
// ============================================================

// New nested namespace block - contains definitions
namespaceBlock
    : NAMESPACE qualifiedName LBRACE 
        (importStmt)* 
        (definition)* 
      RBRACE
    ;

// Legacy namespace statement (flat syntax)
namespaceStmt
    : NAMESPACE qualifiedName SEMICOLON
    ;

qualifiedName
    : IDENTIFIER (DOT IDENTIFIER)*
    ;

importStmt
    : USING ( importAlias )? identifierReference FROM STRING_LITERAL SEMICOLON
    | USING identifierReference SEMICOLON
    ;

importAlias
    : IDENTIFIER COLON
    ;

identifierReference
    : IDENTIFIER (DOT IDENTIFIER)*
    ;

definition
    : annotation* (
          contextDef
        | entityDef
        | typeDef
        | enumDef
        | serviceDef
        | aspectDef
        | tableDef
        | extendDef
        | modifyDef
        | annotateDef
        | accessControlDef
        | ruleDef
        | sequenceDef
        | eventDef
        | migrationDef
        | seedDef
    )
    ;

contextDef
    : CONTEXT IDENTIFIER LBRACE definition* RBRACE
    ;

// ============================================================
// Annotation
// ============================================================

annotation
    : AT identifierReference ( LPAREN annotationValue? RPAREN )?
    | AT identifierReference COLON annotationValue
    | AT identifierReference LBRACE (annotationProperty (COMMA annotationProperty)*)? RBRACE  // Phase 8: @Temporal { strategy: 'inline' }
    ;

annotationValue
    : literal
    | LBRACKET (annotationValue (COMMA annotationValue)*)? RBRACKET
    | LBRACE (annotationProperty (COMMA annotationProperty)*)? RBRACE
    | HASH IDENTIFIER
    | expression
    ;

annotationProperty
    : softIdentifier COLON annotationValue  // Allow reserved keywords like 'from', 'to' as property names
    ;

// ============================================================
// Entity Definition
// ============================================================

entityDef
    : ABSTRACT? ENTITY IDENTIFIER (EXTENDS identifierReference)?
      (COLON identifierReference (COMMA identifierReference)*)? LBRACE entityElement* RBRACE
    ;

entityElement
    : annotation* (
          fieldDef
        | associationDef
        | compositionDef
        | keyElement
        | actionDef
        | functionDef
        | indexDef
        | constraintDef
    ) SEMICOLON?
    ;

keyElement
    : KEY fieldDef
    ;

fieldDef
    : fieldModifier* softIdentifier COLON typeReference (defaultExpr | computedExpr)?
    ;

fieldModifier
    : VIRTUAL | READONLY | IMMUTABLE
    ;

// Soft keywords that can also be used as identifiers (field names, etc.)
softIdentifier
    : IDENTIFIER
    | DESCRIPTION
    | VERSION
    | AUTHOR
    | BREAKING
    | SET
    | DROP
    | ALTER
    | COLUMN
    | NULLABLE
    | UP
    | DOWN
    | TRANSFORM
    | DEPENDS
    | ROLLBACK
    | FROM      // Phase 8: Allow 'from' in annotation properties
    | TO        // Phase 8: Allow 'to' in annotation properties
    | START     // Phase 8: Allow 'start' as field/property name
    | END       // Phase 8: Allow 'end' as field/property name
    | YEAR      // Allow date-part keywords as field names
    | MONTH
    | DAY
    | HOUR
    | MINUTE
    | SECOND
    | SEED      // Seed data keywords as field names
    | INSERT
    | VALUES
    ;

computedExpr
    : (COMPUTED | STORED) EQ expression
    ;

typeReference
    : identifierReference (LPAREN INTEGER_LITERAL (COMMA INTEGER_LITERAL)? RPAREN)?
    | builtInType (LPAREN INTEGER_LITERAL (COMMA INTEGER_LITERAL)? RPAREN)?
    | ARRAY LT typeReference GT
    | LOCALIZED typeReference
    ;

builtInType
    : STRING | INTEGER | DECIMAL | BOOLEAN | TIMESTAMP | DATE | TIME | UUID | BINARY | DATETIME
    ;

defaultExpr
    : DEFAULT expression
    ;

associationDef
    : IDENTIFIER COLON ASSOCIATION cardinality? TO identifierReference (ON expression)?
    | ASSOCIATION cardinality? TO identifierReference (ON expression)? (AS IDENTIFIER)?
    ;

compositionDef
    : IDENTIFIER COLON COMPOSITION cardinality? OF identifierReference
    | COMPOSITION cardinality? OF identifierReference (AS IDENTIFIER)?
    ;

cardinality
    : LBRACKET (INTEGER_LITERAL | STAR)? (COMMA (INTEGER_LITERAL | STAR))? RBRACKET
    ;

// ============================================================
// Index and Constraint Definitions
// ============================================================

indexDef
    : UNIQUE? INDEX IDENTIFIER ON LPAREN fieldRefList RPAREN
    ;

constraintDef
    : CONSTRAINT IDENTIFIER constraintType
    ;

constraintType
    : checkConstraint
    | foreignKeyConstraint
    | uniqueConstraint
    ;

checkConstraint
    : CHECK LPAREN expression RPAREN
    ;

foreignKeyConstraint
    : FOREIGN KEY LPAREN fieldRefList RPAREN REFERENCES identifierReference LPAREN fieldRefList RPAREN
    ;

uniqueConstraint
    : UNIQUE LPAREN fieldRefList RPAREN
    ;

fieldRefList
    : IDENTIFIER (COMMA IDENTIFIER)*
    ;

aspectDef
    : ASPECT IDENTIFIER (COLON identifierReference (COMMA identifierReference)*)? LBRACE aspectElement* RBRACE
    ;

aspectElement
    : annotation* (
          fieldDef
        | associationDef
        | compositionDef
        | keyElement
        | actionDef
        | functionDef
        | indexDef
        | constraintDef
    ) SEMICOLON?
    | ruleDef
    | accessControlDef
    ;

// ============================================================
// View / Table Definition
// ============================================================
    
tableDef
    : DEFINE? VIEW IDENTIFIER viewParams? AS (selectStatement | projectionDef)
    ;

projectionDef
    : PROJECTION ON identifierReference LBRACE projectionField* RBRACE
    ;

projectionField
    : identifierReference (AS IDENTIFIER)? SEMICOLON?
    | STAR (EXCLUDING LPAREN fieldRefList RPAREN)? SEMICOLON?
    ;

viewParams
    : WITH PARAMETERS LPAREN viewParam (COMMA viewParam)* RPAREN
    ;

viewParam
    : IDENTIFIER COLON typeReference (DEFAULT expression)?
    ;

selectStatement
    : SELECT DISTINCT? selectList FROM fromClause
      whereClause? groupByClause? havingClause? orderByClause?
      unionClause*
    ;

selectList
    : STAR
    | selectItem (COMMA selectItem)*
    ;

selectItem
    : expression (AS IDENTIFIER)?
    | identifierReference DOT STAR
    ;

fromClause
    : fromSource (joinClause)*
    ;

fromSource
    : identifierReference temporalQualifier? (AS IDENTIFIER)?
    | LPAREN selectStatement RPAREN (AS IDENTIFIER)?
    ;

temporalQualifier
    : TEMPORAL ASOF expression
    | TEMPORAL VERSIONS ALL
    | TEMPORAL VERSIONS BETWEEN expression AND expression
    | CURRENT
    ;

joinClause
    : joinType? JOIN fromSource ON expression
    ;

joinType
    : INNER | LEFT OUTER? | RIGHT OUTER? | FULL OUTER? | CROSS
    ;

whereClause
    : WHERE expression
    ;

groupByClause
    : GROUP BY expression (COMMA expression)*
    ;

havingClause
    : HAVING expression
    ;

orderByClause
    : ORDER BY orderItem (COMMA orderItem)*
    ;

orderItem
    : expression (ASC | DESC)? (NULLS (FIRST | LAST))?
    ;

unionClause
    : (UNION ALL? | INTERSECT | EXCEPT) selectStatement
    ;

// ============================================================
// Type and Enum
// ============================================================

typeDef
    : TYPE IDENTIFIER COLON typeReference SEMICOLON?
    | TYPE IDENTIFIER COLON structDef SEMICOLON?
    ;

structDef
    : LBRACE structField (SEMICOLON structField)* SEMICOLON? RBRACE
    ;

structField
    : annotation* IDENTIFIER COLON typeReference
    ;

enumDef
    : (TYPE)? ENUM IDENTIFIER (COLON typeReference)? LBRACE enumValue* RBRACE
    ;

enumValue
    : annotation* IDENTIFIER (EQ literal)? SEMICOLON?
    ;

// ============================================================
// Service Definition
// ============================================================

serviceDef
    : SERVICE IDENTIFIER (FOR identifierReference)? LBRACE serviceElement* RBRACE
    ;

serviceElement
    : annotation* (
          entityExposure
        | functionDef
        | actionDef
        | serviceEventHandler
    )
    ;

serviceEventHandler
    : ON identifierReference LBRACE actionStmt* RBRACE
    ;

entityExposure
    : ENTITY IDENTIFIER AS identifierReference projectionClause? SEMICOLON?
    ;

projectionClause
    : LBRACE (STAR | projectionItem (COMMA projectionItem)*) excludingClause? RBRACE
    ;

projectionItem
    : IDENTIFIER (AS IDENTIFIER)?
    | identifierReference DOT STAR
    ;

excludingClause
    : EXCLUDING LBRACE IDENTIFIER (COMMA IDENTIFIER)* RBRACE
    ;


functionDef
    : COMPOSABLE? FUNCTION IDENTIFIER LPAREN parameterList? RPAREN RETURNS typeReference
      (LBRACE functionStmt* RBRACE)?   // Optional body for function
      SEMICOLON?
    ;

actionDef
    : ACTION IDENTIFIER LPAREN parameterList? RPAREN (RETURNS typeReference)? 
      actionClause* 
      (LBRACE actionStmt* RBRACE)?     // Optional body for action
      SEMICOLON?
    ;

actionClause
    : REQUIRES expression
    | ENSURES expression
    | MODIFIES IDENTIFIER EQ expression
    | EMITS identifierReference
    ;

parameterList
    : parameter (COMMA parameter)*
    ;

parameter
    : annotation* IDENTIFIER COLON typeReference (DEFAULT expression)?
    ;

// ============================================================
// Extension Definition
// ============================================================

extendDef
    : EXTEND (ENTITY | TYPE | ASPECT | SERVICE) identifierReference
      (WITH identifierReference (COMMA identifierReference)*)?
      LBRACE (entityElement | entityExposure)* RBRACE
    | EXTEND ENUM identifierReference LBRACE enumValue* RBRACE
    ;

// ============================================================
// Modification Definition
// ============================================================

modifyDef
    : MODIFY (ENTITY | TYPE | ASPECT | SERVICE | ENUM) identifierReference LBRACE modifyAction* RBRACE
    ;

modifyAction
    : MODIFY IDENTIFIER LBRACE modifyProp* RBRACE SEMICOLON?
    | REMOVE IDENTIFIER SEMICOLON
    | RENAME IDENTIFIER TO IDENTIFIER SEMICOLON
    | CHANGE TYPE OF IDENTIFIER TO typeReference SEMICOLON
    | ADD fieldDef SEMICOLON
    | ADD IDENTIFIER (EQ literal)? SEMICOLON
    ;

modifyProp
    : TYPE COLON typeReference SEMICOLON
    | DEFAULT COLON expression SEMICOLON
    | annotation SEMICOLON
    ;

// ============================================================
// Annotate Definition
// ============================================================

annotateDef
    : ANNOTATE identifierReference WITH LBRACE annotateItem* RBRACE
    ;

annotateItem
    : (IDENTIFIER COLON)? annotation SEMICOLON
    ;

// ============================================================
// Access Control Definition
// ============================================================

accessControlDef
    : ACCESS CONTROL (FOR identifierReference)? (EXTENDS identifierReference)?
      LBRACE accessRule* RBRACE
    ;

accessRule
    : GRANT operation (COMMA operation)* (TO principal)? (AT scopeLevel)? whereClause? SEMICOLON
    | DENY operation (COMMA operation)* (TO principal)? (AT scopeLevel)? whereClause? SEMICOLON
    | RESTRICT FIELDS LBRACE fieldRestriction* RBRACE SEMICOLON
    ;

scopeLevel
    : TENANT SCOPE
    | COMPANY SCOPE
    | GLOBAL SCOPE
    | TENANT
    | COMPANY
    | GLOBAL
    ;

operation
    : READ | WRITE | CREATE | UPDATE | DELETE | EXECUTE | ALL
    ;

principal
    : ROLE STRING_LITERAL (COMMA STRING_LITERAL)*
    | USER STRING_LITERAL (COMMA STRING_LITERAL)*
    | AUTHENTICATED
    | ANONYMOUS
    ;

fieldRestriction
    : IDENTIFIER COLON fieldAccessRule SEMICOLON
    ;

fieldAccessRule
    : VISIBLE WHEN expression
    | MASKED (WITH IDENTIFIER)?
    | READONLY WHEN expression
    | HIDDEN_KW (WHEN expression)?
    ;

// ============================================================
// Rule Definition (Business Rules)
// ============================================================

ruleDef
    : RULE IDENTIFIER (FOR identifierReference)? (ON triggerEvent (COMMA triggerEvent)*)? 
      LBRACE ruleStmt* RBRACE
    ;

triggerEvent
    : BEFORE? (CREATE | UPDATE | DELETE | READ)
    | AFTER (CREATE | UPDATE | DELETE)
    | ON CHANGE OF IDENTIFIER (COMMA IDENTIFIER)*
    ;

ruleStmt
    : validateStmt
    | computeStmt
    | whenStmt
    | callStmt
    | raiseStmt
    | rejectStmt
    | foreachStmt
    | letStmt
    | emitStmt
    ;

validateStmt
    : VALIDATE expression (MESSAGE STRING_LITERAL)? (SEVERITY severityLevel)? SEMICOLON
    ;

severityLevel
    : ERROR | WARNING | INFO
    ;

computeStmt
    : COMPUTE identifierReference EQ expression SEMICOLON
    ;

whenStmt
    : WHEN expression THEN LBRACE ruleStmt* RBRACE (ELSE LBRACE ruleStmt* RBRACE)?
    ;

callStmt
    : CALL identifierReference LPAREN argumentList? RPAREN SEMICOLON
    ;

// ============================================================
// Action/Function Body Statements
// ============================================================

// Action statements (can mutate state)
actionStmt
    : validateStmt
    | computeStmt
    | whenActionStmt
    | callStmt
    | emitStmt
    | foreachStmt
    | returnStmt
    | letStmt
    | raiseStmt
    | rejectStmt
    ;

// Function statements (read-only)
functionStmt
    : letStmt
    | whenFuncStmt
    | returnStmt
    | callStmt
    ;

emitStmt
    : EMIT identifierReference (WITH LBRACE emitField* RBRACE)? SEMICOLON
    ;

emitField
    : IDENTIFIER COLON expression SEMICOLON?
    ;

foreachStmt
    : FOREACH IDENTIFIER IN expression LBRACE actionStmt* RBRACE
    ;

returnStmt
    : RETURN expression SEMICOLON
    ;

letStmt
    : LET IDENTIFIER EQ expression SEMICOLON
    ;

raiseStmt
    : RAISE STRING_LITERAL (SEVERITY severityLevel)? SEMICOLON
    ;

rejectStmt
    : REJECT expression? SEMICOLON
    ;

whenActionStmt
    : WHEN expression THEN LBRACE actionStmt* RBRACE 
      (ELSE LBRACE actionStmt* RBRACE)?
    ;

whenFuncStmt
    : WHEN expression THEN LBRACE functionStmt* RBRACE 
      (ELSE LBRACE functionStmt* RBRACE)?
    ;

// ============================================================
// Sequence Definition
// ============================================================

sequenceDef
    : SEQUENCE IDENTIFIER (FOR identifierReference DOT IDENTIFIER)?
      LBRACE sequenceProp* RBRACE
    ;

sequenceProp
    : PATTERN COLON STRING_LITERAL SEMICOLON
    | START COLON INTEGER_LITERAL SEMICOLON
    | INCREMENT COLON INTEGER_LITERAL SEMICOLON
    | PADDING COLON INTEGER_LITERAL SEMICOLON
    | SCOPE COLON scopeLevel SEMICOLON
    | RESET ON resetTrigger SEMICOLON
    | MAX COLON INTEGER_LITERAL SEMICOLON
    ;

resetTrigger
    : NEVER | DAILY | MONTHLY | YEARLY | FISCAL YEAR?
    ;

// ============================================================
// Event Definition (Domain Events)
// ============================================================

eventDef
    : EVENT IDENTIFIER LBRACE eventField* RBRACE
    ;

eventField
    : annotation* IDENTIFIER COLON typeReference SEMICOLON?
    ;

// ============================================================
// Expressions
// ============================================================

expression
    : literal                                                   # LiteralExpr
    | identifierReference                                       # RefExpr
    | contextVar                                                # ContextVarExpr
    | paramRef                                                  # ParamRefExpr
    | functionCall                                              # FunctionCallExpr
    | aggregateCall                                             # AggregateExpr
    | windowFunctionExpr                                        # WindowExpr
    | caseExpr                                                  # CaseExpression
    | castExpr                                                  # CastExpression
    | LPAREN expression RPAREN                                  # ParenExpr
    | LPAREN selectStatement RPAREN                             # SubqueryExpr
    | (NOT | MINUS | PLUS) expression                           # UnaryExpr
    | expression (STAR | SLASH | PERCENT) expression            # MultDivExpr
    | expression (PLUS | MINUS) expression                      # AddSubExpr
    | expression DOUBLE_PIPE expression                         # ConcatExpr
    | expression (GT | LT | GTE | LTE) expression               # RelationalExpr
    | expression (EQ | NEQ) expression                          # EqualityExpr
    | expression IS NOT? NULL                                   # IsNullExpr
    | expression NOT? IN inExpr                                 # InExpression
    | expression NOT? BETWEEN expression AND expression         # BetweenExpr
    | expression NOT? LIKE expression                           # LikeExpr
    | expression OVERLAPS expression                            # OverlapsExpr
    | expression CONTAINS expression                            # ContainsExpr
    | expression PRECEDES expression                            # PrecedesExpr
    | expression MEETS expression                               # MeetsExpr
    | EXISTS LPAREN selectStatement RPAREN                      # ExistsExpr
    | expression AND expression                                 # AndExpr
    | expression OR expression                                  # OrExpr
    | expression QUESTION expression COLON expression           # TernaryExpr
    ;

contextVarSegment
    : IDENTIFIER | USER
    ;

contextVar
    : DOLLAR contextVarSegment (DOT contextVarSegment)*
    ;

paramRef
    : COLON IDENTIFIER
    ;

inExpr
    : LPAREN (expression (COMMA expression)* | selectStatement) RPAREN
    ;

functionCall
    : (IDENTIFIER | builtInFunc) LPAREN argumentList? RPAREN
    ;

builtInFunc
    : CONCAT | SUBSTRING | UPPER | LOWER | TRIM | LTRIM | RTRIM | LENGTH | REPLACE | INSTR | LPAD | RPAD
    | ABS | CEIL | FLOOR | ROUND | TRUNC | MOD | POWER | SQRT | SIGN
    | YEAR | MONTH | DAY | HOUR | MINUTE | SECOND | DAYOFWEEK | WEEKOFYEAR
    | DATEDIFF | ADD_DAYS | ADD_MONTHS | ADD_YEARS
    | COALESCE | IFNULL | NULLIF | DECODE
    | CURRENT_DATE | CURRENT_TIME | CURRENT_TIMESTAMP
    | TO_INTEGER | TO_DECIMAL | TO_STRING | TO_DATE | TO_TIME | TO_TIMESTAMP | FORMAT
    | NEXT_SEQUENCE | CURRENT_SEQUENCE | FORMAT_SEQUENCE | RESET_SEQUENCE | SET_SEQUENCE | PAD_LEFT | PAD_RIGHT
    | CURRENCY_CONVERSION | UNIT_CONVERSION
    | FISCAL_YEAR | FISCAL_PERIOD
    ;

aggregateCall
    : (COUNT | SUM | AVG | MIN | MAX | STDDEV | VARIANCE) LPAREN DISTINCT? expression (WHERE expression)? RPAREN
    | COUNT LPAREN STAR RPAREN
    ;

windowFunctionExpr
    : windowFunction OVER LPAREN windowSpec RPAREN
    ;

windowFunction
    : ROW_NUMBER LPAREN RPAREN
    | RANK LPAREN RPAREN
    | DENSE_RANK LPAREN RPAREN
    | NTILE LPAREN expression RPAREN
    | LAG LPAREN expression (COMMA expression)* RPAREN
    | LEAD LPAREN expression (COMMA expression)* RPAREN
    | FIRST_VALUE LPAREN expression RPAREN
    | LAST_VALUE LPAREN expression RPAREN
    | (SUM | AVG | COUNT | MIN | MAX) LPAREN expression RPAREN
    ;

windowSpec
    : (PARTITION BY expression (COMMA expression)*)?
      (ORDER BY orderItem (COMMA orderItem)*)?
      windowFrame?
    ;

windowFrame
    : (ROWS | RANGE) windowFrameBound
    | (ROWS | RANGE) BETWEEN windowFrameBound AND windowFrameBound
    ;

windowFrameBound
    : UNBOUNDED PRECEDING
    | UNBOUNDED FOLLOWING
    | CURRENT ROW
    | expression PRECEDING
    | expression FOLLOWING
    ;

caseExpr
    : CASE expression? whenClause+ elseClause? END
    ;

whenClause
    : WHEN expression THEN expression
    ;

elseClause
    : ELSE expression
    ;

castExpr
    : CAST LPAREN expression AS typeReference RPAREN
    ;

argumentList
    : argument (COMMA argument)*
    ;

argument
    : (IDENTIFIER COLON)? expression
    ;

literal
    : STRING_LITERAL
    | INTEGER_LITERAL
    | DECIMAL_LITERAL
    | TRUE
    | FALSE
    | NULL
    | HASH IDENTIFIER
    ;

// ============================================================
// Migration Definition
// ============================================================

migrationDef
    : MIGRATION STRING_LITERAL LBRACE migrationProperty* migrationBody RBRACE
    ;

migrationProperty
    : VERSION COLON STRING_LITERAL SEMICOLON
    | AUTHOR COLON STRING_LITERAL SEMICOLON
    | DESCRIPTION COLON STRING_LITERAL SEMICOLON
    | BREAKING COLON (TRUE | FALSE) SEMICOLON
    | DEPENDS COLON STRING_LITERAL (COMMA STRING_LITERAL)* SEMICOLON
    ;

migrationBody
    : upBlock downBlock?
    ;

upBlock
    : UP LBRACE migrationStep* RBRACE
    ;

downBlock
    : DOWN LBRACE migrationStep* RBRACE
    ;

migrationStep
    : alterEntityStep
    | transformStep
    | addEntityStep
    | dropEntityStep
    ;

alterEntityStep
    : ALTER ENTITY identifierReference LBRACE alterAction* RBRACE
    ;

addEntityStep
    : ADD ENTITY identifierReference LBRACE entityElement* RBRACE
    ;

dropEntityStep
    : DROP ENTITY identifierReference SEMICOLON
    ;

alterAction
    : ADD fieldDef SEMICOLON
    | DROP COLUMN IDENTIFIER SEMICOLON
    | RENAME COLUMN IDENTIFIER TO IDENTIFIER SEMICOLON
    | ALTER COLUMN IDENTIFIER alterColumnAction* SEMICOLON
    | ADD indexDef SEMICOLON
    | DROP INDEX IDENTIFIER SEMICOLON
    | ADD constraintDef SEMICOLON
    | DROP CONSTRAINT IDENTIFIER SEMICOLON
    ;

alterColumnAction
    : TYPE typeReference
    | SET DEFAULT expression
    | DROP DEFAULT
    | SET NULLABLE
    | SET NOT NULLABLE
    ;

transformStep
    : TRANSFORM identifierReference LBRACE transformAction* RBRACE
    ;

transformAction
    : SET IDENTIFIER EQ expression SEMICOLON
    | UPDATE SET transformAssignment (COMMA transformAssignment)* whereClause? SEMICOLON
    ;

transformAssignment
    : IDENTIFIER EQ expression
    ;

// ============================================================
// Seed Data Definition
// ============================================================

seedDef
    : SEED IDENTIFIER FOR identifierReference LBRACE seedBody RBRACE
    ;

seedBody
    : INSERT LPAREN fieldRefList RPAREN VALUES seedRow (COMMA seedRow)* SEMICOLON?
    ;

seedRow
    : LPAREN expression (COMMA expression)* RPAREN
    ;

