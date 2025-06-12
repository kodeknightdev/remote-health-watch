# angular-frontend

Angular 19 clinician dashboard: login, risk-sorted patient list with live tiles and sparklines, patient detail with
trends and thresholds, alert inbox with the escalation timeline, device registry, enrollment and programme KPIs.

The access token lives only in memory; the refresh token is an HttpOnly cookie, so a reload restores the session through
`/api/auth/refresh`. Live data arrives over one SignalR connection (`core/live.service.ts`) and is exposed as signals.

```bash
npm ci
npm start                                                   # http://localhost:4200, proxies /api and /hubs to :5000
npx ng build
npx ng test --watch=false --browsers=ChromeHeadlessCI
```

In Docker the production build is served by nginx, which proxies `/api` and `/hubs` to the gateway (`nginx.conf`).
