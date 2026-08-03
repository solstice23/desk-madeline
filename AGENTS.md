# DeskMadeline contribution rules

## Exact-port requirement

DeskMadeline is an exact behavioral and audiovisual port, not a Celeste-inspired
reimplementation. Every change must be cross-checked against the original Celeste
source and assets in `celeste_reference/` and `celeste_graphics_dump/`, or against
the named mod's upstream source when implementing mod functionality.

- Port the original constants, state transitions, update order, collision checks,
  input buffers, timers, animation frame timing, particles, colors, compositing,
  sound event paths, sound parameters, and event timing.
- Preserve frame-order details, including freeze frames, component/state-machine
  ordering, coroutine delays, and when effects are created relative to rendering.
- Use the original sprites and original FMOD events. Never add synthesized,
  approximate, placeholder, or "similar" assets or behavior.
- Do not guess when source material can answer the question. Inspect the source and
  document any desktop-specific adaptation in code beside the adaptation.
- Keep desktop-only policy choices (focus gating, monitor boundaries, menus, and
  persistence) separate from the ported gameplay logic.
- If an exact port is blocked by a missing dependency or asset, leave that portion
  disabled and report the limitation instead of silently substituting behavior.
- Verify changes with frame-level or state-level regression checks where practical,
  build Release with zero warnings/errors, and compare observable behavior with the
  original implementation before considering the work complete.

