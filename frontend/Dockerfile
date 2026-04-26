# syntax=docker/dockerfile:1.7

# =============================================
# Frontend Dockerfile for WishHub
# =============================================
# Notes:
# - We use node:20-slim (Debian-slim, glibc) instead of node:20 (~1.1 GB)
#   so rolldown native bindings still work but the build image is ~3x smaller.
# - Alpine is intentionally avoided because rolldown native bindings break on musl.
# =============================================

# =============================================
# Build stage
# =============================================
FROM node:20-slim AS build

ARG VITE_API_URL=/api
ARG VITE_HUB_URL=/hubs

ENV VITE_API_URL=${VITE_API_URL} \
    VITE_HUB_URL=${VITE_HUB_URL} \
    NODE_ENV=development

WORKDIR /app

# 1. Install deps (cache the npm download cache between builds via BuildKit)
COPY package.json package-lock.json ./
RUN --mount=type=cache,id=wishhub-npm,target=/root/.npm \
    npm ci --include=dev --prefer-offline --no-audit --no-fund

# 2. Copy source and build
COPY . .

ENV NODE_ENV=production
RUN npx tsc -b && npx vite build

# =============================================
# Runtime stage
# =============================================
FROM nginx:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf

RUN chown -R nginx:nginx /usr/share/nginx/html && \
    chmod -R 755 /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
