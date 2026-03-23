#!/usr/bin/env bash
#
# Build & test only the plugin subsystem.
#
# Usage:
#   ./scripts/test-plugins.sh              # build + run all plugin tests
#   ./scripts/test-plugins.sh --build-only # build only, no tests
#   ./scripts/test-plugins.sh --filter TenantIsolation  # run specific feature tests
#

set -euo pipefail

BOLD='\033[1m'
GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
RESET='\033[0m'

SOLUTION="BMMDL.sln"
TEST_PROJECT="src/BMMDL.Tests/BMMDL.Tests.Unit.csproj"
BASE_FILTER="FullyQualifiedName~BMMDL.Tests.Plugins"

BUILD_ONLY=false
EXTRA_FILTER=""

for arg in "$@"; do
    case "$arg" in
        --build-only) BUILD_ONLY=true ;;
        --filter)     shift_next=true ;;
        *)
            if [[ "${shift_next:-}" == "true" ]]; then
                EXTRA_FILTER="$arg"
                shift_next=false
            else
                EXTRA_FILTER="$arg"
            fi
            ;;
    esac
done

echo -e "${BOLD}${CYAN}══════════════════════════════════════════${RESET}"
echo -e "${BOLD}${CYAN}  BMMDL Plugin Subsystem — Build & Test${RESET}"
echo -e "${BOLD}${CYAN}══════════════════════════════════════════${RESET}"

# ── Build ────────────────────────────────────────────────────
echo -e "\n${CYAN}Building solution...${RESET}"
dotnet build "$SOLUTION" --verbosity quiet --nologo
echo -e "${GREEN}Build succeeded${RESET}"

if $BUILD_ONLY; then
    echo -e "\n${GREEN}Done (build only).${RESET}"
    exit 0
fi

# ── Test filter ──────────────────────────────────────────────
FILTER="$BASE_FILTER"
if [[ -n "$EXTRA_FILTER" ]]; then
    FILTER="${BASE_FILTER}&FullyQualifiedName~${EXTRA_FILTER}"
fi

echo -e "\n${CYAN}Running plugin tests...${RESET}"
echo -e "  Filter: ${FILTER}"
echo ""

dotnet test "$TEST_PROJECT" \
    --filter "$FILTER" \
    --no-build \
    --verbosity normal \
    --nologo

exit $?
