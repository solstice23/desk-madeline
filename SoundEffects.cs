using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskMadeline
{
    /// <summary>
    /// Plays the original Celeste FMOD events from an installed copy of the game.
    /// No substitute samples are used: without compatible original banks, SFX stay silent.
    /// </summary>
    internal sealed class SoundEffects : IDisposable
    {
        const uint FmodVersion = 0x00011014; // Celeste ships FMOD Studio 1.10.14.

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemCreate(out IntPtr system, uint headerVersion);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemInitialize(IntPtr system, int maxChannels, uint studioFlags,
            uint coreFlags, IntPtr extraDriverData);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemLoadBankFile(IntPtr system,
            [MarshalAs(UnmanagedType.LPStr)] string fileName, uint flags, out IntPtr bank);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemGetEvent(IntPtr system,
            [MarshalAs(UnmanagedType.LPStr)] string path, out IntPtr description);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemGetBus(IntPtr system,
            [MarshalAs(UnmanagedType.LPStr)] string path, out IntPtr bus);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemUpdate(IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int StudioSystemRelease(IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EventDescriptionCreateInstance(IntPtr description, out IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EventInstanceStart(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EventInstanceStop(IntPtr instance, int mode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EventInstanceRelease(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int EventInstanceGetParameter(IntPtr instance,
            [MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr parameter);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int ParameterInstanceSetValue(IntPtr parameter, float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int BusSetVolume(IntPtr bus, float volume);

        readonly Func<bool> focused;
        readonly Dictionary<string, IntPtr> descriptions =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        IntPtr coreLibrary, studioLibrary, system, sfxBus;
        readonly Dictionary<object, IntPtr> loops = new Dictionary<object, IntPtr>();
        StudioSystemGetEvent getEvent;
        StudioSystemUpdate update;
        StudioSystemRelease releaseSystem;
        EventDescriptionCreateInstance createInstance;
        EventInstanceStart startInstance;
        EventInstanceStop stopInstance;
        EventInstanceRelease releaseInstance;
        EventInstanceGetParameter getParameter;
        ParameterInstanceSetValue setParameterValue;
        BusSetVolume setBusVolume;
        float appliedVolume = -1f;
        bool disposed;

        public volatile int Mode;       // 0 off, 1 focused only, 2 always
        public volatile int Volume;     // 0..100
        public bool Available => system != IntPtr.Zero;

        public SoundEffects(Func<bool> focused, int mode, int volume)
        {
            this.focused = focused;
            Mode = Math.Max(0, Math.Min(2, mode));
            Volume = Math.Max(0, Math.Min(100, volume));
            Initialize();
        }

        void Initialize()
        {
            try
            {
                // A bundled build carries the runtime and banks beside the exe, in the layout
                // they have inside Celeste; otherwise they come from an install, as ever.
                string game = HasBundledAudio(AppDomain.CurrentDomain.BaseDirectory)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : CelesteInstall.Directory;
                if (game == null)
                {
                    PetWindow.Log("SFX unavailable: Celeste installation not found");
                    return;
                }

                string native = Path.Combine(game, "lib64-win-x64");
                string corePath = Path.Combine(native, "fmod64.dll");
                string studioPath = Path.Combine(native, "fmodstudio.dll");
                string banks = Path.Combine(game, "Content", "FMOD", "Desktop");
                if (!File.Exists(corePath) || !File.Exists(studioPath) || !Directory.Exists(banks))
                {
                    PetWindow.Log("SFX unavailable: Celeste FMOD runtime or banks not found");
                    return;
                }

                coreLibrary = NativeLibrary.Load(corePath);
                studioLibrary = NativeLibrary.Load(studioPath);
                var createSystem = Export<StudioSystemCreate>("FMOD_Studio_System_Create");
                var initialize = Export<StudioSystemInitialize>("FMOD_Studio_System_Initialize");
                var loadBank = Export<StudioSystemLoadBankFile>("FMOD_Studio_System_LoadBankFile");
                getEvent = Export<StudioSystemGetEvent>("FMOD_Studio_System_GetEvent");
                var getBus = Export<StudioSystemGetBus>("FMOD_Studio_System_GetBus");
                update = Export<StudioSystemUpdate>("FMOD_Studio_System_Update");
                releaseSystem = Export<StudioSystemRelease>("FMOD_Studio_System_Release");
                createInstance = Export<EventDescriptionCreateInstance>("FMOD_Studio_EventDescription_CreateInstance");
                startInstance = Export<EventInstanceStart>("FMOD_Studio_EventInstance_Start");
                stopInstance = Export<EventInstanceStop>("FMOD_Studio_EventInstance_Stop");
                releaseInstance = Export<EventInstanceRelease>("FMOD_Studio_EventInstance_Release");
                getParameter = Export<EventInstanceGetParameter>("FMOD_Studio_EventInstance_GetParameter");
                setParameterValue = Export<ParameterInstanceSetValue>("FMOD_Studio_ParameterInstance_SetValue");
                setBusVolume = Export<BusSetVolume>("FMOD_Studio_Bus_SetVolume");

                Check(createSystem(out system, FmodVersion), "create system");
                Check(initialize(system, 64, 0, 0, IntPtr.Zero), "initialize system");
                foreach (string bank in new[]
                {
                    "Master Bank.bank", "Master Bank.strings.bank", "sfx.bank", "dlc_sfx.bank"
                })
                {
                    string path = Path.Combine(banks, bank);
                    if (File.Exists(path)) Check(loadBank(system, path, 0, out _), "load " + bank);
                }
                if (getBus(system, "bus:/gameplay_sfx", out sfxBus) != 0)
                    Check(getBus(system, "bus:/", out sfxBus), "get SFX bus");
                PetWindow.Log("SFX active: original Celeste FMOD banks from " + game);
            }
            catch (Exception ex)
            {
                PetWindow.Log("SFX unavailable: " + ex.Message);
                DisposeNative();
            }
        }

        T Export<T>(string name) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(studioLibrary, name));

        static void Check(int result, string operation)
        {
            if (result != 0) throw new InvalidOperationException(operation + " failed (FMOD result " + result + ")");
        }

        public void Update()
        {
            if (!Available) return;
            float target = Mode == 0 || (Mode == 1 && !focused()) ? 0f : Volume / 100f;
            if (Math.Abs(target - appliedVolume) > 0.0001f)
            {
                if (setBusVolume(sfxBus, target) == 0) appliedVolume = target;
            }
            update(system);
        }

        public void Play(string eventPath, string parameter = null, float value = 0f)
        {
            if (!Available || Mode == 0 || Volume == 0 || (Mode == 1 && !focused())) return;
            try
            {
                IntPtr description = Description(eventPath);
                Check(createInstance(description, out IntPtr instance), "create " + eventPath);
                if (parameter != null)
                {
                    Check(getParameter(instance, parameter, out IntPtr parameterInstance), "find " + parameter);
                    Check(setParameterValue(parameterInstance, value), "set " + parameter);
                }
                Check(startInstance(instance), "start " + eventPath);
                releaseInstance(instance); // FMOD retains it until the one-shot finishes.
            }
            catch (Exception ex) { PetWindow.Log("SFX event failed: " + ex.Message); }
        }

        public void StartLoop(string eventPath) => StartLoop("dream", eventPath);

        public void StartLoop(object key, string eventPath)
        {
            if (!Available || loops.ContainsKey(key)) return;
            try
            {
                Check(createInstance(Description(eventPath), out IntPtr instance), "create " + eventPath);
                Check(startInstance(instance), "start " + eventPath);
                loops[key] = instance;
            }
            catch (Exception ex)
            {
                PetWindow.Log("SFX loop failed: " + ex.Message);
            }
        }

        public void SetLoopParameter(object key, string parameter, float value)
        {
            if (!loops.TryGetValue(key, out IntPtr instance)) return;
            if (getParameter(instance, parameter, out IntPtr parameterInstance) == 0)
                setParameterValue(parameterInstance, value);
        }

        public void StopLoop() => StopLoop("dream");

        public void StopLoop(object key)
        {
            if (!loops.TryGetValue(key, out IntPtr instance)) return;
            stopInstance(instance, 0); // FMOD_STUDIO_STOP_ALLOWFADE, matching Audio.Stop.
            releaseInstance(instance);
            loops.Remove(key);
        }

        IntPtr Description(string eventPath)
        {
            if (descriptions.TryGetValue(eventPath, out IntPtr description)) return description;
            Check(getEvent(system, eventPath, out description), "find " + eventPath);
            descriptions[eventPath] = description;
            return description;
        }

        /// <summary>Whether a directory holds the FMOD runtime and banks in Celeste's layout.</summary>
        static bool HasBundledAudio(string directory)
            => File.Exists(Path.Combine(directory, "lib64-win-x64", "fmod64.dll")) &&
               Directory.Exists(Path.Combine(directory, "Content", "FMOD", "Desktop"));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (object key in new List<object>(loops.Keys)) StopLoop(key);
            DisposeNative();
        }

        void DisposeNative()
        {
            if (system != IntPtr.Zero)
            {
                try { releaseSystem?.Invoke(system); } catch { }
                system = IntPtr.Zero;
            }
            if (studioLibrary != IntPtr.Zero) { NativeLibrary.Free(studioLibrary); studioLibrary = IntPtr.Zero; }
            if (coreLibrary != IntPtr.Zero) { NativeLibrary.Free(coreLibrary); coreLibrary = IntPtr.Zero; }
        }
    }
}
