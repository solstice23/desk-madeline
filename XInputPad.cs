using System;
using System.Runtime.InteropServices;

namespace DeskMadeline
{
    /// <summary>
    /// Controller buttons, mirroring the XNA <c>Buttons</c> members Celeste can bind
    /// (Monocle MInput.GamePadData.Check). Stick directions are separate members
    /// because Celeste binds them as buttons with a per-action threshold.
    /// </summary>
    internal enum PadButton
    {
        None = 0,
        A,
        B,
        X,
        Y,
        LeftShoulder,
        RightShoulder,
        LeftTrigger,
        RightTrigger,
        LeftStick,
        RightStick,
        Start,
        Back,
        DPadUp,
        DPadDown,
        DPadLeft,
        DPadRight,
        LeftThumbstickUp,
        LeftThumbstickDown,
        LeftThumbstickLeft,
        LeftThumbstickRight,
        RightThumbstickUp,
        RightThumbstickDown,
        RightThumbstickLeft,
        RightThumbstickRight
    }

    /// <summary>One polled controller snapshot, already normalized the way XNA reports it.</summary>
    internal readonly struct PadState
    {
        public readonly bool Connected;
        readonly ushort buttons;
        readonly float leftTrigger, rightTrigger;
        readonly float leftX, leftY, rightX, rightY;

        public PadState(bool connected, ushort buttons, float leftTrigger, float rightTrigger,
            float leftX, float leftY, float rightX, float rightY)
        {
            Connected = connected;
            this.buttons = buttons;
            this.leftTrigger = leftTrigger;
            this.rightTrigger = rightTrigger;
            this.leftX = leftX;
            this.leftY = leftY;
            this.rightX = rightX;
            this.rightY = rightY;
        }

        bool Mask(ushort mask) => (buttons & mask) != 0;

        /// <summary>
        /// Port of Monocle MInput.GamePadData.Check(Buttons, threshold): digital buttons ignore the
        /// threshold, triggers and stick directions compare against it.
        /// </summary>
        public bool Check(PadButton button, float threshold)
        {
            if (!Connected) return false;
            switch (button)
            {
                case PadButton.A: return Mask(XInputPad.GamepadA);
                case PadButton.B: return Mask(XInputPad.GamepadB);
                case PadButton.X: return Mask(XInputPad.GamepadX);
                case PadButton.Y: return Mask(XInputPad.GamepadY);
                case PadButton.LeftShoulder: return Mask(XInputPad.GamepadLeftShoulder);
                case PadButton.RightShoulder: return Mask(XInputPad.GamepadRightShoulder);
                case PadButton.LeftStick: return Mask(XInputPad.GamepadLeftThumb);
                case PadButton.RightStick: return Mask(XInputPad.GamepadRightThumb);
                case PadButton.Start: return Mask(XInputPad.GamepadStart);
                case PadButton.Back: return Mask(XInputPad.GamepadBack);
                case PadButton.DPadUp: return Mask(XInputPad.GamepadDPadUp);
                case PadButton.DPadDown: return Mask(XInputPad.GamepadDPadDown);
                case PadButton.DPadLeft: return Mask(XInputPad.GamepadDPadLeft);
                case PadButton.DPadRight: return Mask(XInputPad.GamepadDPadRight);
                case PadButton.LeftTrigger: return leftTrigger >= threshold;
                case PadButton.RightTrigger: return rightTrigger >= threshold;
                case PadButton.LeftThumbstickUp: return leftY >= threshold;
                case PadButton.LeftThumbstickDown: return leftY <= -threshold;
                case PadButton.LeftThumbstickLeft: return leftX <= -threshold;
                case PadButton.LeftThumbstickRight: return leftX >= threshold;
                case PadButton.RightThumbstickUp: return rightY >= threshold;
                case PadButton.RightThumbstickDown: return rightY <= -threshold;
                case PadButton.RightThumbstickLeft: return rightX <= -threshold;
                case PadButton.RightThumbstickRight: return rightX >= threshold;
                default: return false;
            }
        }
    }

