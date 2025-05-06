"""JWT validation (same HMAC key the .NET services use) and care-team checks through the patients service."""

import time
import uuid
from dataclasses import dataclass

import httpx
import jwt
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from app.config import Settings, get_settings

PROGRAMME_WIDE = {"CareCoordinator", "Admin", "Service"}
STAFF = {"Doctor", "Nurse", "CareCoordinator", "Admin"}
_bearer = HTTPBearer(auto_error=False)


@dataclass(frozen=True)
class User:
    id: str
    role: str

    @property
    def sees_all(self) -> bool:
        return self.role in PROGRAMME_WIDE


def current_user(
    creds: HTTPAuthorizationCredentials | None = Depends(_bearer),
    settings: Settings = Depends(get_settings),
) -> User:
    if creds is None:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "missing bearer token")
    try:
        claims = jwt.decode(creds.credentials, settings.jwt_signing_key, algorithms=["HS256"],
                            audience=settings.jwt_audience, issuer=settings.jwt_issuer, leeway=30)
    except jwt.PyJWTError as ex:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "invalid token") from ex
    role = claims.get("role", "")
    if role not in STAFF and role != "Service":
        raise HTTPException(status.HTTP_403_FORBIDDEN, "staff only")
    return User(id=claims.get("sub", ""), role=role)


class ServiceToken:
    def __init__(self, settings: Settings):
        self.settings = settings
        self._token: str | None = None
        self._exp = 0.0

    def get(self) -> str:
        now = time.time()
        if self._token and self._exp - now > 60:
            return self._token
        self._exp = now + 300
        self._token = jwt.encode({
            "sub": str(uuid.UUID(int=0)), "role": "Service", "name": self.settings.service_name,
            "iss": self.settings.jwt_issuer, "aud": self.settings.jwt_audience,
            "iat": int(now), "nbf": int(now), "exp": int(self._exp), "jti": str(uuid.uuid4()),
        }, self.settings.jwt_signing_key, algorithm="HS256")
        return self._token


class PatientsClient:
    """Small REST client with a timeout and a couple of retries; good enough for two lookups."""

    def __init__(self, settings: Settings):
        self.settings = settings
        self.tokens = ServiceToken(settings)
        self.http = httpx.Client(base_url=settings.patients_url, timeout=httpx.Timeout(3.0))

    def _request(self, method: str, url: str, **kwargs) -> httpx.Response:
        last: Exception | None = None
        for attempt in range(3):
            try:
                r = self.http.request(method, url, headers={"Authorization": f"Bearer {self.tokens.get()}"}, **kwargs)
                if r.status_code < 500:
                    return r
                last = httpx.HTTPStatusError("server error", request=r.request, response=r)
            except httpx.TransportError as ex:
                last = ex
            time.sleep(0.2 * (2 ** attempt))
        raise last  # type: ignore[misc]

    def can_access(self, user: User, patient_id: str, action: str, resource: str) -> bool:
        r = self._request("POST", "/internal/access", json={
            "userId": user.id, "role": user.role, "patientId": patient_id,
            "action": action, "resource": resource, "service": self.settings.service_name,
        })
        return r.status_code == 200 and r.json().get("allowed", False)

    def visible_patients(self, user: User) -> set[str] | None:
        if user.sees_all:
            return None
        r = self._request("GET", f"/internal/users/{user.id}/patients", params={"role": user.role})
        r.raise_for_status()
        body = r.json()
        return None if body.get("all") else {p.lower() for p in body.get("patientIds", [])}

    def profile(self, patient_id: str) -> dict | None:
        r = self._request("GET", f"/internal/patients/{patient_id}/profile")
        if r.status_code == 404:
            return None
        r.raise_for_status()
        return r.json()
