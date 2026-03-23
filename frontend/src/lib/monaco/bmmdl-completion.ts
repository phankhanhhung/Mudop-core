import * as monaco from 'monaco-editor'

const structureKeywords = [
  'module', 'namespace', 'entity', 'type', 'enum', 'aspect', 'service',
  'action', 'function', 'event', 'rule', 'access', 'control',
  'projection', 'index', 'constraint', 'sequence', 'context',
  'define', 'view', 'migration',
]

const modifierKeywords = [
  'key', 'virtual', 'computed', 'localized', 'abstract', 'sealed',
  'immutable', 'readonly', 'unique', 'stored',
]

const relationKeywords = [
  'association', 'composition', 'of', 'to', 'on', 'many', 'one',
  'extend', 'using', 'as', 'with', 'excluding',
  'imports', 'publishes', 'depends', 'returns', 'extends',
]

const accessKeywords = [
  'grant', 'deny', 'restrict', 'for', 'before', 'after',
  'create', 'read', 'update', 'delete', 'write', 'execute', 'all',
  'validate', 'compute', 'message', 'severity',
  'error', 'warning', 'info',
  'role', 'authenticated', 'anonymous', 'user',
]

const controlKeywords = [
  'if', 'then', 'else', 'when', 'case', 'end',
]

const operatorKeywords = [
  'and', 'or', 'not', 'in', 'between', 'like', 'is', 'exists',
  'null', 'true', 'false',
]

const metadataKeywords = [
  'author', 'description', 'version', 'start', 'increment',
  'pattern', 'padding', 'reset',
]

const allKeywords = [
  ...structureKeywords,
  ...modifierKeywords,
  ...relationKeywords,
  ...accessKeywords,
  ...controlKeywords,
  ...operatorKeywords,
  ...metadataKeywords,
]

const typeNames = [
  'String', 'Integer', 'Decimal', 'Boolean', 'Date', 'Time',
  'DateTime', 'Timestamp', 'UUID', 'Binary', 'array', 'table',
  'FileReference',
]

const builtinFunctions = [
  'count', 'sum', 'avg', 'min', 'max', 'first', 'last',
  'stddev', 'variance',
  'cast', 'coalesce', 'ifnull', 'nullif', 'decode',
  'concat', 'substring', 'upper', 'lower', 'trim', 'length', 'replace',
  'abs', 'ceil', 'floor', 'round', 'mod', 'power', 'sqrt',
  'year', 'month', 'day', 'hour', 'minute', 'second',
  'current_date', 'current_time', 'current_timestamp',
  'to_integer', 'to_decimal', 'to_string', 'to_date', 'to_time', 'to_timestamp',
  'nextSequence', 'currentSequence', 'formatSequence',
]

const commonAnnotations = [
  'TenantScoped', 'Temporal', 'Temporal.ValidTime', 'Temporal.Strategy',
  'Storage.Provider', 'Storage.Bucket', 'Storage.MaxSize', 'Storage.AllowedTypes',
  'OData.Singleton', 'OData.ReadOnly',
  'UI.Label', 'UI.Hidden', 'UI.ReadOnly',
  'Semantics.Email', 'Semantics.Telephone', 'Semantics.URL',
  'Assert.Range', 'Assert.Format',
]

