#!/usr/bin/env bash
# =============================================
# Manual local build & push to GHCR
# =============================================
# Use this when you need to ship a build without waiting for GitHub Actions.
# Usage:
#   IMAGE_OWNER=rabb1t IMAGE_TAG=manual-$(date +%Y%m%d-%H%M) ./scripts/build-and-push.sh
#
# Prereqs:
#   1. docker login ghcr.io   (use a GitHub Personal Access Token with write:packages)
#   2. Docker Buildx is enabled (default on Docker Desktop)
# =============================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

: "${IMAGE_OWNER:?Set IMAGE_OWNER (e.g. export IMAGE_OWNER=rabb1t)}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
PLATFORM="${PLATFORM:-linux/amd64}"

API_IMAGE="ghcr.io/${IMAGE_OWNER}/wishhub-api:${IMAGE_TAG}"
FRONTEND_IMAGE="ghcr.io/${IMAGE_OWNER}/wishhub-frontend:${IMAGE_TAG}"

echo "==> Building & pushing API image: ${API_IMAGE}"
docker buildx build \
  --platform "${PLATFORM}" \
  --file WishHub.Api/Dockerfile \
  --tag "${API_IMAGE}" \
  --cache-from "type=registry,ref=ghcr.io/${IMAGE_OWNER}/wishhub-api:buildcache" \
  --cache-to "type=registry,ref=ghcr.io/${IMAGE_OWNER}/wishhub-api:buildcache,mode=max" \
  --push \
  .

echo "==> Building & pushing Frontend image: ${FRONTEND_IMAGE}"
docker buildx build \
  --platform "${PLATFORM}" \
  --file frontend/Dockerfile \
  --build-arg VITE_API_URL=/api \
  --build-arg VITE_HUB_URL=/hubs \
  --tag "${FRONTEND_IMAGE}" \
  --cache-from "type=registry,ref=ghcr.io/${IMAGE_OWNER}/wishhub-frontend:buildcache" \
  --cache-to "type=registry,ref=ghcr.io/${IMAGE_OWNER}/wishhub-frontend:buildcache,mode=max" \
  --push \
  ./frontend

echo
echo "Done. Images pushed:"
echo "  ${API_IMAGE}"
echo "  ${FRONTEND_IMAGE}"
echo
echo "On the server, run: docker compose -f docker-compose.registry.yml pull && \\"
echo "                    docker compose -f docker-compose.registry.yml up -d"
