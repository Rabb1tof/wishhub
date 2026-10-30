# syntax=docker/dockerfile:1.7

# =============================================
# Frontend Dockerfile for WishHub
# =============================================
# Notes:
# - Node 24 satisfies the frontend toolchain requirements.
# - Debian slim keeps glibc native bindings in the build stage.
# - Only static assets are copied into the nginx Alpine runtime.
# =============================================

# =============================================
# Build stage
# =============================================
FROM node:24-slim AS build

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
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