export function registerBmmdlCompletion() {
  monaco.languages.registerCompletionItemProvider('bmmdl', {
    triggerCharacters: ['@', '#', '.', '$'],

    provideCompletionItems(model, position) {
      const word = model.getWordUntilPosition(position)
      const range: monaco.IRange = {
        startLineNumber: position.lineNumber,
        endLineNumber: position.lineNumber,
        startColumn: word.startColumn,
        endColumn: word.endColumn,
      }

      // Check if we are inside an annotation context (character before word is @)
      const lineContent = model.getLineContent(position.lineNumber)
      const charBeforeWord = lineContent.substring(0, word.startColumn - 1)

      if (charBeforeWord.trimEnd().endsWith('@')) {
        const atRange: monaco.IRange = {
          startLineNumber: position.lineNumber,
          endLineNumber: position.lineNumber,
          startColumn: word.startColumn,
          endColumn: word.endColumn,
        }
        return {
          suggestions: commonAnnotations.map((a) => ({
            label: `@${a}`,
            kind: monaco.languages.CompletionItemKind.Property,
            insertText: a,
            range: atRange,
            detail: 'Annotation',
            sortText: `0_${a}`,
          })),
        }
      }

      const suggestions: monaco.languages.CompletionItem[] = []

      // Keywords
      for (const kw of allKeywords) {
        suggestions.push({
          label: kw,
          kind: monaco.languages.CompletionItemKind.Keyword,
          insertText: kw,
          range,
          sortText: `2_${kw}`,
        })
      }

      // Types
      for (const t of typeNames) {
        suggestions.push({
          label: t,
          kind: monaco.languages.CompletionItemKind.TypeParameter,
          insertText: t,
          range,
          detail: 'Built-in type',
          sortText: `1_${t}`,
        })
      }

      // Built-in functions
      for (const fn of builtinFunctions) {
        suggestions.push({
          label: fn,
          kind: monaco.languages.CompletionItemKind.Function,
          insertText: `${fn}($0)`,
          insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
          range,
          detail: 'Built-in function',
          sortText: `3_${fn}`,
        })
      }

      // --- Snippets ---

      suggestions.push({
        label: 'module (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          "module ${1:MyModule} version '${2:1.0}' {",
          "\tauthor: '${3:Author}';",
          "\tdescription: '${4:Module description}';",
          '',
          '\tnamespace ${5:my.namespace} {',
          '\t\t$0',
          '\t}',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Module definition with namespace block',
        range,
        sortText: '0_module',
      })

      suggestions.push({
        label: 'entity (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'entity ${1:Name} {',
          '\tkey ID: UUID;',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Entity definition with UUID key field',
        range,
        sortText: '0_entity',
      })

      suggestions.push({
        label: 'enum (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'enum ${1:Name} {',
          '\t${2:Value1} = 1;',
          '\t${3:Value2} = 2;',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Enum type definition with integer values',
        range,
        sortText: '0_enum',
      })

      suggestions.push({
        label: 'service (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'service ${1:Name}Service {',
          '\tentity ${2:Entities} as ${3:Entity};',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Service definition with entity exposure',
        range,
        sortText: '0_service',
      })

      suggestions.push({
        label: 'rule (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'rule ${1:RuleName} for ${2:Entity} on before ${3|create,update,delete|} {',
          "\tvalidate ${4:expression} message '${5:Validation failed}' severity ${6|error,warning,info|};",
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Business rule with validation',
        range,
        sortText: '0_rule',
      })

      suggestions.push({
        label: 'access control (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'access control for ${1:Entity} {',
          '\tgrant read to authenticated;',
          "\tgrant create, update to role '${2:Editor}';",
          "\tgrant delete to role '${3:Admin}';",
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Access control definition with role-based grants',
        range,
        sortText: '0_access',
      })

      suggestions.push({
        label: 'aspect (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'aspect ${1:Name} {',
          '\t${2:field}: ${3:String(100)};',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Aspect (mixin) definition with reusable fields',
        range,
        sortText: '0_aspect',
      })

      suggestions.push({
        label: 'association (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: '${1:name}: association [${2|0..1,1..1,*,1..*|}] to ${3:TargetEntity};',
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Association to another entity',
        range,
        sortText: '0_association',
      })

      suggestions.push({
        label: 'composition (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: '${1:name}: composition [${2|*,1..*|}] of ${3:ChildEntity};',
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Composition (owned child entities)',
        range,
        sortText: '0_composition',
      })

      suggestions.push({
        label: 'sequence (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'sequence ${1:Name} for ${2:Entity}.${3:field} {',
          "\tpattern: '${4:{YYYY}-{SEQ}}';",
          '\tstart: ${5:1};',
          '\tincrement: ${6:1};',
          '\tpadding: ${7:5};',
          '\treset on ${8|yearly,monthly,daily,never|};',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Sequence number generator definition',
        range,
        sortText: '0_sequence',
      })

      suggestions.push({
        label: 'action (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'action ${1:name}(${2:param}: ${3:UUID}) returns ${4:Entity} {',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Action definition with parameters and return type',
        range,
        sortText: '0_action',
      })

      suggestions.push({
        label: 'function (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'function ${1:name}(${2:param}: ${3:UUID}) returns ${4:Entity} {',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Function definition (read-only, no side effects)',
        range,
        sortText: '0_function',
      })

      suggestions.push({
        label: 'event (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'event ${1:Name} {',
          '\t${2:field}: ${3:UUID};',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Domain event definition',
        range,
        sortText: '0_event',
      })

      suggestions.push({
        label: 'namespace (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'namespace ${1:my.namespace} {',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Namespace block',
        range,
        sortText: '0_namespace',
      })

      suggestions.push({
        label: 'type struct (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: [
          'type ${1:Name}: {',
          '\t${2:field}: ${3:String(100)};',
          '\t$0',
          '}',
        ].join('\n'),
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Structured type definition',
        range,
        sortText: '0_type',
      })

      suggestions.push({
        label: 'type alias (snippet)',
        kind: monaco.languages.CompletionItemKind.Snippet,
        insertText: 'type ${1:Name}: ${2:String(100)};',
        insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
        documentation: 'Type alias definition',
        range,
        sortText: '0_type_alias',
      })

      return { suggestions }
    },
  })
}
