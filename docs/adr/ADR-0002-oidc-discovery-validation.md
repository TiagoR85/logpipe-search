# ADR-0002: OIDC discovery validation

**Status:** Accepted  
**Date:** 2026-10-12

## Decision

Search is a pure OIDC **resource server**: JwtBearer with `Authority` = the ingest issuer URL performs standard discovery + JWKS validation; no client secret, no shared symmetric key, no `ValidateIssuer=false`. `ValidateAudience=false` because F2's client_credentials tokens carry scopes, not audiences; authorization is scope-based (Task 8). `Jwt:RequireHttpsMetadata` defaults to `true` and is explicitly disabled (`false`) in Development, tests and the local compose — the demo hub runs plain HTTP internally.

## Context

F1 validated tokens with a symmetric key per service; F2's issuer already exposes discovery at `/`, so the standard metadata path costs nothing and removes secret distribution from the search deployment entirely. JwtBearer hard-requires HTTPS for metadata unless the flag is relaxed, which forced the explicit config switch instead of relying on the environment.

## Consequences

- Search cannot validate tokens if the issuer is down at validation time (JWKS is cached by the JwtBearer handler after first fetch — outage-tolerant after warm-up).
- Rotating the OpenIddict development signing certificate invalidates outstanding 5-minute tokens only.
- Production would set an audience, flip `ValidateAudience` and `RequireHttpsMetadata` back on (roadmap note).
