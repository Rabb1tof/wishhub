#!/bin/bash
# =============================================
# Sync root .env to frontend/.env.production
# For production builds
# =============================================

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "🔄 Syncing environment variables..."

# Check if root .env exists
if [ ! -f "$ROOT_DIR/.env" ]; then
    echo "❌ Error: $ROOT_DIR/.env not found"
    echo "   Copy .env.example to .env and fill in your values"
    exit 1
fi

# Source the root .env
set -a
source "$ROOT_DIR/.env"
set +a

# Create frontend .env.production from template
cat > "$ROOT_DIR/frontend/.env.production" << EOF
# =============================================
# Production Environment Variables
# Auto-generated from root .env via sync-env.sh
# Do not edit manually - edit root .env instead
# =============================================

# API URLs (relative for same-domain deployment)
VITE_API_URL=/api
VITE_HUB_URL=/hubs

# Domain info from root .env
VITE_PUBLIC_URL=${PUBLIC_URL:-https://your_domain.com}
EOF

echo "✅ Synced .env → frontend/.env.production"
echo "   PUBLIC_URL: ${PUBLIC_URL:-https://your_domain.com}"
