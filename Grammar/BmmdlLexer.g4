/**
 * BMMDL Lexer Grammar
 * Business Meta Model Definition Language
 * Version: 3.0.0
 * 
 * This lexer defines all tokens for the BMMDL DSL,
 * which is comparable to SAP CDS for defining business meta models.
 */
lexer grammar BmmdlLexer;

// ============================================================
// CHANNELS
// ============================================================

channels { COMMENTS_CHANNEL, DOCUMENTATION_CHANNEL }

// ============================================================
// UTF-8 BOM HANDLING
// ============================================================

// Skip UTF-8 BOM (Byte Order Mark) if present at start of file
// This allows files saved with Encoding.UTF8 (which includes BOM) to be parsed
BOM : '\uFEFF' -> skip;

// ============================================================
// FRAGMENTS
// ============================================================

fragment DIGIT      : [0-9];
fragment LETTER     : [a-zA-Z];
fragment HEX_DIGIT  : [0-9a-fA-F];
fragment ESC_SEQ    : '\\' [btnfr"'\\];

// ============================================================
// KEYWORDS - Definition
// ============================================================

ABSTRACT        : 'abstract';
ACTION          : 'action';
ADD             : 'add';
ANNOTATE        : 'annotate';
AS              : 'as';
ASPECT          : 'aspect';
ASSOCIATION     : 'association';
BINARY          : 'binary';
BOOLEAN         : 'boolean';
CHANGE          : 'change';
COMPOSITION     : 'composition';
COMPUTED        : 'computed';
CONSTRAINT      : 'constraint';
CONTEXT         : 'context';
DATE            : 'date';
DATETIME        : 'datetime';
DECIMAL         : 'decimal';
DEFAULT         : 'default';
DELETE          : 'delete';
ELSE            : 'else';
END             : 'end';
ENTITY          : 'entity';
ENUM            : 'enum';
EXCLUDING       : 'excluding';
EXTEND          : 'extend';
FALSE           : 'false';
FUNCTION        : 'function';
// IF: reserved — grammar uses WHEN/THEN for conditionals
// IF              : 'if';
IMMUTABLE       : 'immutable';
INTEGER         : 'integer';
KEY             : 'key';
LOCALIZED       : 'localized';
// MANY: reserved — not used in parser
// MANY            : 'many';
MODIFY          : 'modify';
NAMESPACE       : 'namespace';
NULL            : 'null';
OF              : 'of';
ON              : 'on';
// ONE: reserved — not used in parser
// ONE             : 'one';
READONLY        : 'readonly';
REMOVE          : 'remove';
RENAME          : 'rename';
RETURNS         : 'returns';
// SEALED: reserved — not used in parser
// SEALED          : 'sealed';
SERVICE         : 'service';
STRING          : 'string';
THEN            : 'then';
TIME            : 'time';
TIMESTAMP       : 'timestamp';
TO              : 'to';
TRUE            : 'true';
TYPE            : 'type';
UNIQUE          : 'unique';
USING           : 'using';
UUID            : 'uuid';
VIRTUAL         : 'virtual';
WHEN            : 'when';
WITH            : 'with';
ARRAY           : 'array' | 'Array';
// TABLE: reserved — tableDef uses VIEW keyword
// TABLE           : 'table';
EXTENDS         : 'extends';

// ============================================================
// KEYWORDS - Module System
// ============================================================

MODULE          : 'module';
PUBLISHES       : 'publishes';
IMPORTS         : 'imports';

// ============================================================
// KEYWORDS - Advanced Features (Phase 7)
// ============================================================

// Entity Enhancements
INDEX           : 'index';
STORED          : 'stored';
CHECK           : 'check';
FOREIGN         : 'foreign';
REFERENCES      : 'references';
COMPOSABLE      : 'composable';
PROJECTION      : 'projection';

// Behavior Features
EVENT           : 'event';
EMITS           : 'emits';
REQUIRES        : 'requires';
ENSURES         : 'ensures';
MODIFIES        : 'modifies';

// Data Management
TEMPORAL        : 'temporal';
// HISTORY, VALID, TRANSACTION: reserved for future temporal syntax — not used in parser
// HISTORY         : 'history';
// VALID           : 'valid';
// TRANSACTION     : 'transaction';

// Temporal Query Keywords (Phase 8: Bitemporal)
ASOF            : 'asof';
VERSIONS        : 'versions';
// PERIOD: reserved for future temporal syntax — not used in parser
// PERIOD          : 'period';
OVERLAPS        : 'overlaps';
CONTAINS        : 'contains';
PRECEDES        : 'precedes';
MEETS           : 'meets';

// Inheritance
// JOINED: reserved for joined-table inheritance — not used in parser
// JOINED          : 'joined';

// ============================================================
// KEYWORDS - Migration DSL
// ============================================================

MIGRATION       : 'migration';
ROLLBACK        : 'rollback';
TRANSFORM       : 'transform';
DEPENDS         : 'depends';
VERSION         : 'version';
AUTHOR          : 'author';
DESCRIPTION     : 'description';
BREAKING        : 'breaking';
UP              : 'up';
DOWN            : 'down';
SET             : 'set';
DROP            : 'drop';
ALTER           : 'alter';
COLUMN          : 'column';
NULLABLE        : 'nullable';

// ============================================================
// KEYWORDS - Seed Data DSL
// ============================================================

SEED            : 'seed';
INSERT          : 'insert';
VALUES          : 'values';

// ============================================================
// KEYWORDS - Access Control
// ============================================================

ACCESS          : 'access';
CONTROL         : 'control';
FOR             : 'for';
GRANT           : 'grant';
DENY            : 'deny';
RESTRICT        : 'restrict';
ROLE            : 'role';
USER            : 'user';
AUTHENTICATED   : 'authenticated';
ANONYMOUS       : 'anonymous';
VISIBLE         : 'visible';
MASKED          : 'masked';
HIDDEN_KW       : 'hidden';
ALL             : 'all';
READ            : 'read';
WRITE           : 'write';
CREATE          : 'create';
UPDATE          : 'update';
EXECUTE         : 'execute';
FIELDS          : 'fields';

// ============================================================
// KEYWORDS - Business Rules
// ============================================================

RULE            : 'rule';
BEFORE          : 'before';
AFTER           : 'after';
VALIDATE        : 'validate';
COMPUTE         : 'compute';
MESSAGE         : 'message';
SEVERITY        : 'severity';
ERROR           : 'error';
WARNING         : 'warning';
INFO            : 'info';
// ABORT: reserved — not used in parser
// ABORT           : 'abort';
RAISE           : 'raise';
REJECT          : 'reject';
// LOG: reserved — not used in parser
// LOG             : 'log';
CALL            : 'call';

// Action/Function Body Keywords
EMIT            : 'emit';
FOREACH         : 'foreach';
RETURN          : 'return';
LET             : 'let';

// ============================================================
// KEYWORDS - Expression / SQL
// ============================================================

AND             : 'and';
OR              : 'or';
NOT             : 'not';
IN              : 'in';
BETWEEN         : 'between';
LIKE            : 'like';
IS              : 'is';
EXISTS          : 'exists';
CASE            : 'case';

// Aggregate Functions
SUM             : 'sum';
AVG             : 'avg';
COUNT           : 'count';
MIN             : 'min';
MAX             : 'max';
FIRST           : 'first';
LAST            : 'last';
STDDEV          : 'stddev';
VARIANCE        : 'variance';

// Window Functions
OVER            : 'over';
PARTITION       : 'partition';
ORDER           : 'order';
BY              : 'by';
ROWS            : 'rows';
RANGE           : 'range';
UNBOUNDED       : 'unbounded';
PRECEDING       : 'preceding';
FOLLOWING       : 'following';
CURRENT         : 'current';
ROW             : 'row';
ROW_NUMBER      : 'row_number';
RANK            : 'rank';
DENSE_RANK      : 'dense_rank';
NTILE           : 'ntile';
LAG             : 'lag';
LEAD            : 'lead';
FIRST_VALUE     : 'first_value';
LAST_VALUE      : 'last_value';

// ============================================================
// KEYWORDS - View Definition
// ============================================================

DEFINE          : 'define';
VIEW            : 'view';
SELECT          : 'select';
FROM            : 'from';
WHERE           : 'where';
GROUP           : 'group';
HAVING          : 'having';
JOIN            : 'join';
INNER           : 'inner';
LEFT            : 'left';
RIGHT           : 'right';
FULL            : 'full';
OUTER           : 'outer';
CROSS           : 'cross';
UNION           : 'union';
INTERSECT       : 'intersect';
EXCEPT          : 'except';
DISTINCT        : 'distinct';
ASC             : 'asc';
DESC            : 'desc';
NULLS           : 'nulls';
PARAMETERS      : 'parameters';

// SQL Functions
CAST            : 'cast';
COALESCE        : 'coalesce';
IFNULL          : 'ifnull';
NULLIF          : 'nullif';
DECODE          : 'decode';
CONCAT          : 'concat';
SUBSTRING       : 'substring';
UPPER           : 'upper';
LOWER           : 'lower';
TRIM            : 'trim';
LTRIM           : 'ltrim';
RTRIM           : 'rtrim';
LENGTH          : 'length';
REPLACE         : 'replace';
INSTR           : 'instr';
LPAD            : 'lpad';
RPAD            : 'rpad';
ABS             : 'abs';
CEIL            : 'ceil';
FLOOR           : 'floor';
ROUND           : 'round';
TRUNC           : 'trunc';
MOD             : 'mod';
POWER           : 'power';
SQRT            : 'sqrt';
SIGN            : 'sign';
YEAR            : 'year';
MONTH           : 'month';
DAY             : 'day';
HOUR            : 'hour';
MINUTE          : 'minute';
SECOND          : 'second';
DAYOFWEEK       : 'dayofweek';
WEEKOFYEAR      : 'weekofyear';
ADD_DAYS        : 'add_days';
ADD_MONTHS      : 'add_months';
ADD_YEARS       : 'add_years';
DATEDIFF        : 'datediff';
CURRENT_DATE    : 'current_date';
CURRENT_TIME    : 'current_time';
CURRENT_TIMESTAMP : 'current_timestamp';
// NOW and TODAY removed - use $now and $today context variables instead
TO_INTEGER      : 'to_integer';
TO_DECIMAL      : 'to_decimal';
TO_STRING       : 'to_string';
TO_DATE         : 'to_date';
TO_TIME         : 'to_time';
TO_TIMESTAMP    : 'to_timestamp';
FORMAT          : 'format';
CURRENCY_CONVERSION : 'currency_conversion';
UNIT_CONVERSION : 'unit_conversion';

// ============================================================
// KEYWORDS - Multi-tenant
// ============================================================

TENANT          : 'tenant';
COMPANY         : 'company';
// SESSION: reserved — not used in scopeLevel or parser
// SESSION         : 'session';
SCOPE           : 'scope';
GLOBAL          : 'global';
// SHARED: reserved — not used in parser
// SHARED          : 'shared';
AWARE           : 'aware';

// ============================================================
// KEYWORDS - Sequence
// ============================================================

SEQUENCE        : 'sequence';
PATTERN         : 'pattern';
START           : 'start';
INCREMENT       : 'increment';
PADDING         : 'padding';
RESET           : 'reset';
NEVER           : 'never';
DAILY           : 'daily';
MONTHLY         : 'monthly';
YEARLY          : 'yearly';
FISCAL          : 'fiscal';
// REF: reserved — not used in parser
// REF             : 'ref';
NEXT_SEQUENCE   : 'nextSequence';
CURRENT_SEQUENCE: 'currentSequence';
RESET_SEQUENCE  : 'resetSequence';
SET_SEQUENCE    : 'setSequence';
FORMAT_SEQUENCE : 'formatSequence';
PAD_LEFT        : 'padLeft';
PAD_RIGHT       : 'padRight';
FISCAL_YEAR     : 'fiscalYear';
FISCAL_PERIOD   : 'fiscalPeriod';

// ============================================================
// OPERATORS
// ============================================================

COLON           : ':';
SEMICOLON       : ';';
COMMA           : ',';
DOT             : '.';
LBRACE          : '{';
RBRACE          : '}';
LBRACKET        : '[';
RBRACKET        : ']';
LPAREN          : '(';
RPAREN          : ')';
AT              : '@';
HASH            : '#';
DOLLAR          : '$';

// Comparison Operators
EQ              : '=';
NEQ             : '!=' | '<>';
LT              : '<';
GT              : '>';
LTE             : '<=';
GTE             : '>=';

// Arithmetic Operators
PLUS            : '+';
MINUS           : '-';
STAR            : '*';
SLASH           : '/';
PERCENT         : '%';

// Logical/Other Operators
// ARROW: reserved — not used in parser
// ARROW           : '->';
// Reserved for future use
// DOUBLE_ARROW    : '=>';
QUESTION        : '?';
DOUBLE_PIPE     : '||';
// AMPERSAND       : '&';
// PIPE            : '|';
// CARET           : '^';
// TILDE           : '~';
// EXCLAIM         : '!';
// DOUBLE_COLON    : '::';

// ============================================================
// LITERALS
// ============================================================

// String Literal
STRING_LITERAL
    : '\'' ( ~['\r\n\\] | ESC_SEQ | '\'\'' )* '\''
    ;

// Integer Literal
INTEGER_LITERAL
    : DIGIT+
    ;

// Decimal Literal
DECIMAL_LITERAL
    : DIGIT+ '.' DIGIT+
    | '.' DIGIT+
    ;

// Reserved for future use
// HEX_LITERAL
//     : '0' [xX] HEX_DIGIT+
//     ;

// ============================================================
// IDENTIFIERS
// ============================================================

// Standard Identifier
IDENTIFIER
    : LETTER (LETTER | DIGIT | '_')*
    ;

// Reserved for future use
// DELIMITED_ID
//     : '![' ( ~[\]\\] | '\\' . )* ']'
//     ;

// Reserved for future use
// QUOTED_ID
//     : '"' ( ~["\r\n\\] | ESC_SEQ )* '"'
//     ;

// ============================================================
// COMMENTS
// ============================================================

// Single-line comment
LINE_COMMENT
    : '//' ~[\r\n]* -> channel(COMMENTS_CHANNEL)
    ;

// Block comment
BLOCK_COMMENT
    : '/*' ( BLOCK_COMMENT | . )*? '*/' -> channel(COMMENTS_CHANNEL)
    ;

// Documentation comment (preserved for annotations)
DOC_COMMENT
    : '/**' .*? '*/' -> channel(DOCUMENTATION_CHANNEL)
    ;

// ============================================================
// WHITESPACE
// ============================================================

WS
    : [ \t\r\n\u000C]+ -> skip
    ;

// ============================================================
// CATCH-ALL (for error handling)
// ============================================================

UNEXPECTED_CHAR
    : .
    ;
