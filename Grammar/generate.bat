@echo off
REM BMMDL Grammar - ANTLR4 Code Generator Script
REM Prerequisites: Java Runtime (JRE 11+)

echo ========================================
echo BMMDL Grammar - C# Code Generator
echo ========================================

REM Check if Java is available
where java >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Java is not installed or not in PATH
    echo Please install Java from https://adoptium.net/
    exit /b 1
)

REM Set paths
set GRAMMAR_DIR=%~dp0
set ANTLR_JAR=%GRAMMAR_DIR%..\tools\antlr-4.13.1-complete.jar
set OUTPUT_DIR=%GRAMMAR_DIR%..\src\BMMDL.Parser\Generated

REM Check if ANTLR jar exists
if not exist "%ANTLR_JAR%" (
    echo ERROR: ANTLR jar not found at %ANTLR_JAR%
    echo Downloading...
    powershell -Command "Invoke-WebRequest -Uri 'https://www.antlr.org/download/antlr-4.13.1-complete.jar' -OutFile '%ANTLR_JAR%'"
)

REM Create output directory
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo.
echo Generating C# code from grammar files...
echo.

java -jar "%ANTLR_JAR%" ^
    -Dlanguage=CSharp ^
    -visitor ^
    -no-listener ^
    -package BMMDL.Parser.Generated ^
    -o "%OUTPUT_DIR%" ^
    "%GRAMMAR_DIR%BmmdlLexer.g4" ^
    "%GRAMMAR_DIR%BmmdlParser.g4"

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo SUCCESS! Generated files:
    echo ========================================
    dir /b "%OUTPUT_DIR%\*.cs"
    echo.
    echo Files are in: %OUTPUT_DIR%
) else (
    echo.
    echo ERROR: Generation failed!
    exit /b 1
)
