# DeskMadeline contribution rules

## Exact-port requirement

DeskMadeline is an exact behavioral and audiovisual port, not a Celeste-inspired
reimplementation. Every change must be cross-checked against the original Celeste
source and assets in `celeste_reference/` and `celeste_graphics_dump/` (neither is in
the repository; `SETUP.md` says how to make them), or against the named mod's upstream
source when implementing mod functionality.

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

## Behavior is emergent; never special-case it

Most of what a player recognizes as Celeste is not named anywhere in Celeste's source.
Supers, hypers, wavedashes, ultras, cornerboosts, wallbounces and the dream double jump
have no code of their own: they fall out of ordinary systems interacting. Reproduce the
systems and the tech appears by itself.

- When a behavior is missing or feels wrong, find the mismatch in the underlying system
  and fix that. Never add a mechanism whose only purpose is to reproduce an observation.
  A special case reproduces one symptom and diverges everywhere else that system is used.
- Never shadow a vanilla variable with a parallel one. Every site that clears the original
  must also clear the shadow, and a single missed site is a new exploit. Port the vanilla
  field, with the vanilla name, lifetime and clear sites.
- If vanilla stores something as a flag, port it as a flag. Widening it into a timer
  invents a window that vanilla does not have.
- Worked example: the dream double jump is `DreamDashEnd` re-granting the ordinary
  `jumpGraceTimer` on a horizontal exit, even though the exit-frame jump just consumed it.
  `dreamJump` is only a sound selector. An added parallel "dream grace timer" reproduced
  the double jump and simultaneously handed out free mid-air jumps after wall jumps,
  because the wall-jump paths only cleared the vanilla timer.

## The engine is part of the port

Frame counts come from Monocle, not from Celeste's entity code. Porting a `Player` method
without its engine context silently changes input windows.

- `StateMachine.Update` runs the state update *before* the state coroutine, so a state's
  update method runs one extra time on the frame its coroutine finishes.
- `Coroutine.orig_Update` tests the wait timer *before* decrementing it, so a wait ends on
  the frame the timer is no longer positive, not the frame it reaches zero.
- `yield return null` costs a real frame, with the state update running during it.
- Consequence: the dash Super/Hyper window is 12 `DashUpdate` frames. Rewriting
  `DashCoroutine` as a plain countdown loses two of them.
- Update order inside `Player.orig_Update` is load-bearing: the wall boost reads the
  previous frame's `moveX`, retained wall speed is restored before the state machine, and
  the dash floor snap runs after the state machine and before `MoveH`.
- Guard conditions are as much a part of the port as the numbers. `onGround` is only
  evaluated while `Speed.Y >= 0`; `lastClimbMove` is sampled before the slip override.
- Some correct behaviour looks like a bug from the outside. A dream hyper comes out a super
  whenever she reached the block already crouched, because `DashBegin` stands up an airborne
  crouched player and its `else if` cannot crouch her again — which is the whole reason
  Celeste players say not to demo-dash into the block. Forcing the crouch there would "fix"
  the report and break the rule. `DreamHyperChecks` pins both outcomes.
- *When* input is sampled is itself a mechanic. Vanilla refreshes `lastAim` every frame and
  `DashCoroutine` reads it when it resumes, after the freeze - so the aim held about four
  frames *after* the dash press is the one that counts. Latching input at the press frame
  looks like a harmless simplification and silently deletes the hand demo dash: press down
  with dash to duck at `DashBegin`, then swap to a horizontal aim before it is sampled.
  Never snapshot input early "to keep it stable"; sample it where vanilla samples it.

## Constants

- Trace a constant to where vanilla actually sets it, not to a similar-looking neighbour.
  Input buffers come from `VirtualButton(binding, pad, bufferTime, triggerThreshold)`:
  Jump, Dash and CrouchDash all buffer 0.08s, which is not the 0.1s `JumpGraceTime`.
- Vanilla often reads one binding through several differently tuned accessors.
  `Input.MoveX`, `MoveY`, `GliderMoveY` and `Aim` are the same buttons at 0.3, 0.7, 0.3 and
  0.25. Collapsing them is invisible on a keyboard and wrong on a controller: at full
  diagonal deflection a stick reads ~0.61 after deadzone rescaling, so an up-diagonal dash
  is impossible if dash aim is gated at the 0.7 movement threshold.
- Port vanilla's default bindings, including the ones deliberately left unbound.

## Read all of the reference; omission is the expensive bug

Most of a fresh port's round trips are not subtle mismatches — they are parts of the
reference that were never read, or were read and silently set aside. The pufferfish took
four rounds of fixes, and the first and largest existed only because `Render` had not
been ported at all: the black outline, the arc of marks that watches the player, and the
one-pixel eye are most of what a puffer looks like, and none of them live in `Update`.
Reading the whole class before writing anything is cheaper than debugging the missing
parts in afterwards, one user report at a time.

