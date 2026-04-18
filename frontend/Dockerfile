# =============================================
# Frontend Dockerfile for WishHub
# =============================================

# Build stage - use Debian-based image for native module compatibility
# (Alpine uses musl libc which breaks rolldown native bindings)
FROM node:20 AS build

# Accept build arguments from docker-compose
ARG VITE_API_URL=/api
ARG VITE_HUB_URL=/hubs

# Set as environment variables for Vite build
ENV VITE_API_URL=${VITE_API_URL}
ENV VITE_HUB_URL=${VITE_HUB_URL}
# Don't set NODE_ENV=production before npm ci - it would skip devDependencies
ENV NODE_ENV=development

WORKDIR /app

# Copy package files first (for better layer caching)
COPY package.json package-lock.json ./

# Clean install ALL dependencies (including devDependencies for build tools)
# Force install native bindings for the container platform (linux)
RUN npm ci --include=dev

# Copy source code (.dockerignore excludes node_modules)
COPY . .

# Now set production mode for the build
ENV NODE_ENV=production

# Build: TypeScript check + Vite build
# Use npx to ensure local TypeScript is used
RUN npx tsc -b && npx vite build

# =============================================
# Production stage
# =============================================
FROM nginx:alpine

# Copy built application
COPY --from=build /app/dist /usr/share/nginx/html

# Copy nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf

# Set proper permissions (nginx user already exists in nginx:alpine)
RUN chown -R nginx:nginx /usr/share/nginx/html && \
    chmod -R 755 /usr/share/nginx/html

EXPOSE 80

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
