import * as monaco from 'monaco-editor'

export function registerBmmdlLanguage() {
  monaco.languages.register({ id: 'bmmdl' })

  monaco.languages.setLanguageConfiguration('bmmdl', {
    comments: {
      lineComment: '//',
      blockComment: ['/*', '*/'],
    },
    brackets: [
      ['{', '}'],
      ['[', ']'],
      ['(', ')'],
    ],
    autoClosingPairs: [
      { open: '{', close: '}' },
      { open: '[', close: ']' },
      { open: '(', close: ')' },
      { open: "'", close: "'", notIn: ['string'] },
      { open: '"', close: '"', notIn: ['string'] },
    ],
    surroundingPairs: [
      { open: '{', close: '}' },
      { open: '[', close: ']' },
      { open: '(', close: ')' },
      { open: "'", close: "'" },
      { open: '"', close: '"' },
    ],
    folding: {
      markers: {
        start: /\{/,
        end: /\}/,
      },
    },
    indentationRules: {
      increaseIndentPattern: /\{[^}]*$/,
      decreaseIndentPattern: /^\s*\}/,
    },
    wordPattern: /(-?\d*\.\d\w*)|([^\`\~\!\@\#\%\^\&\*\(\)\-\=\+\[\{\]\}\\\|\;\:\'\"\,\.\<\>\/\?\s]+)/g,
  })

  monaco.languages.setMonarchTokensProvider('bmmdl', {
    defaultToken: 'invalid',
    tokenPostfix: '.bmmdl',

    keywords: [
      'module', 'namespace', 'entity', 'type', 'enum', 'aspect', 'service',
      'action', 'function', 'event', 'rule', 'access', 'control',
      'projection', 'index', 'constraint', 'sequence',
      'context', 'define', 'view', 'migration',
      'select', 'from', 'where', 'group', 'having', 'order', 'by',
      'join', 'inner', 'left', 'right', 'full', 'outer', 'cross',
      'union', 'intersect', 'except', 'distinct',
      'asc', 'desc', 'nulls',
      'modify', 'remove', 'rename', 'change', 'add', 'annotate',
      'alter', 'drop', 'column', 'check', 'foreign', 'references',
      'temporal', 'history', 'valid', 'transaction',
      'asof', 'versions', 'period', 'overlaps', 'contains', 'precedes', 'meets',
      'emit', 'foreach', 'return', 'let', 'raise', 'abort', 'log', 'call',
      'up', 'down', 'transform', 'rollback', 'breaking', 'nullable',
      'set', 'parameters',
    ],

    modifiers: [
      'key', 'virtual', 'computed', 'localized', 'abstract', 'sealed',
      'immutable', 'readonly', 'unique', 'stored',
    ],

    typeKeywords: [
      'String', 'string', 'Integer', 'integer', 'Decimal', 'decimal',
      'Boolean', 'boolean', 'Date', 'date', 'Time', 'time',
      'DateTime', 'datetime', 'Timestamp', 'timestamp',
      'UUID', 'uuid', 'Binary', 'binary',
      'array', 'table',
    ],

    relationKeywords: [
      'association', 'composition', 'of', 'to', 'on', 'many', 'one',
      'extend', 'using', 'as', 'with', 'excluding',
      'imports', 'publishes', 'depends', 'returns', 'extends',
      'emits', 'requires', 'ensures', 'modifies',
    ],

    accessKeywords: [
      'grant', 'deny', 'restrict', 'for', 'before', 'after',
      'create', 'read', 'update', 'delete', 'write', 'execute', 'all',
      'validate', 'compute', 'message', 'severity',
      'error', 'warning', 'info',
      'role', 'authenticated', 'anonymous', 'user',
      'visible', 'masked', 'hidden', 'fields',
      'scope', 'tenant', 'company', 'global', 'shared', 'aware',
    ],

    controlFlow: [
      'if', 'then', 'else', 'when', 'case', 'end',
    ],

    operators: [
      'and', 'or', 'not', 'in', 'between', 'like', 'is', 'exists',
    ],

    builtinLiterals: [
      'null', 'true', 'false',
    ],

    builtinFunctions: [
      'count', 'sum', 'avg', 'min', 'max', 'first', 'last',
      'stddev', 'variance',
      'cast', 'coalesce', 'ifnull', 'nullif', 'decode',
      'concat', 'substring', 'upper', 'lower', 'trim', 'ltrim', 'rtrim',
      'length', 'replace', 'instr', 'lpad', 'rpad',
      'abs', 'ceil', 'floor', 'round', 'trunc', 'mod', 'power', 'sqrt', 'sign',
      'year', 'month', 'day', 'hour', 'minute', 'second',
      'dayofweek', 'weekofyear', 'datediff',
      'add_days', 'add_months', 'add_years',
      'current_date', 'current_time', 'current_timestamp',
      'to_integer', 'to_decimal', 'to_string', 'to_date', 'to_time', 'to_timestamp',
      'format', 'currency_conversion', 'unit_conversion',
      'nextSequence', 'currentSequence', 'formatSequence',
      'resetSequence', 'setSequence',
      'padLeft', 'padRight', 'fiscalYear', 'fiscalPeriod',
      'row_number', 'rank', 'dense_rank', 'ntile', 'lag', 'lead',
      'first_value', 'last_value',
    ],

    metadataKeywords: [
      'author', 'description', 'version', 'start', 'increment',
      'pattern', 'padding', 'reset',
      'never', 'daily', 'monthly', 'yearly', 'fiscal',
    ],

    symbols: /[=><!~?:&|+\-*\/\^%]+/,

    escapes: /\\(?:[abfnrtv\\"']|x[0-9A-Fa-f]{1,4}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})/,

    tokenizer: {
      root: [
        // Whitespace
        { include: '@whitespace' },

        // Annotations: @Name or @Name.SubName (including dotted paths)
        [/@[a-zA-Z_][\w]*(?:\.[a-zA-Z_][\w]*)*/, 'annotation'],

        // Enum hash literals: #Active, #Inactive
        [/#[a-zA-Z_][\w]*/, 'enum-literal'],

        // Context variables: $user, $now, $tenant.id
        [/\$[a-zA-Z_][\w]*(?:\.[a-zA-Z_][\w]*)*/, 'variable.predefined'],

        // Cardinality brackets: [0..1], [*], [1..*], [0..*]
        [/\[\s*(?:\d+\s*(?:\.\.\s*(?:\d+|\*))?\s*|\*\s*)\]/, 'number.cardinality'],

        // Identifiers and keywords
        [/[a-zA-Z_]\w*/, {
          cases: {
            '@modifiers': 'modifier',
            '@typeKeywords': 'type.bmmdl',
            '@relationKeywords': 'relation',
            '@accessKeywords': 'access',
            '@controlFlow': 'keyword.control',
            '@operators': 'operator.word',
            '@builtinLiterals': 'constant.language',
            '@builtinFunctions': 'predefined.function',
            '@metadataKeywords': 'keyword.metadata',
            '@keywords': 'keyword',
            '@default': 'identifier',
          },
        }],

        // Numbers
        [/\d+\.\d+/, 'number.float'],
        [/0[xX][0-9a-fA-F]+/, 'number.hex'],
        [/\d+/, 'number'],

        // Delimiters and operators
        [/[{}()\[\]]/, '@brackets'],
        [/[<>](?!@symbols)/, '@brackets'],

        [/@symbols/, {
          cases: {
            '@default': 'operator',
          },
        }],

        // Delimiter: after number because of decimal separator
        [/[;,.]/, 'delimiter'],

        // Strings
        [/'([^'\\]|\\.)*$/, 'string.invalid'], // non-terminated single-quoted string
        [/"([^"\\]|\\.)*$/, 'string.invalid'], // non-terminated double-quoted string
        [/'/, 'string', '@stringSingle'],
        [/"/, 'string', '@stringDouble'],
      ],

      whitespace: [
        [/[ \t\r\n]+/, 'white'],
        [/\/\*\*(?!\/)/, 'comment.doc', '@docComment'],
        [/\/\*/, 'comment', '@blockComment'],
        [/\/\/.*$/, 'comment'],
      ],

      blockComment: [
        [/[^\/*]+/, 'comment'],
        [/\/\*/, 'comment', '@push'], // nested block comment
        [/\*\//, 'comment', '@pop'],
        [/[\/*]/, 'comment'],
      ],

      docComment: [
        [/[^\/*]+/, 'comment.doc'],
        [/\/\*/, 'comment.doc', '@push'], // nested
        [/\*\//, 'comment.doc', '@pop'],
        [/[\/*]/, 'comment.doc'],
      ],

      stringSingle: [
        [/[^\\']+/, 'string'],
        [/@escapes/, 'string.escape'],
        [/\\./, 'string.escape.invalid'],
        [/''/, 'string.escape'], // escaped single quote in BMMDL
        [/'/, 'string', '@pop'],
      ],

      stringDouble: [
        [/[^\\"]+/, 'string'],
        [/@escapes/, 'string.escape'],
        [/\\./, 'string.escape.invalid'],
        [/"/, 'string', '@pop'],
      ],
    },
  } as monaco.languages.IMonarchLanguage)
}
