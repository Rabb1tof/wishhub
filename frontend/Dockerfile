# =============================================
# Frontend Dockerfile for WishHub
# =============================================

# Build stage
FROM node:20-alpine AS build

# Accept build arguments
ARG VITE_API_URL=/api
ARG VITE_HUB_URL=/hubs
ARG NODE_ENV=production

# Set as environment variables for build
ENV VITE_API_URL=${VITE_API_URL}
ENV VITE_HUB_URL=${VITE_HUB_URL}
ENV NODE_ENV=${NODE_ENV}

WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci --only=production

# Copy source code
COPY . .

# Build the application
RUN npm run build

# =============================================
# Production stage
# =============================================
FROM nginx:alpine

# Copy built application
COPY --from=build /app/dist /usr/share/nginx/html

# Copy nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf

# Create non-root user for security
RUN addgroup -g 1001 -S nginx && \
    adduser -S -D -H -u 1001 -h /var/cache/nginx -s /sbin/nologin -G nginx nginx

# Set proper permissions
RUN chown -R nginx:nginx /usr/share/nginx/html && \
    chmod -R 755 /usr/share/nginx/html

EXPOSE 80

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
