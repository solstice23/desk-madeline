using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DeskMadeline
{
    // The right-click flyout's pages: what each tab holds and what each control changes. The
    // flyout itself (PetFlyout) knows nothing of Madeline; this is where the two meet.
    public partial class PetWindow
    {
        PetFlyout flyout;
        // What the flyout was left showing, kept in settings.txt: the tab, and which fold-outs
        // were open. A fold-out's key is its name in code, not its label, so a language change
        // does not forget it.
        int flyoutTab;
        readonly HashSet<string> flyoutOpen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Open the flyout at a point, replacing any that is open.</summary>
        void OpenFlyout(Point anchor) => ShowFlyout(anchor, null, false);

        /// <summary>Build it again where it is, for a change that alters its text.</summary>
        void RebuildFlyout()
        {
            if (flyout == null || flyout.IsDisposed) return;
            ShowFlyout(flyout.Location, flyout.Bounds, flyout.FromBottom);
        }

        void ShowFlyout(Point anchor, Rectangle? previous, bool fromBottom)
        {
            bool developer = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
            PetFlyout old = flyout;
            var opened = flyout = new PetFlyout(anchor, previous, fromBottom);
            BuildFlyoutPages(opened, developer);
            opened.SelectPage(flyoutTab);
            opened.TabChanged += tab => { if (flyoutTab != tab) { flyoutTab = tab; SaveSettings(); } };
            opened.FormClosed += (_, __) => { if (flyout == opened) flyout = null; };
            // Above every other window, and under her alone: in the topmost band with her when
            // she is always on top, and she and her things lifted back over it whenever it comes
            // to the front -- which showing it, and every click on it, does.
            opened.TopMost = AlwaysOnTop;
            opened.Activated += (_, __) => BeginInvoke(new Action(RaisePetWindows));
            old?.Close();
            opened.Show();
            RaisePetWindows();
        }

        /// <summary>Her windows, back to the top of their band, without taking the focus.</summary>
        /// <remarks>
        /// The picture of her and every entity is the one composition window, so they come
        /// above together; the input windows follow, so she can still be grabbed where she is
        /// drawn over the flyout. While she is stepping aside for a fullscreen window
        /// (FullscreenAvoidance), the top is just behind that window instead, so nothing that
        /// raises her lifts her over it.
        /// </remarks>
        void RaisePetWindows()
        {
            IntPtr top = AlwaysOnTop ? Win32.HWND_TOPMOST : IntPtr.Zero;   // IntPtr.Zero is HWND_TOP
            const uint flags = Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE;
            IntPtr behind = steppedBehind;
            bool leaveBand = false;
            if (behind != IntPtr.Zero)
            {
                bool behindTopmost = (Win32.GetWindowLong(behind, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) != 0;
                // Out of the topmost band herself, she is under a topmost window already, and
                // the top of her own band is still the right place for her. Otherwise hers go
                // straight after it, each one after the same window, which leaves them in the
                // order raising them would; an ordinary window means leaving the band first.
                if (AlwaysOnTop || !behindTopmost)
                {
                    top = behind;
                    leaveBand = AlwaysOnTop && !behindTopmost;
                }
            }
            var handles = new List<IntPtr>();
            if (compositionHost != null && compositionHost.IsHandleCreated) handles.Add(compositionHost.Handle);
            if (IsHandleCreated) handles.Add(Handle);
            void Collect<T>(IEnumerable<T> windows) where T : Control
            {
                foreach (T window in windows)
                    if (window.IsHandleCreated) handles.Add(window.Handle);
            }
            lock (gliderWindowLock) Collect(gliderWindows.Values);
            lock (seekerWindowLock) Collect(seekerWindows.Values);
            lock (pufferWindowLock) Collect(pufferWindows.Values);
            lock (bumperWindowLock) Collect(bumperWindows.Values);
            lock (theoWindowLock) Collect(theoWindows.Values);
            foreach (IntPtr handle in handles)
            {
                bool outOfBand = (Win32.GetWindowLong(handle, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) == 0;
                // Back from behind an ordinary window, a lone HWND_TOPMOST was seen to report
                // success and leave her input window out of the band; going through
                // HWND_NOTOPMOST first is what made it take. The same step leaves the band.
                if (leaveBand || (top == Win32.HWND_TOPMOST && outOfBand))
                    Win32.SetWindowPos(handle, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
                Win32.SetWindowPos(handle, top, 0, 0, 0, 0, flags);
            }
        }

        void BuildFlyoutPages(PetFlyout f, bool developer)
        {
            FlyoutPalette p = f.P;
            void Save() => SaveSettings();

            // ---- Madeline: her, and how she plays ----
            FlyoutStack page = f.AddPage(Loc.T("Tab.Madeline"), "\uE77B");
            var actions = page.Add(new FlyoutButtons(p));
            actions.Add(Loc.T("Menu.ResetPosition"), ResetPosition);
            actions.Add(Loc.T("Flyout.WakeUp"), () =>
            {
                introWakeUp = true;
                player.PlaySprite("wakeUp", true);
            });
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.Autonomy"), () => IdleAutonomyEnabled,
                on => { IdleAutonomyEnabled = on; Save(); }));
            // A developer's switch, beside what it debugs: there only when the flyout is opened
            // with Shift held, so the everyday right-click stays a user's.
            if (developer)
                page.Add(new FlyoutSwitch(p, Loc.T("Menu.AutonomyDebug"), () => IdleDebugWanted, SetIdleDebug));
            // Celeste's own two menus of changes to how she plays, under its own names.
            page.Add(new FlyoutHeader(p, Loc.T("Flyout.Assists")));
            int[] dashModes = { 0, 1, 2, -1 };
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.DashCount"), new[] { "0", "1", "2", "∞" },
                () => Array.IndexOf(dashModes, player.DashMode), i => { player.SetDashMode(dashModes[i]); Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.InfiniteStamina"), () => player.InfiniteStamina,
                on => { player.InfiniteStamina = on; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.Invincible"), () => player.Invincible,
                on => { player.Invincible = on; Save(); }));
            page.Add(new FlyoutHeader(p, Loc.T("Flyout.Variants")));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.SuperDash"), () => player.SuperDashing,
                on => { player.SuperDashing = on; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.Elytra"), () => player.ElytraEnabled,
                on => { player.ElytraEnabled = on; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.FreezeFrames"), () => player.FreezeFramesEnabled,
                on => { player.SetFreezeFramesEnabled(on); Save(); }));

            // ---- Environment: what she plays in ----
            page = f.AddPage(Loc.T("Tab.Environment"), "\uE909", Loc.T("Tab.EnvironmentFull"));
            int[] windowModes = { WindowsSolid, WindowsDream, WindowsWater, WindowsMoon, WindowsKevin };
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.WindowsAre"),
                new[] { Loc.T("Windows.Solid"), Loc.T("Windows.DreamBlocks"), Loc.T("Windows.Water"),
                    Loc.T("Windows.MoonBlocks"), Loc.T("Windows.KevinBlocks") },
                () => Array.IndexOf(windowModes, windowMode), i => SetWindowMode(windowModes[i])));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.IgnoreMaximizedWindows"), () => ignoreMaximizedWindows,
                on => { ignoreMaximizedWindows = on; pollCounter = 999; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.AvoidFullscreen"), () => avoidFullscreen,
                on => { avoidFullscreen = on; pollCounter = 999; Save(); }));
            // Which screen she wraps around only means anything while she wraps, so the switch
            // is there only then, folding out under the choice the way the hair swatches do.
            Action<bool> showOneMonitor = null;
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.EdgeWrap"),
                new[] { Loc.T("Common.Off"), Loc.T("Common.Horizontal"), Loc.T("Common.Vertical"), Loc.T("EdgeWrap.Both") },
                () => edgeWrapMode, i =>
                {
                    edgeWrapMode = i;
                    pollCounter = 999;
                    Save();
                    showOneMonitor(i != 0);
                }));
            f.Revealed(page, edgeWrapMode != 0, out showOneMonitor).Add(new FlyoutSwitch(p,
                Loc.T("Menu.EdgeWrapOneMonitor"), () => edgeWrapOneMonitor,
                on => { edgeWrapOneMonitor = on; pollCounter = 999; Save(); }));
            page.Add(new FlyoutHeader(p, Loc.T("Menu.Spawn")));
            var spawn = page.Add(new FlyoutButtons(p));
            spawn.Add(Loc.T("Entity.Jellyfish"), () => Interlocked.Increment(ref pendingGliderSpawns));
            spawn.Add(Loc.T("Entity.Seeker"), () => Interlocked.Increment(ref pendingSeekerSpawns));
            spawn.Add(Loc.T("Entity.Theo"), () => Interlocked.Increment(ref pendingTheoSpawns));
            spawn.Add(Loc.T("Entity.Bumper"), () => Interlocked.Increment(ref pendingBumperSpawns));
            spawn.Add(Loc.T("Entity.Puffer"), () => Interlocked.Increment(ref pendingPufferSpawns));
            // Clearing the lot is the common case and a click; one kind at a time folds out.
            page.Add(new FlyoutButtons(p)).Add(Loc.T("Flyout.RemoveAll"),
                () => Interlocked.Or(ref pendingRemoveAllEntities, 31));
            FlyoutStack remove = f.Expander(page, "remove", Loc.T("Flyout.RemoveByType"), flyoutOpen, Save);
            var removeKind = remove.Add(new FlyoutButtons(p));
            removeKind.Add(Loc.T("Entity.Jellyfish"), () => Interlocked.Or(ref pendingRemoveAllEntities, 1));
            removeKind.Add(Loc.T("Entity.Seeker"), () => Interlocked.Or(ref pendingRemoveAllEntities, 2));
            removeKind.Add(Loc.T("Entity.Theo"), () => Interlocked.Or(ref pendingRemoveAllEntities, 4));
            removeKind.Add(Loc.T("Entity.Bumper"), () => Interlocked.Or(ref pendingRemoveAllEntities, 8));
            removeKind.Add(Loc.T("Entity.Puffer"), () => Interlocked.Or(ref pendingRemoveAllEntities, 16));

            // ---- Controls ----
            page = f.AddPage(Loc.T("Tab.Controls"), "\uE765");
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.KeyboardControls"), () => InputEnabled,
                on => { InputEnabled = on; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.ControllerControls"), () => PadInputEnabled,
                on => { PadInputEnabled = on; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.RespondUnfocused"), () => InputWhenUnfocused,
                on => { InputWhenUnfocused = on; Save(); }));
            page.Add(new FlyoutNote(p, Loc.T("Settings.UnfocusedNote")));
            // MenuOptions.CreateGrabMode: Hold, Invert, Toggle, and a change resets the latch.
            FlyoutNote grabNote = null;
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.GrabMode"),
                new[] { Loc.T("GrabMode.Hold"), Loc.T("GrabMode.Invert"), Loc.T("GrabMode.Toggle") },
                () => (int)grabInput.Mode, i =>
                {
                    grabInput.SetMode((GrabModes)i);
                    Save();
                    grabNote.Text = Loc.T("Settings.Grab" + (GrabModes)i);
                    f.Refit();
                }));
            grabNote = page.Add(new FlyoutNote(p, Loc.T("Settings.Grab" + grabInput.Mode)));
            page.Add(new FlyoutButtons(p)).Add(Loc.T("Flyout.Bindings"), () => BindingsWindow.Show(bindings, padBindings));

            // ---- Look ----
            page = f.AddPage(Loc.T("Tab.Look"), "\uE790");
            var skinIds = new List<string> { SkinManager.DefaultId };
            var skinNames = new List<string> { Loc.T("Skin.Default") };
            foreach (var skin in skinManager.Skins) { skinIds.Add(skin.Id); skinNames.Add(skin.DisplayName); }
            var skinPicker = page.Add(new FlyoutPicker(p, Loc.T("Menu.Skin"), skinNames.ToArray(),
                () => skinIds.FindIndex(id => id.Equals(pendingSkinId ?? skinManager.Active?.Id ?? SkinManager.DefaultId,
                    StringComparison.OrdinalIgnoreCase)),
                i => pendingSkinId = skinIds[i]));
            page.Add(skinPicker.List);
            var skinTools = page.Add(new FlyoutButtons(p));
            skinTools.Add(Loc.T("Skin.Refresh"), () =>
            {
                // Re-scan the archives and reload the chosen skin as well: that matters when a
                // zip was replaced, not only when one was added.
                string activeId = skinManager.Active?.Id ?? SkinManager.DefaultId;
                skinManager.Discover();
                pendingSkinId = skinManager.Find(activeId)?.Id ?? SkinManager.DefaultId;
                Log("skins refreshed: " + skinManager.Skins.Count);
                BeginInvoke(new Action(RebuildFlyout));
            });
            skinTools.Add(Loc.T("Skin.OpenFolder"), OpenSkinsFolder);
            page.Add(new FlyoutSegmented(p, Loc.T("Flyout.Scale"), new[] { "2×", "3×", "4×", "5×", "6×", "8×" },
                () => Array.IndexOf(new[] { 2, 3, 4, 5, 6, 8 }, pendingScale > 0 ? pendingScale : GameScale),
                i => { pendingScale = new[] { 2, 3, 4, 5, 6, 8 }[i]; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.ParticleEffects"), () => ParticlesEnabled,
                on => { ParticlesEnabled = on; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.RespawnReversal"), () => player.RespawnReversalEnabled,
                on => { player.RespawnReversalEnabled = on; Save(); }));

            page.Add(new FlyoutHeader(p, Loc.T("Menu.Cosmetics")));
            page.Add(new FlyoutSwitch(p, Loc.T("Cosmetics.CatTail"), () => catTailEnabled,
                on => { catTailEnabled = on; catTailStarted = false; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Cosmetics.CatBangs"), () => catBangsEnabled,
                on => { catBangsEnabled = on; Save(); }));

            page.Add(new FlyoutHeader(p, Loc.T("Menu.HairColors")));
            // The three swatches only mean anything while custom colours are on, so they are
            // only there then -- folded out by the switch, not by a click of their own.
            Action<bool> showHairRows = null;
            page.Add(new FlyoutSwitch(p, Loc.T("Hair.UseCustom"), () => customHairColorsEnabled, on =>
            {
                customHairColorsEnabled = on;
                Save();
                showHairRows(on);
            }));
            FlyoutStack hairRows = f.Revealed(page, customHairColorsEnabled, out showHairRows);
            string[] colorNames = { Loc.T("Hair.NoDashes"), Loc.T("Hair.OneDash"), Loc.T("Hair.TwoDashes") };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                hairRows.Add(new FlyoutSwatch(p, colorNames[i], () => customHairColors[index], () =>
                {
                    using var dialog = new ColorDialog { Color = customHairColors[index], FullOpen = true, AnyColor = true };
                    if (dialog.ShowDialog(f) != DialogResult.OK) return;
                    customHairColors[index] = dialog.Color;
                    Save();
                }));
            }
            var hairReset = hairRows.Add(new FlyoutButtons(p));
            hairReset.Add(Loc.T("Hair.ResetCeleste"), () =>
            {
                customHairColors[0] = Player.UsedHairColor;
                customHairColors[1] = Player.NormalHairColor;
                customHairColors[2] = Player.TwoDashesHairColor;
                Save();
                foreach (Control row in hairRows.Controls) row.Invalidate();
            });

            page.Add(new FlyoutHeader(p, Loc.T("Menu.ExtraOverlays")));
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.Speedometer"),
                new[] { Loc.T("Common.Off"), Loc.T("Common.Horizontal"), Loc.T("Common.Vertical"), Loc.T("Speedometer.Both") },
                () => speedometerMode, i => { speedometerMode = i; Save(); }));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.Hitboxes"), () => hitboxesEnabled,
                on => { hitboxesEnabled = on; Save(); }));

            // ---- Sound ----
            page = f.AddPage(Loc.T("Tab.Sound"), "\uE767");
            // Silence has a reason, and this is where it is looked for: which of the two halves of
            // sound this machine has not got, and -- where it is the runtime, which can be
            // fetched -- the offer to go and get it. Read as the flyout is built.
            if (!soundEffects.Available)
            {
                page.Add(new FlyoutNote(p, Loc.T(soundEffects.Trouble ?? "Sfx.WhyUnavailable")));
                if (FmodDownload.Wanted)
                    page.Add(new FlyoutButtons(p)).Add(Loc.T("Sfx.Get"),
                        () => FmodDownload.Ask(f, () => { restartAfterExit = true; ExitApp(); }));
            }
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.SoundEffects"),
                new[] { Loc.T("Common.Off"), Loc.T("Sfx.OnlyWhenFocused"), Loc.T("Common.On") },
                () => soundEffects.Mode, i => { soundEffects.Mode = i; Save(); }));
            page.Add(new FlyoutSlider(p, Loc.T("Sfx.Volume"), 0, 100, 10, () => soundEffects.Volume,
                v => { soundEffects.Volume = v; Save(); }, v => v + "%"));
            var surfaces = SurfaceSounds();
            var surfacePicker = page.Add(new FlyoutPicker(p, Loc.T("Sfx.SurfaceMaterial"),
                surfaces.Select(s => s.Name).ToArray(),
                () => surfaces.FindIndex(s => s.Index == player.NormalSurfaceSoundIndex),
                i => { player.NormalSurfaceSoundIndex = surfaces[i].Index; Save(); }));
            page.Add(surfacePicker.List);

            // ---- App: the program's own affairs ----
            page = f.AddPage(Loc.T("Tab.App"), "\uE713");
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.AlwaysOnTop"), () => AlwaysOnTop, SetAlwaysOnTop));
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.LaunchAtSignIn"), StartupRegistration.IsEnabled, on =>
            {
                try { StartupRegistration.SetEnabled(on); }
                catch (Exception ex)
                {
                    FlyoutDialog.Tell(f, FlyoutDialog.Kind.Error, Loc.T("Startup.ChangeFailed"), ex.Message);
                }
            }));
            var languages = Loc.Languages.ToList();
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.Language"), languages.Select(l => l.NativeName).ToArray(),
                () => languages.FindIndex(l => l.Code.Equals(Loc.CurrentCode, StringComparison.OrdinalIgnoreCase)),
                i => ChangeLanguage(languages[i].Code)));
            // Every window reads the palette as it opens, so the flyout is built again to show
            // the change at once; the others pick it up the next time they open.
            page.Add(new FlyoutSegmented(p, Loc.T("Flyout.Theme"),
                new[] { Loc.T("Theme.System"), Loc.T("Theme.Light"), Loc.T("Theme.Dark") },
                () => FlyoutPalette.Theme, i =>
                {
                    FlyoutPalette.Theme = i;
                    Save();
                    BeginInvoke(new Action(RebuildFlyout));
                }));
            if (NeedsCelesteInstall)
            {
                // Why the pet wants to know, and where it is reading from: the first two things
                // to ask when she has no sprites or no sound.
                page.Add(new FlyoutNote(p, Loc.T("Celeste.Why") + "\n" +
                    Loc.Format("Celeste.InUse", CelesteInstall.Directory ?? Loc.T("Celeste.None"))));
                var celeste = page.Add(new FlyoutButtons(p));
                celeste.Add(Loc.T("Menu.CelesteDetect"), DetectCeleste);
                celeste.Add(Loc.T("Menu.CelesteChoose"), () =>
                {
                    string folder = AskForCelesteFolder();
                    if (folder == null ||
                        folder.Equals(CelesteInstall.Directory, StringComparison.OrdinalIgnoreCase)) return;
                    UseCelesteFolder(folder);
                });
            }

            // ---- Under every page ----
            f.AddFooterItem("\uE946", Loc.T("Menu.About"), ShowAbout);
            f.AddFooterItem("\uE895", Loc.T("Flyout.Updates"), CheckForUpdate);
            f.AddFooterItem("\uE7E8", Loc.T("Common.Exit"), ExitApp, right: true);
        }

        void SetWindowMode(int mode)
        {
            if (windowMode == WindowsMoon && mode != WindowsMoon) moonWindows.Restore();
            if (windowMode == WindowsKevin && mode != WindowsKevin) kevinWindows.Restore();
            windowMode = mode;
            pollCounter = 999;
            SaveSettings();
        }

        void SetAlwaysOnTop(bool on)
        {
            AlwaysOnTop = on;
            SaveSettings();
            Win32.SetWindowPos(Handle, AlwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            if (compositionHost != null)
                Win32.SetWindowPos(compositionHost.Handle, AlwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                    0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            // The switch is on the flyout: it follows her into or out of the topmost band.
            if (flyout != null && !flyout.IsDisposed)
            {
                flyout.TopMost = on;
                RaisePetWindows();
            }
            // Behind a fullscreen window, she stays behind it in whichever band she is in now.
            else if (steppedBehind != IntPtr.Zero) RaisePetWindows();
        }

        void SetIdleDebug(bool on)
        {
            IdleDebugWanted = on;
            if (on)
            {
                if (idleDebugWindow == null || idleDebugWindow.IsDisposed)
                {
                    idleDebugWindow = new IdleDebugWindow();
                    idleDebugWindow.Hidden = () => IdleDebugWanted = false;
                }
                idleDebugWindow.Show();
            }
            else idleDebugWindow?.Hide();
        }

        void OpenSkinsFolder()
        {
            string skinsDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins");
            try
            {
                System.IO.Directory.CreateDirectory(skinsDirectory);
                Process.Start(new ProcessStartInfo { FileName = skinsDirectory, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                FlyoutDialog.Tell(flyout, FlyoutDialog.Kind.Error, Loc.T("Skin.OpenFolderFailed"), ex.Message);
            }
        }

        /// <summary>Celeste's surface sound indices a floor can be given, with their names.</summary>
        static List<(int Index, string Name)> SurfaceSounds() => new List<(int, string)>
        {
            (1, Loc.T("Surface.Asphalt")), (2, Loc.T("Surface.Car")), (3, Loc.T("Surface.Dirt")),
            (4, Loc.T("Surface.Snow")), (5, Loc.T("Surface.Wood")), (6, Loc.T("Surface.StoneBridge")),
            (7, Loc.T("Surface.Girder")), (8, Loc.T("Surface.BrickDefault")), (9, Loc.T("Surface.ZipMover")),
            (11, Loc.T("Surface.InactiveDreamBlock")), (12, Loc.T("Surface.ActiveDreamBlock")),
            (13, Loc.T("Surface.ResortWood")), (14, Loc.T("Surface.ResortRoof")),
            (15, Loc.T("Surface.ResortSinkingPlatform")), (16, Loc.T("Surface.ResortBasementTile")),
            (17, Loc.T("Surface.ResortLinens")), (18, Loc.T("Surface.ResortBoxes")),
            (19, Loc.T("Surface.ResortBooks")), (20, Loc.T("Surface.ClutterDoor")),
            (21, Loc.T("Surface.ClutterSwitch")), (22, Loc.T("Surface.ResortElevator")),
            (23, Loc.T("Surface.CliffsideSnow")), (25, Loc.T("Surface.CliffsideGrass")),
            (27, Loc.T("Surface.CliffsideWhiteBlock")), (28, Loc.T("Surface.Gondola")),
            (32, Loc.T("Surface.AuroraGlass")), (33, Loc.T("Surface.Grass")),
            (35, Loc.T("Surface.CassetteBlock")), (36, Loc.T("Surface.CoreIce")),
            (37, Loc.T("Surface.CoreMoltenRock")), (40, Loc.T("Surface.Glitch")),
            (42, Loc.T("Surface.MoonCafe")), (43, Loc.T("Surface.DreamClouds")), (44, Loc.T("Surface.Moon")),
        };
    }
}
