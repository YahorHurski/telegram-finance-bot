---
schema_version: 1
open_count: 2
waived_count: 0
fixed_count: 0
total_count: 2
last_updated: 2026-11-16T21:29:28.216Z
---

# Broken Windows Ledger

> Cross-phase defect register. With `workflow.windows_enforce` enabled, `/gsd-ship` blocks while `open_count > 0`.
> Waive with `gsd-tools windows waive <id> "<reason>"` (reason required).
> Mark fixed with `gsd-tools windows fixed <id>`.

| id | phase | kind | file | line | description | status | reason | recorded_at | resolved_at |
|----|-------|------|------|------|-------------|--------|--------|-------------|-------------|
| 1 | 01 | stub | src/FinanceBot/Messages.cs | 26 | No overrun branch (D-02): over 200 zł the second line shows negative remainder; plan 01-02 adds 🔴 branch | open |  | 2026-11-16T20:56:26.515Z |  |
| 2 | 01 | unrun-verify | spikes/skia-arm64/Program.cs |  | ScottPlot/SkiaSharp PNG on linux/arm64 not proven: QEMU crashes .NET itself; native run on the VM in 01-06 | open |  | 2026-11-16T21:29:28.216Z |  |

````json
[
  {
    "id": 1,
    "kind": "stub",
    "phase": "01",
    "file": "src/FinanceBot/Messages.cs",
    "line": 26,
    "description": "No overrun branch (D-02): over 200 zł the second line shows negative remainder; plan 01-02 adds 🔴 branch",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-11-16T20:56:26.515Z",
    "resolved_at": null,
    "milestone": null
  },
  {
    "id": 2,
    "kind": "unrun-verify",
    "phase": "01",
    "file": "spikes/skia-arm64/Program.cs",
    "line": null,
    "description": "ScottPlot/SkiaSharp PNG on linux/arm64 not proven: QEMU crashes .NET itself; native run on the VM in 01-06",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-11-16T21:29:28.216Z",
    "resolved_at": null,
    "milestone": null
  }
]
````
