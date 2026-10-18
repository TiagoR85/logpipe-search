# ADR-0003: Retention and alias

**Status:** Accepted  
**Date:** 2026-10-12

## Decision

Retention (delete `logs-*` older than `Retention:Days`, default 7) and the `logs-current` alias (atomically reassigned to today's index) are **code-managed** by an hourly `IndexMaintenance` hosted service — not Elasticsearch ILM.

## Context

ILM requires license-aware policy APIs and snapshot semantics that are overkill for a 7-day demo window; code keeps the behavior testable with Testcontainers and identical in every environment; the alias gives the UI a stable read target while writes keep rolling to fresh daily indices. The first run happens at startup so a fresh clone is fully converged (it also creates today's index when missing — the alias swap requires the target to exist, and ES mandates explicit `index` on alias-remove actions, so current alias targets are listed first and removed one by one in the same atomic update call).

## Consequences

- A stopped search service stops retention (indices can grow past 7 days until the next run — acceptable for a demo, documented in the README).
- Production would move to ILM + curator-style snapshots (roadmap).