    /// <summary>
    /// XInput polling. Desktop-specific: Celeste reads one configured gamepad slot through
    /// FNA, while the pet has no gamepad-select UI, so all four XInput slots are merged into
    /// a single state and any attached controller drives Madeline.
    /// </summary>
    internal static class XInputPad
    {
        public const ushort GamepadDPadUp = 0x0001;
        public const ushort GamepadDPadDown = 0x0002;
        public const ushort GamepadDPadLeft = 0x0004;
        public const ushort GamepadDPadRight = 0x0008;
        public const ushort GamepadStart = 0x0010;
        public const ushort GamepadBack = 0x0020;
        public const ushort GamepadLeftThumb = 0x0040;
        public const ushort GamepadRightThumb = 0x0080;
        public const ushort GamepadLeftShoulder = 0x0100;
        public const ushort GamepadRightShoulder = 0x0200;
        public const ushort GamepadA = 0x1000;
        public const ushort GamepadB = 0x2000;
        public const ushort GamepadX = 0x4000;
        public const ushort GamepadY = 0x8000;

        // XInput SDK deadzone constants, normalized the way XNA's default
        // GamePadDeadZone.IndependentAxes reports thumbstick axes.
        const float LeftThumbDeadzone = 7849f / 32767f;
        const float RightThumbDeadzone = 8689f / 32767f;

        const uint ErrorSuccess = 0;
        const int SlotCount = 4;
        // Querying an empty XInput slot is slow, so rescan disconnected slots on a timer.
        const int RescanIntervalMs = 2000;

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint XInputGetState14(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern uint XInputGetState910(uint dwUserIndex, out XINPUT_STATE pState);

        static readonly object sync = new object();
        static readonly bool[] slotConnected = new bool[SlotCount];
        // Unchecked int differences stay correct across the TickCount wrap, and the initial
        // value is one interval in the past so the first poll always scans every slot.
        static int lastRescanTick = unchecked(Environment.TickCount - RescanIntervalMs);
        static bool triedLegacy;
        static bool unavailable;

        static uint GetState(uint slot, out XINPUT_STATE state)
        {
            if (unavailable) { state = default; return 1u; }
            if (!triedLegacy)
            {
                try { return XInputGetState14(slot, out state); }
                catch (DllNotFoundException) { triedLegacy = true; }
                catch (EntryPointNotFoundException) { triedLegacy = true; }
            }
            try { return XInputGetState910(slot, out state); }
            catch (DllNotFoundException) { unavailable = true; }
            catch (EntryPointNotFoundException) { unavailable = true; }
            state = default;
            return 1u;
        }

        /// <summary>Reads every attached controller and merges them into one state.</summary>
        public static PadState Poll()
        {
            lock (sync)
            {
                int now = Environment.TickCount;
                bool rescan = unchecked(now - lastRescanTick) >= RescanIntervalMs;
                if (rescan) lastRescanTick = now;

                bool connected = false;
                ushort buttons = 0;
                float leftTrigger = 0f, rightTrigger = 0f;
                float leftX = 0f, leftY = 0f, rightX = 0f, rightY = 0f;

                for (uint slot = 0; slot < SlotCount; slot++)
                {
                    if (!slotConnected[slot] && !rescan) continue;
                    if (GetState(slot, out XINPUT_STATE state) != ErrorSuccess)
                    {
                        slotConnected[slot] = false;
                        continue;
                    }
                    slotConnected[slot] = true;
                    connected = true;
                    buttons |= state.Gamepad.wButtons;
                    leftTrigger = Math.Max(leftTrigger, state.Gamepad.bLeftTrigger / 255f);
                    rightTrigger = Math.Max(rightTrigger, state.Gamepad.bRightTrigger / 255f);
                    Merge(ref leftX, Axis(state.Gamepad.sThumbLX, LeftThumbDeadzone));
                    Merge(ref leftY, Axis(state.Gamepad.sThumbLY, LeftThumbDeadzone));
                    Merge(ref rightX, Axis(state.Gamepad.sThumbRX, RightThumbDeadzone));
                    Merge(ref rightY, Axis(state.Gamepad.sThumbRY, RightThumbDeadzone));
                }

                return new PadState(connected, buttons, leftTrigger, rightTrigger,
                    leftX, leftY, rightX, rightY);
            }
        }

        static void Merge(ref float target, float value)
        {
            if (Math.Abs(value) > Math.Abs(target)) target = value;
        }

        /// <summary>XNA GamePadDeadZone.IndependentAxes: cut the deadzone, then rescale to full range.</summary>
        static float Axis(short raw, float deadzone)
        {
            float value = Math.Max(-1f, raw / 32767f);
            if (value < -deadzone) value += deadzone;
            else if (value > deadzone) value -= deadzone;
            else return 0f;
            return value / (1f - deadzone);
        }
    }
}
