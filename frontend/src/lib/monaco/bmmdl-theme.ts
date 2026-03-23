import * as monaco from 'monaco-editor'

export function registerBmmdlThemes() {
  monaco.editor.defineTheme('bmmdl-light', {
    base: 'vs',
    inherit: true,
    rules: [
      // Structure keywords (blue, bold)
      { token: 'keyword.bmmdl', foreground: '0000FF', fontStyle: 'bold' },
      { token: 'keyword.control.bmmdl', foreground: '0000FF', fontStyle: 'bold' },
      { token: 'keyword.metadata.bmmdl', foreground: '4B5563' },

      // Modifiers (purple)
      { token: 'modifier.bmmdl', foreground: '7C3AED', fontStyle: 'bold' },

      // Types (cyan/teal)
      { token: 'type.bmmdl', foreground: '0891B2', fontStyle: 'bold' },

      // Relationship keywords (amber/orange)
      { token: 'relation.bmmdl', foreground: 'D97706' },

      // Access/rule keywords (green)
      { token: 'access.bmmdl', foreground: '059669' },

      // Annotations (gray, italic)
      { token: 'annotation.bmmdl', foreground: '6B7280', fontStyle: 'italic' },

      // Enum hash literals (pink)
      { token: 'enum-literal.bmmdl', foreground: 'BE185D' },

      // Strings
      { token: 'string.bmmdl', foreground: 'A21CAF' },
      { token: 'string.escape.bmmdl', foreground: 'C026D3' },
      { token: 'string.invalid.bmmdl', foreground: 'DC2626' },

      // Numbers
      { token: 'number.bmmdl', foreground: '0D9488' },
      { token: 'number.float.bmmdl', foreground: '0D9488' },
      { token: 'number.hex.bmmdl', foreground: '0D9488' },
      { token: 'number.cardinality.bmmdl', foreground: '0D9488' },

      // Comments (gray, italic)
      { token: 'comment.bmmdl', foreground: '6B7280', fontStyle: 'italic' },
      { token: 'comment.doc.bmmdl', foreground: '4B5563', fontStyle: 'italic' },

      // Operators
      { token: 'operator.bmmdl', foreground: '374151' },
      { token: 'operator.word.bmmdl', foreground: '374151', fontStyle: 'bold' },

      // Built-in literals (null, true, false)
      { token: 'constant.language.bmmdl', foreground: '0000FF' },

      // Built-in functions
      { token: 'predefined.function.bmmdl', foreground: '7C2D12' },

      // Context variables ($user, $now)
      { token: 'variable.predefined.bmmdl', foreground: 'B45309' },

      // Identifiers
      { token: 'identifier.bmmdl', foreground: '1F2937' },

      // Delimiters
      { token: 'delimiter.bmmdl', foreground: '374151' },
    ],
    colors: {},
  })

  monaco.editor.defineTheme('bmmdl-dark', {
    base: 'vs-dark',
    inherit: true,
    rules: [
      // Structure keywords (blue, bold)
      { token: 'keyword.bmmdl', foreground: '569CD6', fontStyle: 'bold' },
      { token: 'keyword.control.bmmdl', foreground: '569CD6', fontStyle: 'bold' },
      { token: 'keyword.metadata.bmmdl', foreground: '9CA3AF' },

      // Modifiers (purple)
      { token: 'modifier.bmmdl', foreground: 'C084FC', fontStyle: 'bold' },

      // Types (cyan)
      { token: 'type.bmmdl', foreground: '22D3EE', fontStyle: 'bold' },

      // Relationship keywords (amber/yellow)
      { token: 'relation.bmmdl', foreground: 'FBBF24' },

      // Access/rule keywords (green)
      { token: 'access.bmmdl', foreground: '34D399' },

      // Annotations (gray, italic)
      { token: 'annotation.bmmdl', foreground: '9CA3AF', fontStyle: 'italic' },

      // Enum hash literals (pink)
      { token: 'enum-literal.bmmdl', foreground: 'F472B6' },

      // Strings
      { token: 'string.bmmdl', foreground: 'CE9178' },
      { token: 'string.escape.bmmdl', foreground: 'D7BA7D' },
      { token: 'string.invalid.bmmdl', foreground: 'F44747' },

      // Numbers
      { token: 'number.bmmdl', foreground: 'B5CEA8' },
      { token: 'number.float.bmmdl', foreground: 'B5CEA8' },
      { token: 'number.hex.bmmdl', foreground: 'B5CEA8' },
      { token: 'number.cardinality.bmmdl', foreground: 'B5CEA8' },

      // Comments (green, italic)
      { token: 'comment.bmmdl', foreground: '6A9955', fontStyle: 'italic' },
      { token: 'comment.doc.bmmdl', foreground: '608B4E', fontStyle: 'italic' },

      // Operators
      { token: 'operator.bmmdl', foreground: 'D4D4D4' },
      { token: 'operator.word.bmmdl', foreground: 'D4D4D4', fontStyle: 'bold' },

      // Built-in literals (null, true, false)
      { token: 'constant.language.bmmdl', foreground: '569CD6' },

      // Built-in functions
      { token: 'predefined.function.bmmdl', foreground: 'DCDCAA' },

      // Context variables ($user, $now)
      { token: 'variable.predefined.bmmdl', foreground: 'FCD34D' },

      // Identifiers
      { token: 'identifier.bmmdl', foreground: 'D4D4D4' },

      // Delimiters
      { token: 'delimiter.bmmdl', foreground: 'D4D4D4' },
    ],
    colors: {},
  })
}
