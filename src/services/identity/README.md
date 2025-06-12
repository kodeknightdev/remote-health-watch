# identity

Staff accounts, roles and tokens. Issues 10-minute JWT access tokens and rotating refresh tokens (HttpOnly cookie, single use, reuse revokes the whole family). Seeds the demo staff in Development.

## Endpoints

- `POST /api/auth/login`, `/refresh`, `/logout`, `GET /api/auth/me`
- `GET /api/users?role=` (coordinators, admins), `POST`/`PUT /api/users` (admins)
- `GET /api/users/on-call`, `GET /api/users/directory` (services)

## Events

- Publishes: none
- Consumes: none

## Run it alone

```bash
dotnet run --project src/services/identity/RemoteHealthWatch.Identity
```

Needs SQL Server (`rhw_identity`). Port 5101 in Development.
