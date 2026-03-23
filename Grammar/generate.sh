#!/bin/bash
# BMMDL Grammar - ANTLR4 Code Generator Script
# Prerequisites: Java Runtime (JRE 11+)

echo "========================================"
echo "BMMDL Grammar - C# Code Generator"
echo "========================================"

# Set paths
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
GRAMMAR_DIR="$SCRIPT_DIR"
ANTLR_JAR="$SCRIPT_DIR/../tools/antlr-4.13.1-complete.jar"
OUTPUT_DIR="$SCRIPT_DIR/../src/BMMDL.Parser/Generated"

# Check if Java is available
if ! command -v java &> /dev/null; then
    echo "ERROR: Java is not installed"
    echo "Please install Java from https://adoptium.net/"
    exit 1
fi

# Check if ANTLR jar exists
if [ ! -f "$ANTLR_JAR" ]; then
    echo "ERROR: ANTLR jar not found at $ANTLR_JAR"
    echo "Downloading..."
    curl -L -o "$ANTLR_JAR" https://www.antlr.org/download/antlr-4.13.1-complete.jar
fi

# Create output directory
mkdir -p "$OUTPUT_DIR"

echo ""
echo "Generating C# code from grammar files..."
echo ""

java -jar "$ANTLR_JAR" \
    -Dlanguage=CSharp \
    -visitor \
    -no-listener \
    -package BMMDL.Parser.Generated \
    -o "$OUTPUT_DIR" \
    "$GRAMMAR_DIR/BmmdlLexer.g4" \
    "$GRAMMAR_DIR/BmmdlParser.g4"

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================"
    echo "SUCCESS! Generated files:"
    echo "========================================"
    ls -la "$OUTPUT_DIR"/*.cs
    echo ""
    echo "Files are in: $OUTPUT_DIR"
else
    echo ""
    echo "ERROR: Generation failed!"
    exit 1
fi