- Before porting an entity, read its reference class end to end and write out its member
  list: the constructor and every component it `Add`s, every override — `Added`,
  `Update`, `Render`, `OnSquish`, the colliders and their callbacks — plus the
  sprite-bank entry, the sounds and the particles. Port from that list and tick items
  off. Anything left out is a decision that gets written down with its reason ("the
  desktop has no TouchSwitch to turn on"), never a silence. "It's only drawing" is not
  a reason; there is no *only drawing*. Wigglers, sine offsets, outlines and per-frame
  render math are behavior, and the reference draws much of an entity's character in
  `Render`.
- The same thoroughness applies one level down: when entity code calls the engine, read
  the engine method and port its semantics, not a from-memory paraphrase. Monocle's
  `Move*` measures against `ExactPosition` — position *plus* the pending sub-pixel
  counter. A paraphrase that measured against position alone re-integrated the same
  half-pixel error every frame and vibrated the whole sprite at frame rate; it passed
  every numeric check, and cost a round trip that reading `Actor.cs` would have spared.
  Prefer the port's existing, already-verified helpers over writing new copies.
- Rounding happens once, in one space. Anything that lands on the world grid is floored
  in world coordinates and then offset by the same rounded anchor its stamp hangs on;
  flooring against a fractional position puts that fraction into the sum twice with two
  different roundings, and the result crawls. Relatedly, a GDI+ transform resamples even
  at rotation zero and scale one — draw plain unless actually turned or squashed.
- What does slip through shows up on screen, not in the checks, so the backstop is
  instrumented looking: burst-capture the entity and diff consecutive frames, on a stage
  with nothing live behind it. Reproduce a reported bug with such an instrument *before*
  changing code, and call it fixed when the same instrument goes quiet — a plausible fix
  shipped unmeasured is how one report becomes four.
- Env-gated spawn and trace hooks are fine while hunting and come back out before the
  commit. The code and the checks carry the lesson afterwards, not the scaffolding.

## Verifying movement

- Prefer a headless harness that drives `Player.Update` at a fixed 60Hz over synthetic
  `Solid`s and asserts vanilla's numbers: 260/-105 super, 325/-52.5 hyper, 1.2x ultra,
  170/-160 wallbounce, 130/-105 wall jump, 0.1s coyote, 12-frame dash window, 10/s climb
  still cost. Frame-level assertions catch what play testing cannot.
- Feed the harness the same input contract the app uses, or the test measures nothing:
  `PetWindow.SampleInput` re-derives `JumpPressed`/`DashPressed` from the buffers every
  frame.
- Assert a window in the unit the source uses (seconds, or a counted frame span), not a
  guessed frame index. When a measurement disagrees with expectation, re-derive the
  expectation from the reference before changing code.
- The checks live in `tests/DeskMadeline.Tests`; add to them rather than starting over.

## Sound

- Sounds are the original Celeste FMOD events, played from an installed copy of the game.
  There are no substitute samples, so an event path is either vanilla's or nothing.
- Which events fire, and in what order, is part of the port. `Play` calls in the reference
  are as load-bearing as the numbers beside them: `SuperJump` plays `jump` *and then* its
  `jump_super` / `jump_superslide` layer, and a move that plays one of a pair plays both.
- `Player.PlaySound` only queues onto `SoundEvents`, which `PetWindow` drains. That makes
  sound headless-testable: drive the move and read the queue, exactly as `SoundChecks` does.
  Assert what was heard *and* how many times, since a doubled sound is as wrong as a missing
  one.
- Watch for effects vanilla fires from two places. `CallDashEvents` is reached by
  `DashCoroutine` on the dash's second frame *and* by `DashEnd`, guarded by
  `calledDashEvents`, precisely so a dash cut short on frame one still sounds. Any move that
  leaves a state early is a candidate for this; port the End callback, not just the
  coroutine.
- Sound is gated on state that is easy to get subtly wrong: `orig_Update` sounds a duck
  wherever it happens but a stand only while `onGround`. Read the guard, not just the call.
- A missing event is silent rather than loud: `SoundEffects.Play` logs and swallows. Nothing
  will look broken, so verify against the queue, and use the opt-in bank check
  (`SFXCHECK=1`) when an event path itself is in question.

## Commit messages

Name the change, plainly. A subject is lowercase, leads with the verb where a verb fits,
and says what the commit does in the fewest words that stay specific: `port celeste's super
dashing variant`, `update strings`. No conventional-commit prefixes, no ticket numbers, no
trailing period.

- Much of the existing log is figurative — `up is the point`, `a lip with a border standing
  on it is not a lip`. That register is history, not the house style; do not imitate it.
- A body carries only what the subject cannot: which reference method the port came from,
  why a constant is that number, what was deliberately left out and why. When there is none
  of that, the subject is the whole message.
- Say what changed, not how it went. "fix", "improve" and "properly" carry no information,
  and neither does a subject that praises the work it describes.
