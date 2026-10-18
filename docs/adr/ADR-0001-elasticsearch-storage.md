# ADR-0001: Elasticsearch storage

**Status:** Accepted  
**Date:** 2026-10-12

## Decision

One write index per UTC day (`logs-yyyy.MM.dd`), created implicitly by the first bulk and pre-configured by the index template `logpipe-logs` (pattern `logs-*`, priority 500 to outrank the built-in Elastic `logs` template, 1 shard / 0 replicas, `id|level|service|trace_id|span_id` keyword, `timestamp` date, `message` text, `attributes` dynamic). Reads query the `logs-*` pattern; the `logs-current` alias and retention are code-managed (Task 8). Document ids are the client entry ids so gRPC retries are idempotent overwrites.

## Context

Spec §5 asks for Elasticsearch without naming a schema; daily indices make the 7-day retention a single cheap `delete index` instead of per-document deletes, and the template guarantees the UI/search filters have keyword fields from the first entry. Priority 500 (not the originally planned 100) is required because ES 9 ships a built-in `logs` template (`logs-*-*`) at priority 100, and two templates at the same priority matching the same index are rejected with 400.

## Consequences

- Cross-day queries fan out over the pattern (fine at demo scale); time zones are pinned to UTC; renaming fields is a template-version bump + reindex (roadmap), not an in-place migration.
- The built-in template conflict means our priority must stay above 100 unless the pattern is narrowed.
