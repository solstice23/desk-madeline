using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>What a kevin window wants started, wound down or stopped on its sound loops.</summary>
    public enum KevinLoopCommand { Start, Ending, Stop }

    public readonly struct KevinLoopEvent
    {
        public readonly IntPtr Window;
        public readonly string Path;
        public readonly KevinLoopCommand Command;
        public KevinLoopEvent(IntPtr window, string path, KevinLoopCommand command)
        { Window = window; Path = path; Command = command; }
    }

    public enum KevinParticleKind { Activate, Crushing, Impact }

    public readonly struct KevinParticleEvent
    {
        public readonly KevinParticleKind Kind;
        public readonly float X, Y, Direction, RangeX, RangeY;
        public readonly int Count;
        public KevinParticleEvent(KevinParticleKind kind, float x, float y, float direction,
            float rangeX, float rangeY, int count)
        { Kind = kind; X = x; Y = y; Direction = direction; RangeX = rangeX; RangeY = rangeY; Count = count; }
    }

    /// <summary>
    /// Windows as CrushBlocks: dash into a border and the whole window charges the way its
    /// face was hit, crushes whatever it reaches against whatever is behind, and then crawls
    /// back to where it started. The port of Celeste's kevin blocks, second of the modes in
    /// which the pet moves the user's windows rather than only reading them.
    /// </summary>
    /// <remarks>
    /// The numbers and the sequence are CrushBlock's: four tenths of a second of shaking
    /// before it moves, 240 pixels a second reached at 500 a second while attacking, 60
    /// reached at 160 on the way home, the return walked back along a stack of the places
    /// each attack started -- a new attack skips the push when it runs along or against the
    /// last one -- and a dash re-arms it as soon as the windup ends, so a second dash from
    /// another side turns it mid-flight. Hitting anything sounds the impact, shakes it for
    /// four tenths, and rests it for four tenths more before the return begins; each waypoint
    /// on the way home is a two-tenths shake and a two-tenths pause.
    ///
    /// What stops an attack is what stops her: the world's own solids, the same occluded,
    /// ignored-and-subtracted border pieces RebuildSolids makes, plus the edge of the desktop.
    /// Testing raw window rectangles instead would let a window that is entirely hidden behind
    /// a maximized one -- no border of it visible, nothing of it solid to her -- stop a charge
    /// dead against something nobody can see. Windows overlap freely on a desktop, so whatever
    /// the window already overlaps when its attack begins is excused until it has separated --
    /// vanilla never decides this, because its solids can never overlap at all. Before giving
    /// up, the block tries vanilla's slip: up to four pixels perpendicular either way to
    /// squeeze past a corner.
    ///
    /// Also the desktop's rather than the game's: a window's home is wherever its owner last
    /// put it, told apart from this class's own moves by the same ring of lately-asked-for
    /// places MoonWindows keeps; an owner's drag cancels everything and the window simply
    /// lives where it was dropped. Movement lands on whole game pixels, as the moon blocks'
    /// does and for the same reasons. The face, the tile artwork and the lit edges are the
    /// window itself here, so nothing of them is drawn; the particles, sounds and shaking are
    /// ported, which is everything of the block that is not its skin.
    /// </remarks>
    internal sealed class KevinWindows
    {
        // CrushBlock's numbers.
        const float CrushSpeed = 240f, CrushAccel = 500f;
        const float ReturnSpeed = 60f, ReturnAccel = 160f;

        public enum Phase { Idle, Windup, Attack, ImpactPause, Return, ReturnPause }

        sealed class Block
        {
            public Win32.RECT Home;                     // where its owner put it, screen px
            public readonly Point[] Told = new Point[32];
            public int ToldNext;
            public int OffsetX, OffsetY;                // where it is being held, game px
            public Point Shake;                         // game px, on top of the offset
            public Phase Phase;
            public PointF CrushDir;
            public float Speed, Timer, WaypointSfxDelay;
            public bool CanActivate = true;
            public float MoveRemainder;                 // fraction of a game px not yet moved
            public readonly List<(PointF From, PointF Direction)> ReturnStack
                = new List<(PointF, PointF)>();
            public float ShakeTimer, ShakeTick;
            public float DustTimer, MoveLoopEndTimer;
            public bool MoveLoop, ReturnLoop;
            /// <summary>Solids already overlapped when the attack began; excused until clear.</summary>
            public readonly HashSet<IntPtr> Grace = new HashSet<IntPtr>();
            public bool GraceFresh;
        }

        readonly Dictionary<IntPtr, Block> blocks = new Dictionary<IntPtr, Block>();
        static readonly Random random = new Random();

        public bool Active
        {
            get
            {
                foreach (Block block in blocks.Values)
                    if (block.Phase != Phase.Idle || block.OffsetX != 0 || block.OffsetY != 0)
                        return true;
                return false;
            }
        }

        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();
        public readonly Queue<KevinLoopEvent> LoopEvents = new Queue<KevinLoopEvent>();
        public readonly Queue<KevinParticleEvent> ParticleEvents = new Queue<KevinParticleEvent>();

        /// <summary>
        /// CrushBlock.OnDashed: a dash into a face charges the block toward where she came
        /// from, and throws her back off it. Answered from the shell's OnDashCollide.
        /// </summary>
        /// <param name="direction">The collision's direction: the way she was moving.</param>
        public DashCollisionResults Dashed(IntPtr window, PointF direction)
        {
            if (!blocks.TryGetValue(window, out Block block)) return DashCollisionResults.NormalCollision;
            var attackDir = new PointF(-direction.X, -direction.Y);
            // CanActivate: re-armed once the windup ends, but never along the way it is
            // already going -- vanilla's, minus the axis locks no window mode has.
            if (!block.CanActivate || block.CrushDir == attackDir)
                return DashCollisionResults.NormalCollision;
            Attack(window, block, attackDir);
            return DashCollisionResults.Rebound;
        }

        void Attack(IntPtr window, Block block, PointF direction)
        {
            SoundEvents.Enqueue(new PlayerSoundEvent(
                "event:/game/06_reflection/crushblock_activate"));
            // Vanilla winds the old move loop down and starts a new SoundSource beside it; the
            // shell keys one loop per window, so here the new start simply replaces the old.
            LoopEvents.Enqueue(new KevinLoopEvent(window,
                "event:/game/06_reflection/crushblock_move_loop", KevinLoopCommand.Start));
            block.MoveLoop = true;
            block.MoveLoopEndTimer = 0f;
            if (block.ReturnLoop)
            {
                LoopEvents.Enqueue(new KevinLoopEvent(window,
                    "event:/game/06_reflection/crushblock_return_loop", KevinLoopCommand.Stop));
                block.ReturnLoop = false;
            }
            block.CrushDir = direction;
            block.CanActivate = false;
            block.Phase = Phase.Windup;
            block.Timer = .4f;
            block.Speed = 0f;
            block.MoveRemainder = 0f;
            block.ShakeTimer = .4f;
            block.DustTimer = 0f;
            // Everything it overlaps right now is excused; the attack only stops at what it
            // newly reaches. What that is exactly is measured on the first step, when the
            // world's solids are in hand.
            block.Grace.Clear();
            block.GraceFresh = true;

            // ActivateParticles: a burst out of the leading face, its count scaled to the
            // face's length exactly as vanilla scales it to the block's.
            EmitActivateBurst(block, direction);

            // The stack of places to come home to. A new attack along or against the last
            // leg replaces it rather than stacking, as vanilla's does.
            bool push = true;
            if (block.ReturnStack.Count > 0)
            {
                (PointF, PointF) last = block.ReturnStack[block.ReturnStack.Count - 1];
                if (last.Item2 == direction ||
                    (last.Item2.X == -direction.X && last.Item2.Y == -direction.Y)) push = false;
            }
            if (push)
                block.ReturnStack.Add((new PointF(block.OffsetX, block.OffsetY), direction));
        }

        /// <summary>
        /// Advance every window one frame. Windows are the world's other solids, so each one's
        /// attack is tested against the rest of the list and the desktop's own edge.
        /// </summary>
        /// <param name="scale">Game pixels to screen pixels.</param>
        /// <param name="bounds">The virtual desktop, in screen pixels: vanilla's level bounds.</param>
        public void Update(float dt, int scale, IReadOnlyList<PolledWindowInfo> windows,
            IList<Solid> solids, Win32.RECT bounds)
        {
            var seen = new HashSet<IntPtr>();
            for (int i = 0; i < windows.Count; i++)
            {
                PolledWindowInfo window = windows[i];
                if (!window.IsPlatform) continue;
                seen.Add(window.Handle);
                if (!blocks.TryGetValue(window.Handle, out Block block))
                {
                    blocks[window.Handle] = block = new Block { Home = window.Rect };
                    Remember(block, window.Rect.Left, window.Rect.Top);
                }

                // Somewhere this never put it: its owner moved it, and the owner's word is
                // final -- whatever the block was doing is over, and this is its new home.
                if (!WasToldTo(block, window.Rect.Left, window.Rect.Top))
                {
                    ResetTo(window.Handle, block, window.Rect);
                    Remember(block, window.Rect.Left, window.Rect.Top);
                }

                Step(window.Handle, block, dt, scale, solids, bounds);

                int wantX = block.Home.Left + (block.OffsetX + block.Shake.X) * scale;
                int wantY = block.Home.Top + (block.OffsetY + block.Shake.Y) * scale;
                if (wantX != window.Rect.Left || wantY != window.Rect.Top)
                {
                    Remember(block, wantX, wantY);
                    Move(window.Handle, wantX, wantY);
                }
            }

            if (seen.Count == blocks.Count) return;
            var gone = new List<IntPtr>();
            foreach (IntPtr handle in blocks.Keys) if (!seen.Contains(handle)) gone.Add(handle);
            foreach (IntPtr handle in gone)
            {
                StopLoops(handle, blocks[handle]);
                blocks.Remove(handle);
            }
        }

        void Step(IntPtr handle, Block block, float dt, int scale,
            IList<Solid> solids, Win32.RECT bounds)
        {
            // Platform.Update's shake: a fresh whole-pixel jiggle every 0.04s while it lasts.
            if (block.ShakeTimer > 0f)
            {
                block.ShakeTimer -= dt;
                block.ShakeTick -= dt;
                if (block.ShakeTick <= 0f)
                {
                    block.ShakeTick = .04f;
                    block.Shake = new Point(random.Next(-1, 2), random.Next(-1, 2));
                }
                if (block.ShakeTimer <= 0f) block.Shake = Point.Empty;
            }
            else block.Shake = Point.Empty;

            switch (block.Phase)
            {
                case Phase.Windup:
                    block.Timer -= dt;
                    if (block.Timer <= 0f)
                    {
                        // The block re-arms the moment it starts moving, which is what lets a
                        // second dash from another side turn it mid-flight.
                        block.CanActivate = true;
                        block.Phase = Phase.Attack;
                    }
                    break;

                case Phase.Attack:
                    block.Speed = Approach(block.Speed, CrushSpeed, CrushAccel * dt);
                    if (!Advance(handle, block, block.Speed * dt, scale, solids, bounds))
                        Impact(handle, block, scale, solids, bounds);
                    else EmitCrushingDust(block, dt);
                    break;

                case Phase.ImpactPause:
                    block.Timer -= dt;
                    if (block.MoveLoopEndTimer > 0f)
                    {
                        block.MoveLoopEndTimer -= dt;
                        if (block.MoveLoopEndTimer <= 0f && block.MoveLoop)
                        {
                            LoopEvents.Enqueue(new KevinLoopEvent(handle,
                                "event:/game/06_reflection/crushblock_move_loop",
                                KevinLoopCommand.Stop));
                            block.MoveLoop = false;
                        }
                    }
                    if (block.Timer <= 0f)
                    {
                        block.Phase = Phase.Return;
                        block.Speed = 0f;
                        block.MoveRemainder = 0f;
                    }
                    break;

                case Phase.Return:
                    if (block.MoveLoopEndTimer > 0f)
                    {
                        block.MoveLoopEndTimer -= dt;
                        if (block.MoveLoopEndTimer <= 0f && block.MoveLoop)
                        {
                            LoopEvents.Enqueue(new KevinLoopEvent(handle,
                                "event:/game/06_reflection/crushblock_move_loop",
                                KevinLoopCommand.Stop));
                            block.MoveLoop = false;
                        }
                    }
                    if (block.ReturnStack.Count == 0) { Rest(handle, block); break; }
                    block.Speed = Approach(block.Speed, ReturnSpeed, ReturnAccel * dt);
                    block.WaypointSfxDelay -= dt;
                    (PointF from, PointF dir) = (block.ReturnStack[block.ReturnStack.Count - 1].From,
                        block.ReturnStack[block.ReturnStack.Count - 1].Direction);
                    // MoveTowards along the leg's own axis; the return pushes through whatever
                    // is in the way, exactly as a vanilla solid coming home does.
                    block.MoveRemainder += block.Speed * dt;
                    int step = (int)block.MoveRemainder;
                    block.MoveRemainder -= step;
                    if (dir.X != 0f)
                        block.OffsetX = Towards(block.OffsetX, (int)from.X, step);
                    if (dir.Y != 0f)
                        block.OffsetY = Towards(block.OffsetY, (int)from.Y, step);
                    if ((dir.X != 0f && block.OffsetX != (int)from.X) ||
                        (dir.Y != 0f && block.OffsetY != (int)from.Y)) break;

                    block.Speed = 0f;
                    block.MoveRemainder = 0f;
                    block.ReturnStack.RemoveAt(block.ReturnStack.Count - 1);
                    if (block.ReturnStack.Count == 0)
                    {
                        Rest(handle, block);
                        if (block.WaypointSfxDelay <= 0f)
                            SoundEvents.Enqueue(new PlayerSoundEvent(
                                "event:/game/06_reflection/crushblock_rest"));
                    }
                    else if (block.WaypointSfxDelay <= 0f)
                        SoundEvents.Enqueue(new PlayerSoundEvent(
                            "event:/game/06_reflection/crushblock_rest_waypoint"));
                    block.WaypointSfxDelay = .1f;
                    block.ShakeTimer = .2f;
                    if (block.Phase == Phase.Return)     // more legs to walk
                    {
                        block.Phase = Phase.ReturnPause;
                        block.Timer = .2f;
                    }
                    break;

                case Phase.ReturnPause:
                    block.Timer -= dt;
                    if (block.Timer <= 0f) { block.Phase = Phase.Return; block.Speed = 0f; }
                    break;
            }
        }

        /// <summary>
        /// Carry the attack forward by this many game pixels, stopping a pixel at a time.
        /// </summary>
        /// <returns>False the moment a pixel cannot be taken, which is the impact.</returns>
        bool Advance(IntPtr handle, Block block, float amount, int scale,
            IList<Solid> solids, Win32.RECT bounds)
        {
            // The grace set is settled here, where the solids are: whatever the window is
            // inside as its charge begins is excused, and stays excused until it has left.
            RefreshGrace(handle, block, scale, solids);
            block.MoveRemainder += amount;
            int steps = (int)block.MoveRemainder;
            block.MoveRemainder -= steps;
            int dx = Math.Sign(block.CrushDir.X), dy = Math.Sign(block.CrushDir.Y);
            for (int i = 0; i < steps; i++)
            {
                if (Fits(handle, block, block.OffsetX + dx, block.OffsetY + dy, dx, dy, scale, solids, bounds))
                {
                    block.OffsetX += dx;
                    block.OffsetY += dy;
                    continue;
                }
                // MoveHCheck's slip: before giving up, up to four pixels perpendicular either
                // way, nearest first and the positive side first, to squeeze past a corner.
                bool slipped = false;
                for (int reach = 1; reach <= 4 && !slipped; reach++)
                    for (int side = 1; side >= -1 && !slipped; side -= 2)
                    {
                        int px = dy != 0 ? reach * side : 0;
                        int py = dx != 0 ? reach * side : 0;
                        if (!Fits(handle, block, block.OffsetX + px + dx, block.OffsetY + py + dy,
                            dx, dy, scale, solids, bounds)) continue;
                        block.OffsetX += px + dx;
                        block.OffsetY += py + dy;
                        slipped = true;
                    }
                if (!slipped) return false;
            }
            return true;
        }

        void RefreshGrace(IntPtr handle, Block block, int scale, IList<Solid> solids)
        {
            graceScratch.Clear();
            GameRect(block, block.OffsetX, block.OffsetY, scale,
                out float l, out float t, out float r, out float b);
            for (int i = 0; i < solids.Count; i++)
            {
                Solid piece = solids[i];
                if (piece.Id == handle) continue;
                if (l < piece.R && r > piece.L && t < piece.B && b > piece.T)
                    graceScratch.Add(piece.Id);
            }
            if (block.GraceFresh)
            {
                block.Grace.Clear();
                foreach (IntPtr id in graceScratch) block.Grace.Add(id);
                block.GraceFresh = false;
            }
            else block.Grace.IntersectWith(graceScratch);
        }

        readonly HashSet<IntPtr> graceScratch = new HashSet<IntPtr>();

        void GameRect(Block block, int offsetX, int offsetY, int scale,
            out float l, out float t, out float r, out float b)
        {
            l = block.Home.Left / (float)scale + offsetX;
            t = block.Home.Top / (float)scale + offsetY;
            r = l + (block.Home.Right - block.Home.Left) / (float)scale;
            b = t + (block.Home.Bottom - block.Home.Top) / (float)scale;
        }

        /// <summary>
        /// Whether the window can take this step: its leading edge still inside the desktop,
        /// and touching nothing of the world's solids it was not already touching. The solids
        /// carry the occlusion, the ignored-maximized rule and the taskbar with them, so a
        /// window nobody can see stops nothing here either.
        /// </summary>
        /// <remarks>
        /// Only the edge being led with is measured against the desktop, and only along the
        /// axis of travel -- vanilla's MoveHCollideSolidsAndBounds does the same, and never
        /// asks whether the block started inside. A window half off the bottom of the screen
        /// is an ordinary window; demanding it be wholly on screen before it may move made
        /// every such window impact on the spot it stood on.
        /// </remarks>
        bool Fits(IntPtr handle, Block block, int offsetX, int offsetY, int stepDx, int stepDy,
            int scale, IList<Solid> solids, Win32.RECT bounds)
        {
            int sl = block.Home.Left + offsetX * scale;
            int st = block.Home.Top + offsetY * scale;
            int sr = sl + (block.Home.Right - block.Home.Left);
            int sb = st + (block.Home.Bottom - block.Home.Top);
            if ((stepDx < 0 && sl < bounds.Left) || (stepDx > 0 && sr > bounds.Right) ||
                (stepDy < 0 && st < bounds.Top) || (stepDy > 0 && sb > bounds.Bottom))
                return false;
            GameRect(block, offsetX, offsetY, scale, out float l, out float t, out float r, out float b);
            for (int i = 0; i < solids.Count; i++)
            {
                Solid piece = solids[i];
                if (piece.Id == handle) continue;
                if (l < piece.R && r > piece.L && t < piece.B && b > piece.T &&
                    !block.Grace.Contains(piece.Id)) return false;
            }
            return true;
        }

        void Impact(IntPtr handle, Block block, int scale,
            IList<Solid> solids, Win32.RECT bounds)
        {
            SoundEvents.Enqueue(new PlayerSoundEvent(
                "event:/game/06_reflection/crushblock_impact"));
            EmitImpactBurst(handle, block, scale, solids, bounds);
            block.ShakeTimer = .4f;
            // Vanilla winds the move loop down with its "end" parameter and removes it half a
            // second later; the return loop starts as the pause does.
            if (block.MoveLoop)
            {
                LoopEvents.Enqueue(new KevinLoopEvent(handle,
                    "event:/game/06_reflection/crushblock_move_loop", KevinLoopCommand.Ending));
                block.MoveLoopEndTimer = .5f;
            }
            LoopEvents.Enqueue(new KevinLoopEvent(handle,
                "event:/game/06_reflection/crushblock_return_loop", KevinLoopCommand.Start));
            block.ReturnLoop = true;
            block.CrushDir = PointF.Empty;
            block.Phase = Phase.ImpactPause;
            block.Timer = .4f;
        }

        void Rest(IntPtr handle, Block block)
        {
            block.Phase = Phase.Idle;
            block.CrushDir = PointF.Empty;
            block.Speed = 0f;
            if (block.ReturnLoop)
            {
                LoopEvents.Enqueue(new KevinLoopEvent(handle,
                    "event:/game/06_reflection/crushblock_return_loop", KevinLoopCommand.Stop));
                block.ReturnLoop = false;
            }
        }

        void ResetTo(IntPtr handle, Block block, Win32.RECT rect)
        {
            StopLoops(handle, block);
            block.Home = rect;
            block.OffsetX = block.OffsetY = 0;
            block.Shake = Point.Empty;
            block.ShakeTimer = 0f;
            block.Phase = Phase.Idle;
            block.CrushDir = PointF.Empty;
            block.Speed = 0f;
            block.MoveRemainder = 0f;
            block.CanActivate = true;
            block.ReturnStack.Clear();
            block.Grace.Clear();
        }

        void StopLoops(IntPtr handle, Block block)
        {
            if (block.MoveLoop)
                LoopEvents.Enqueue(new KevinLoopEvent(handle,
                    "event:/game/06_reflection/crushblock_move_loop", KevinLoopCommand.Stop));
            if (block.ReturnLoop)
                LoopEvents.Enqueue(new KevinLoopEvent(handle,
                    "event:/game/06_reflection/crushblock_return_loop", KevinLoopCommand.Stop));
            block.MoveLoop = block.ReturnLoop = false;
        }

        /// <summary>Put every window back where its owner left it, and forget them.</summary>
        public void Restore()
        {
            foreach (var pair in blocks)
            {
                StopLoops(pair.Key, pair.Value);
                if (pair.Value.OffsetX == 0 && pair.Value.OffsetY == 0 &&
                    pair.Value.Shake == Point.Empty) continue;
                Move(pair.Key, pair.Value.Home.Left, pair.Value.Home.Top);
            }
            blocks.Clear();
        }

        // ===== particles, at the edges vanilla emits them from =====

        void EmitActivateBurst(Block block, PointF dir)
        {
            GeometryOf(block, dir, out float x, out float y, out float rangeX, out float rangeY,
                out float direction, out int count);
            ParticleEvents.Enqueue(new KevinParticleEvent(KevinParticleKind.Activate,
                x, y, direction, rangeX, rangeY, count));
        }

        void EmitCrushingDust(Block block, float dt)
        {
            block.DustTimer += dt;
            while (block.DustTimer >= .02f)
            {
                block.DustTimer -= .02f;
                GeometryOf(block, block.CrushDir, out float x, out float y,
                    out float rangeX, out float rangeY, out float direction, out _);
                // Vanilla scatters each puff along the face itself and points it backwards.
                float backwards = (float)Math.Atan2(-block.CrushDir.Y, -block.CrushDir.X);
                ParticleEvents.Enqueue(new KevinParticleEvent(KevinParticleKind.Crushing,
                    x, y, backwards, rangeX, rangeY, 1));
            }
        }

        void EmitImpactBurst(IntPtr handle, Block block, int scale,
            IList<Solid> solids, Win32.RECT bounds)
        {
            // Vanilla walks the leading edge in eight-pixel cells and bursts a pair wherever a
            // solid is actually touching. The probe is one pixel past the edge.
            int dx = Math.Sign(block.CrushDir.X), dy = Math.Sign(block.CrushDir.Y);
            if (dx == 0 && dy == 0) return;
            GeometryOf(block, block.CrushDir, out float edgeX, out float edgeY,
                out float rangeX, out float rangeY, out _, out _);
            float backwards = (float)Math.Atan2(-dy, -dx);
            int cells = (int)Math.Max(1f, (rangeX > 0f ? rangeX : rangeY) / 4f);
            for (int cell = 0; cell < cells; cell++)
            {
                float along = -1f + (cell + .5f) * 2f / cells;
                float px = edgeX + rangeX * along;
                float py = edgeY + rangeY * along;
                if (!TouchesSolidAt(handle, block, px + dx, py + dy, scale, solids, bounds)) continue;
                ParticleEvents.Enqueue(new KevinParticleEvent(KevinParticleKind.Impact,
                    px + (dx == 0 ? 2f : 0f), py + (dy == 0 ? 2f : 0f), backwards, 0f, 0f, 1));
                ParticleEvents.Enqueue(new KevinParticleEvent(KevinParticleKind.Impact,
                    px - (dx == 0 ? 2f : 0f), py - (dy == 0 ? 2f : 0f), backwards, 0f, 0f, 1));
            }
        }

        bool TouchesSolidAt(IntPtr handle, Block block, float gx, float gy, int scale,
            IList<Solid> solids, Win32.RECT bounds)
        {
            float sx = gx * scale, sy = gy * scale;
            if (sx <= bounds.Left || sy <= bounds.Top || sx >= bounds.Right || sy >= bounds.Bottom)
                return true;
            for (int i = 0; i < solids.Count; i++)
            {
                Solid piece = solids[i];
                if (piece.Id == handle) continue;
                if (gx > piece.L && gx < piece.R && gy > piece.T && gy < piece.B) return true;
            }
            return false;
        }

        int scaleForGeometry = 6;

        /// <summary>The scale Update last ran at, for turning the window's rect into game pixels.</summary>
        int ScaleGuess(Block block) => scaleForGeometry;

        /// <summary>The leading face of the window in game pixels: centre, half-length, outward direction.</summary>
        void GeometryOf(Block block, PointF dir, out float x, out float y,
            out float rangeX, out float rangeY, out float direction, out int count)
        {
            int scale = scaleForGeometry;
            float l = (block.Home.Left + block.OffsetX * scale) / (float)scale;
            float t = (block.Home.Top + block.OffsetY * scale) / (float)scale;
            float w = (block.Home.Right - block.Home.Left) / (float)scale;
            float h = (block.Home.Bottom - block.Home.Top) / (float)scale;
            if (dir.X > 0f)
            { x = l + w - 1f; y = t + h / 2f; rangeX = 0f; rangeY = (h - 2f) * .5f; direction = 0f; count = (int)(h / 8f) * 4 + 2; }
            else if (dir.X < 0f)
            { x = l + 1f; y = t + h / 2f; rangeX = 0f; rangeY = (h - 2f) * .5f; direction = (float)Math.PI; count = (int)(h / 8f) * 4 + 2; }
            else if (dir.Y > 0f)
            { x = l + w / 2f; y = t + h - 1f; rangeX = (w - 2f) * .5f; rangeY = 0f; direction = (float)(Math.PI / 2.0); count = (int)(w / 8f) * 4 + 2; }
            else
            { x = l + w / 2f; y = t + 1f; rangeX = (w - 2f) * .5f; rangeY = 0f; direction = (float)(-Math.PI / 2.0); count = (int)(w / 8f) * 4 + 2; }
        }

        /// <summary>Update stores the scale here so the particle geometry can speak game pixels.</summary>
        public void SetScale(int scale) => scaleForGeometry = Math.Max(1, scale);

        // ===== the checks' windows into the state =====

        public Phase PhaseOf(IntPtr window)
            => blocks.TryGetValue(window, out Block block) ? block.Phase : Phase.Idle;

        public Point OffsetOf(IntPtr window)
            => blocks.TryGetValue(window, out Block block)
                ? new Point(block.OffsetX, block.OffsetY) : Point.Empty;

        public Point ShakeOf(IntPtr window)
            => blocks.TryGetValue(window, out Block block) ? block.Shake : Point.Empty;

        public Win32.RECT HomeOf(IntPtr window)
            => blocks.TryGetValue(window, out Block block) ? block.Home : default;

        public int ReturnLegsOf(IntPtr window)
            => blocks.TryGetValue(window, out Block block) ? block.ReturnStack.Count : 0;

        // ===== the same bookkeeping MoonWindows keeps, for the same reasons =====

        static bool WasToldTo(Block block, int x, int y)
        {
            foreach (Point told in block.Told) if (told.X == x && told.Y == y) return true;
            return false;
        }

        static void Remember(Block block, int x, int y)
        {
            block.Told[block.ToldNext] = new Point(x, y);
            block.ToldNext = (block.ToldNext + 1) % block.Told.Length;
        }

        static void Move(IntPtr window, int x, int y)
            => Win32.SetWindowPos(window, IntPtr.Zero, x, y, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE |
                Win32.SWP_ASYNCWINDOWPOS);

        static float Approach(float value, float target, float amount)
            => value > target ? Math.Max(value - amount, target) : Math.Min(value + amount, target);

        static int Towards(int value, int target, int step)
            => value < target ? Math.Min(value + step, target)
             : value > target ? Math.Max(value - step, target) : value;
    }
}
