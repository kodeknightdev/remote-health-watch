# syntax=docker/dockerfile:1.7
FROM node:22.14.0-alpine AS build
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
RUN --mount=type=cache,id=rhw-npm,target=/root/.npm npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.27.5-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/remote-health-watch-dashboard/browser /usr/share/nginx/html
EXPOSE 80
