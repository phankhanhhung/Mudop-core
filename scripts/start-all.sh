#!/bin/bash
# start-all.sh — Start all BMMDL services in the correct order
# Usage: ./scripts/start-all.sh [--stop] [--restart]
#
# Services:
#   1. PostgreSQL (Docker container: bmmdl-postgres)
#   2. Registry API  — port 51742 (admin, compile, clear-db)
#   3. Runtime API   — port 5175  (auth, OData, plugins)
#   4. Frontend      — port 5173  (Vue + Vite)

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

LOG_DIR="/tmp/bmmdl-logs"
mkdir -p "$LOG_DIR"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[BMMDL]${NC} $1"; }
warn() { echo -e "${YELLOW}[BMMDL]${NC} $1"; }
err()  { echo -e "${RED}[BMMDL]${NC} $1"; }

stop_all() {
    log "Stopping all services..."
    pkill -f "BMMDL.Runtime.Api" 2>/dev/null && log "Runtime API stopped" || true
    pkill -f "BMMDL.Registry.Api" 2>/dev/null && log "Registry API stopped" || true
    pkill -f "vite.*frontend" 2>/dev/null && log "Frontend stopped" || true
    # Give processes time to exit
    sleep 2
    log "All services stopped."
}

# Handle --stop flag
if [[ "$1" == "--stop" ]]; then
    stop_all
    exit 0
fi

# Handle --restart flag
if [[ "$1" == "--restart" ]]; then
    stop_all
fi

# ── 1. PostgreSQL ────────────────────────────────────────────
log "Checking PostgreSQL..."
if docker ps --format '{{.Names}}' | grep -q "bmmdl-postgres"; then
    log "PostgreSQL is running (bmmdl-postgres)"
else
    warn "PostgreSQL container not running, starting..."
    docker start bmmdl-postgres 2>/dev/null || {
        err "Failed to start bmmdl-postgres container. Is Docker running?"
        exit 1
    }
    sleep 3
    log "PostgreSQL started"
fi

# Wait for PostgreSQL to accept connections
for i in $(seq 1 10); do
    if docker exec bmmdl-postgres pg_isready -U bmmdl -q 2>/dev/null; then
        break
    fi
    if [ "$i" -eq 10 ]; then
        err "PostgreSQL not ready after 10 seconds"
        exit 1
    fi
    sleep 1
done
log "PostgreSQL ready"

# ── 2. Registry API (port 51742) ────────────────────────────
if pgrep -f "BMMDL.Registry.Api" > /dev/null 2>&1; then
    log "Registry API already running"
else
    log "Starting Registry API (port 51742)..."
    nohup dotnet run --project "$ROOT_DIR/src/BMMDL.Registry.Api/BMMDL.Registry.Api.csproj" \
        > "$LOG_DIR/registry-api.log" 2>&1 &
    REGISTRY_PID=$!

    # Wait for Registry API to be ready (it creates schemas on startup)
    for i in $(seq 1 30); do
        if curl -sf http://localhost:51742/api/admin/health > /dev/null 2>&1; then
            break
        fi
        if ! kill -0 $REGISTRY_PID 2>/dev/null; then
            err "Registry API process died. Check $LOG_DIR/registry-api.log"
            exit 1
        fi
        if [ "$i" -eq 30 ]; then
            # Check if at least listening
            if grep -q "Now listening" "$LOG_DIR/registry-api.log" 2>/dev/null; then
                break
            fi
            warn "Registry API slow to start — check $LOG_DIR/registry-api.log"
            break
        fi
        sleep 1
    done
    log "Registry API started (PID: $REGISTRY_PID)"
fi

# ── 3. Runtime API (port 5175) ──────────────────────────────
if pgrep -f "BMMDL.Runtime.Api" > /dev/null 2>&1; then
    log "Runtime API already running"
else
    log "Starting Runtime API (port 5175)..."
    nohup dotnet run --project "$ROOT_DIR/src/BMMDL.Runtime.Api/BMMDL.Runtime.Api.csproj" \
        > "$LOG_DIR/runtime-api.log" 2>&1 &
    RUNTIME_PID=$!

    for i in $(seq 1 30); do
        if grep -q "Now listening" "$LOG_DIR/runtime-api.log" 2>/dev/null; then
            break
        fi
        if ! kill -0 $RUNTIME_PID 2>/dev/null; then
            err "Runtime API process died. Check $LOG_DIR/runtime-api.log"
            exit 1
        fi
        if [ "$i" -eq 30 ]; then
            warn "Runtime API slow to start — check $LOG_DIR/runtime-api.log"
        fi
        sleep 1
    done
    log "Runtime API started (PID: $RUNTIME_PID)"
fi

# ── 4. Frontend (port 5173) ─────────────────────────────────
if pgrep -f "vite" > /dev/null 2>&1; then
    log "Frontend already running"
else
    log "Starting Frontend (Vite)..."
    cd "$ROOT_DIR/frontend"
    nohup npm run dev > "$LOG_DIR/frontend.log" 2>&1 &
    FRONTEND_PID=$!

    for i in $(seq 1 15); do
        if grep -q "ready in" "$LOG_DIR/frontend.log" 2>/dev/null; then
            break
        fi
        if [ "$i" -eq 15 ]; then
            warn "Frontend slow to start — check $LOG_DIR/frontend.log"
        fi
        sleep 1
    done

    FRONTEND_PORT=$(grep -oP 'localhost:\K[0-9]+' "$LOG_DIR/frontend.log" | head -1)
    log "Frontend started (PID: $FRONTEND_PID)"
fi

# ── Summary ─────────────────────────────────────────────────
echo ""
log "All services running:"
echo "  Registry API : http://localhost:51742"
echo "  Runtime API  : http://localhost:5175"
FRONTEND_PORT=${FRONTEND_PORT:-5173}
echo "  Frontend     : http://localhost:$FRONTEND_PORT"
echo ""
echo "  Logs: $LOG_DIR/"
echo "    registry-api.log | runtime-api.log | frontend.log"
echo ""
log "Stop all: ./scripts/start-all.sh --stop"
