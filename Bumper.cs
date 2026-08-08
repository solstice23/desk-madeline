using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// Port of Celeste.Bumper: the pinball bumper of Chapter 6, which throws her off in
    /// whatever direction she came from and then sits out six tenths of a second.
    /// </summary>
    /// <remarks>
    /// Vanilla's bumper answers to one thing and one thing only -- it adds a PlayerCollider and
    /// nothing else -- so the crystal, the jellyfish and the seeker pass straight through it,
    /// here as there. It is not a solid either: it has a circle for a collider and no hitbox,
    /// which is why nothing stands on it.
    ///
    /// The desktop's own: it can be picked up and put somewhere else. A bumper in Celeste sits
    /// where the map put it, or slides between two points a map gave it; one that arrived from
    /// a menu has neither, so its anchor is wherever it was last dropped and it wanders about
    /// that on the same sine as any other.
    /// </remarks>
    public sealed class Bumper
    {
        /// <summary>Collider = new Circle(12f), centred on it.</summary>
        public const float Radius = 12f;
        const float RespawnTime = .6f;
        const float SineFrequency = .44f;
        /// <summary>SineWave.Counter wraps here, and Randomize picks somewhere in the first half.</summary>
        const float SineWrap = (float)(Math.PI * 8.0);

        /// <summary>Where it was put. The wander is measured from here.</summary>
        public PointF Anchor;

        /// <summary>
        /// Where it actually is: Bumper.UpdatePosition, three pixels across on the sine and two
        /// down on the half of it, which is what makes the wander an ellipse rather than a line.
        /// </summary>
        public PointF Pos => new PointF(
            Anchor.X + (float)Math.Sin(sineCounter) * 3f,
            Anchor.Y + (float)Math.Sin(sineCounter / 2f) * 2f);

        public bool Removed { get; private set; }
        public bool BeingDragged { get; private set; }
        /// <summary>False while it is sitting out a hit.</summary>
        public bool Ready => respawnTimer <= 0f;
        public string FrameId => animator.CurrentFrameId;
        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();

        /// <summary>How many times it has thrown her, so the shell knows a new one happened.</summary>
        public int Hits { get; private set; }

        /// <summary>
        /// Ambient particles wanted this frame. Vanilla asks Scene.OnInterval(0.05f) for them,
        /// and only while the bumper is up: one that has just been hit gives off nothing.
        /// </summary>
        public int AmbientPuffs { get; private set; }
        /// <summary>The way she went the last time it threw her, for the burst that goes with it.</summary>
        public PointF LaunchDirection { get; private set; }

        float sineCounter, respawnTimer, ambientTimer;
        readonly Animator animator;

        public Bumper(PointF at) : this(at, (float)(random.NextDouble() * Math.PI * 4.0)) { }

        /// <param name="sinePhase">
        /// SineWave.Randomize, handed in so a check can ask for a bumper that is not wandering.
        /// </param>
        public Bumper(PointF at, float sinePhase)
        {
            Anchor = at;
            sineCounter = sinePhase;
            animator = new Animator(BuildAnimations());
            animator.Play("idle", true);
        }

        static readonly Random random = new Random();

        /// <summary>
        /// The bumper's entry in the game's Sprites.xml, frame for frame: idle runs 0 to 33,
        /// a hit runs 34 to 42 and stops there, and coming back runs 42 to 44 into idle again.
        /// </summary>
        static Dictionary<string, Anim> BuildAnimations()
        {
            string[] Seq(int from, int to)
            {
                var frames = new string[to - from + 1];
                for (int i = from; i <= to; i++) frames[i - from] = "bumper/idle" + i.ToString("00");
                return frames;
            }
            return new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new Anim { Frames = Seq(0, 33), Delay = .06f, Loop = true },
                ["hit"] = new Anim { Frames = Seq(34, 42), Delay = .06f, Goto = "off" },
                ["off"] = new Anim { Frames = Seq(42, 42), Delay = .06f, Loop = true },
                ["on"] = new Anim { Frames = Seq(42, 44), Delay = .06f, Goto = "idle" },
            };
        }

        /// <summary>
        /// One frame: wander, count down a hit, and throw her if she is touching it.
        /// </summary>
        /// <remarks>
        /// The order is Bumper.Update's -- the timer, then the position -- and the collision is
        /// where Monocle would run it, after both, so that what she is tested against is where
        /// it has moved to this frame rather than where it was last.
        /// </remarks>
        public void Update(float dt, Player player)
        {
            AmbientPuffs = 0;
            if (respawnTimer > 0f)
            {
                respawnTimer -= dt;
                if (respawnTimer <= 0f)
                {
                    animator.Play("on");
                    SoundEvents.Enqueue(new PlayerSoundEvent(
                        "event:/game/06_reflection/pinballbumper_reset"));
                }
            }
            else
            {
                ambientTimer += dt;
                while (ambientTimer >= .05f) { ambientTimer -= .05f; AmbientPuffs++; }
            }
            sineCounter = (sineCounter + (float)(Math.PI * 2.0) * SineFrequency * dt) % SineWrap;
            animator.Update(dt);

            if (BeingDragged || player == null || player.IsDead || player.IsRespawning) return;
            if (respawnTimer > 0f || !player.OverlapsCircle(Pos, Radius)) return;

            respawnTimer = RespawnTime;
            SoundEvents.Enqueue(new PlayerSoundEvent(
                "event:/game/06_reflection/pinballbumper_hit"));
            // snapUp false: a bumper never sends her straight up for coming at it from above,
            // the way a puffer does. Whatever way she arrived is the way she leaves.
            LaunchDirection = player.ExplodeLaunch(Pos, snapUp: false);
            Hits++;
            animator.Play("hit", restart: true);
        }

        public void BeginDrag() => BeingDragged = true;

        /// <summary>
        /// Dropped somewhere else. It keeps no speed of its own -- a bumper does not fall, and
        /// the sine it wanders on carries on from wherever it was.
        /// </summary>
        public void DragTo(PointF to) => Anchor = to;

        public void EndDrag() => BeingDragged = false;

        public void Remove() => Removed = true;
    }
}
