# syntax=docker/dockerfile:1.7
FROM python:3.13.3-slim-bookworm

# Microsoft ODBC Driver 18 for pyodbc; the repo setup follows learn.microsoft.com for Debian 12
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
    && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
    && curl -fsSL https://packages.microsoft.com/config/debian/12/prod.list -o /etc/apt/sources.list.d/mssql-release.list \
    && apt-get update \
    && ACCEPT_EULA=Y apt-get install -y --no-install-recommends msodbcsql18=18.5.1.1-1 unixodbc libgssapi-krb5-2 \
    && apt-get purge -y curl gnupg && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY requirements.txt .
RUN --mount=type=cache,id=rhw-pip,target=/root/.cache/pip pip install -r requirements.txt
COPY alembic.ini .
COPY alembic/ alembic/
COPY app/ app/

RUN useradd --uid 10001 --no-create-home appuser
USER 10001
ENV PYTHONUNBUFFERED=1
EXPOSE 8000
CMD ["sh", "-c", "alembic upgrade head && exec uvicorn app.main:app --host 0.0.0.0 --port 8000 --no-access-log"]
