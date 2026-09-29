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
            old?.Close();
            opened.Show();
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
                animator.Play("wakeUp", true);
            });
            page.Add(new FlyoutSwitch(p, Loc.T("Menu.Autonomy"), () => IdleAutonomyEnabled,
                on => { IdleAutonomyEnabled = on; Save(); }));
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
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.EdgeWrap"),
                new[] { Loc.T("Common.Off"), Loc.T("Common.Horizontal"), Loc.T("Common.Vertical"), Loc.T("EdgeWrap.Both") },
                () => edgeWrapMode, i => { edgeWrapMode = i; pollCounter = 999; Save(); }));
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
            // only there then -- shown by the switch, not by a click of their own.
            var hairRows = new List<Control>();
            page.Add(new FlyoutSwitch(p, Loc.T("Hair.UseCustom"), () => customHairColorsEnabled, on =>
            {
                customHairColorsEnabled = on;
                Save();
                foreach (Control row in hairRows) row.Visible = on;
                f.Refit();
            }));
            string[] colorNames = { Loc.T("Hair.NoDashes"), Loc.T("Hair.OneDash"), Loc.T("Hair.TwoDashes") };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                hairRows.Add(page.Add(new FlyoutSwatch(p, colorNames[i], () => customHairColors[index], () =>
                {
                    using var dialog = new ColorDialog { Color = customHairColors[index], FullOpen = true, AnyColor = true };
                    if (dialog.ShowDialog(f) != DialogResult.OK) return;
                    customHairColors[index] = dialog.Color;
                    Save();
                })));
            }
            var hairReset = page.Add(new FlyoutButtons(p));
            hairReset.Add(Loc.T("Hair.ResetCeleste"), () =>
            {
                customHairColors[0] = Player.UsedHairColor;
                customHairColors[1] = Player.NormalHairColor;
                customHairColors[2] = Player.TwoDashesHairColor;
                Save();
                foreach (Control row in hairRows) row.Invalidate();
            });
            hairRows.Add(hairReset);
            foreach (Control row in hairRows) row.Visible = customHairColorsEnabled;

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
                    MessageBox.Show(ex.Message, Loc.T("Startup.ChangeFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
            var languages = Loc.Languages.ToList();
            page.Add(new FlyoutSegmented(p, Loc.T("Menu.Language"), languages.Select(l => l.NativeName).ToArray(),
                () => languages.FindIndex(l => l.Code.Equals(Loc.CurrentCode, StringComparison.OrdinalIgnoreCase)),
                i => ChangeLanguage(languages[i].Code)));
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
            // A developer's switch: there only when the flyout is opened with Shift held, so the
            // everyday right-click stays a user's.
            if (developer)
                page.Add(new FlyoutSwitch(p, Loc.T("Menu.AutonomyDebug"), () => IdleDebugWanted, SetIdleDebug));

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
                MessageBox.Show(ex.Message, Loc.T("Skin.OpenFolderFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
