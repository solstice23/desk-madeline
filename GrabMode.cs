using System;

namespace DeskMadeline
{
    /// <summary>Celeste's <c>GrabModes</c>, in its order: the settings file stores the number.</summary>
    internal enum GrabModes
    {
        Hold,
        Invert,
        Toggle
    }

    /// <summary>
    /// <c>Input.GrabCheck</c>, <c>Input.UpdateGrab</c> and <c>Input.ResetGrab</c>: what the grab
    /// binding means under each mode.
    /// </summary>
    /// <remarks>
    /// Vanilla runs <c>UpdateGrab</c> at the very end of <c>Celeste.Update</c>, after the scene has
    /// updated, so a Toggle press flips the latch only after the player has read it: she answers
    /// the frame after the press. The caller keeps that order by reading <see cref="Check"/>
    /// before calling <see cref="Update"/>. <c>Input.Grab</c> buffers 0s, so the press is the raw
    /// edge of the binding. <c>ResetGrab</c> runs in the <c>Player</c> constructor -- every
    /// respawn -- and when the mode is changed in the options menu.
    /// </remarks>
    internal sealed class GrabInput
    {
        GrabModes mode;
        bool grabToggle;

        public GrabModes Mode => mode;

        /// <summary>The Toggle latch, whatever the mode; only Toggle ever sets it.</summary>
        public bool Toggled => grabToggle;

        /// <summary><c>Input.GrabCheck</c>, given whether the grab binding is held.</summary>
        public bool Check(bool grabHeld) => mode switch
        {
            GrabModes.Invert => !grabHeld,
            GrabModes.Toggle => grabToggle,
            _ => grabHeld,
        };

        /// <summary><c>Input.UpdateGrab</c>, given whether the grab binding was pressed this frame.</summary>
        public void Update(bool grabPressed)
        {
            if (mode == GrabModes.Toggle && grabPressed) grabToggle = !grabToggle;
        }

        /// <summary><c>Input.ResetGrab</c>.</summary>
        public void Reset() => grabToggle = false;

        /// <summary><c>MenuOptions.CreateGrabMode</c>'s change handler: set, then reset.</summary>
        public void SetMode(GrabModes value)
        {
            mode = value;
            Reset();
        }
    }

    /// <summary>
    /// <c>GrabbyIcon</c>: the glove over her head while Toggle has grab latched on.
    /// </summary>
    /// <remarks>
    /// Vanilla adds it once per level (<c>LevelLoader</c>) at <c>Depth</c> -1000001, in front of
    /// everything in the gameplay layer. It is an entity, so a freeze frame, which skips the
    /// scene's update, holds its wiggle where it is. Its <c>Wiggler.Create(0.1f, 0.3f)</c> is
    /// ported member for member: <c>Start</c> sets the value to one, each update advances the
    /// sine by 2π·0.3 a second and the counter down by 1/0.1 a second, and the value is their
    /// product until the counter runs out. The level has no cutscenes here, so
    /// <c>InCutscene</c> is always false.
    /// </remarks>
    internal sealed class GrabbyIcon
    {
        const float WiggleDuration = 0.1f, WiggleFrequency = 0.3f;

        bool enabled;
        bool wigglerActive;
        float wigglerCounter, sineCounter;

        public bool Enabled => enabled;

        /// <summary><c>Wiggler.Value</c>.</summary>
        public float WigglerValue { get; private set; }

        /// <summary>What <c>Render</c> scales the glove by.</summary>
        public float Scale => 1f + WigglerValue * 0.2f;

        /// <summary>
        /// <c>GrabbyIcon.Update</c>. <paramref name="show"/> is its flag: a live player, Toggle
        /// mode and <c>Input.GrabCheck</c>.
        /// </summary>
        public void Update(bool show, float dt)
        {
            // base.Update: the wiggler is a component, and updates before the entity's own code.
            if (wigglerActive)
            {
                sineCounter += (float)Math.PI * 2f * WiggleFrequency * dt;
                wigglerCounter -= 1f / WiggleDuration * dt;
                if (wigglerCounter <= 0f)
                {
                    wigglerCounter = 0f;
                    wigglerActive = false;
                }
                WigglerValue = (float)Math.Cos(sineCounter) * wigglerCounter;
            }
            if (show != enabled)
            {
                enabled = show;
                // Wiggler.Start, without StartZero.
                wigglerCounter = 1f;
                sineCounter = 0f;
                WigglerValue = 1f;
                wigglerActive = true;
            }
        }
    }
}
