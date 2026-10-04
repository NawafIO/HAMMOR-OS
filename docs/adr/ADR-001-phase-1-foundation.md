# ADR 001: Phase 1 Foundation & Architecture Baseline

## Status
Accepted / Implemented

## Context
HAMMOR OS requires a strictly isolated native Windows 11 AI assistant core. The architecture must avoid Electron or web wrappers, support dynamic Arabic (RTL) and English (LTR) switching without restarts, and securely manage API secrets without hardcoding them into the business logic.

## Decisions
1. **Layered Architecture:** Enforced strict separation across `HAMMOR.Core` (logic/interfaces), `HAMMOR.Infrastructure` (data/providers), `HAMMOR.Platform.Windows` (OS-specific integration), and `HAMMOR.App` (WPF UI).
2. **Secret Management:** Utilized Windows DPAPI for CurrentUser secret storage. Ensured no secrets exist in Git, logs, or plain text configurations.
3. **Voice Configuration:** The ElevenLabs Voice ID (`G3YpdjT1OTh9cunaumJs`) is restricted to the configuration layer and explicitly excluded from provider hard-coding.
4. **Memory & State:** Adopted SQLite for structured task/memory state, paired with an incremental Markdown mirror for human-readable Vault access.
5. **UI Framework:** Integrated `Wpf.Ui` for native Fluent Design implementation.

## Consequences
- A safe rollback point is established.
- 63/63 tests pass, guaranteeing the Definition of Done (DoD) is verified at rest.
- The repository is secured against secret leaks prior to expanding agentic tools.
