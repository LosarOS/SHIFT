# SHIFT RC7 — Productivity & reliability

2026-11-02. Baseline main: ace0e8ba5462a05cd0146bf8705472c74b1f6ea1; published RC6 preserved.

## Environment and audit

The managed executor failed provisioning with gateway_unavailable. No local builds or tests have run this session. Work proceeds through authorized GitHub APIs and actual disposable GitHub Actions runners where available.

Latest main CI40972038240 has384passing core tests but failed native Studio with an application ERROR. This is being investigated before new release gates. Open PRs1–8 are dependency proposals, not user feature requests; no blind major dependency upgrades.

## Scope under investigation

Bulk workspace composition with reviewed local targets/URLs, launch plan preview, stronger native lifecycle reliability. No hidden shell inference, no automatic launch/import, no schema changes unless independently justified. Physical Windows10/11/DPI/accessibility gates remain separate from Server CI.
