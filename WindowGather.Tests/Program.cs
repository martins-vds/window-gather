using System.Text.Json;
using WindowGather;
using Shortcut = WindowGather.Shortcut;

internal static class Program
{
    private static int passed;
    private static readonly Display A = new("physical-a", @"\\.\DISPLAY1", "Desk",
        new(-1920, 0, 0, 1080), new(-1920, 0, 0, 1040));
    private static readonly Display B = new("physical-b", @"\\.\DISPLAY4", "Treadmill",
        new(0, 0, 1920, 1080), new(0, 40, 1920, 1080));

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Test("only imported windows are moved and restored", () =>
            {
                var (desktop, store, engine) = Setup();
                Check(engine.Gather(B).Completed == 1);
                Check(desktop.Moved.SequenceEqual([1L]) && engine.Session!.Windows.Count == 1);
                Check(engine.Restore().Completed == 1);
                Check(desktop.Restored.SequenceEqual([1L]) && engine.Session is null && store.Current is null);
            });
            Test("resident changes and new windows survive restore", () =>
            {
                var (desktop, _, engine) = Setup();
                engine.Gather(B);
                var changed = desktop.Windows[2] with { Placement = Place(new(400, 300, 900, 700)) };
                desktop.Windows[2] = changed;
                desktop.Windows[3] = Window(3, B);
                engine.Restore();
                Check(desktop.Windows[2] == changed && !desktop.Restored.Contains(3));
            });
            Test("second gather cannot overwrite recovery", () =>
            {
                var (_, store, engine) = Setup();
                engine.Gather(B);
                string token = store.Current!.Token;
                Throws<InvalidOperationException>(() => engine.Gather(A));
                Check(store.Current.Token == token);
            });
            Test("save failure causes no movement", () =>
            {
                var (desktop, store, engine) = Setup();
                store.FailWrites = true;
                Throws<IOException>(() => engine.Gather(B));
                Check(desktop.Moved.Count == 0 && engine.Session is null && desktop.Markers.Count == 0);
            });
            Test("missing display remains retryable", () =>
            {
                var (desktop, _, engine) = Setup();
                engine.Gather(B);
                desktop.Displays.Remove(A);
                Check(engine.Restore().Problems.Count == 1 && engine.Session!.Windows.Count == 1);
                desktop.Displays.Add(A);
                Check(engine.Restore().Completed == 1 && engine.Session is null);
            });
            Test("changed layout is not mistaken for original", () =>
            {
                var (desktop, _, engine) = Setup();
                engine.Gather(B);
                desktop.Displays[0] = A with { Bounds = new(-1920, -100, 0, 980) };
                Check(engine.Restore().Problems.Count == 1 && desktop.Restored.Count == 0);
            });
            Test("closed or replaced windows are not restored", () =>
            {
                var (desktop, _, engine) = Setup();
                engine.Gather(B);
                desktop.Markers.Clear();
                Check(engine.Restore().Skipped == 1 && desktop.Restored.Count == 0 && engine.Session is null);
            });
            Test("recovery survives utility restart", () =>
            {
                var (desktop, store, engine) = Setup();
                engine.Gather(B);
                var restarted = new GatherEngine(desktop, store);
                Check(restarted.Restore().Completed == 1);
            });
            Test("failed moves retain recovery", () =>
            {
                var (desktop, _, engine) = Setup();
                desktop.FailMove = true;
                Check(engine.Gather(B).Problems.Count == 1 && engine.Session is not null);
                desktop.FailMove = false;
                Check(engine.Restore().Completed == 1);
            });
            Test("partial restore retries only unresolved windows", () =>
            {
                var (desktop, _, engine) = Setup();
                desktop.Windows[3] = Window(3, A);
                engine.Gather(B);
                desktop.FailRestoreHandle = 3;
                Check(engine.Restore().Completed == 1 && engine.Session!.Windows.Single().Handle == 3);
                desktop.FailRestoreHandle = 0;
                Check(engine.Restore().Completed == 1 && desktop.Restored.SequenceEqual([1L, 3L]));
            });
            Test("failed completion write preserves marker and return point", () =>
            {
                var (desktop, store, engine) = Setup();
                engine.Gather(B);
                store.FailWrites = true;
                Throws<IOException>(() => engine.Restore());
                Check(engine.Session is not null && desktop.Markers.ContainsKey(1));
                store.FailWrites = false;
                Check(engine.Restore().Completed == 1);
            });
            Test("forget clears recovery without moving windows", () =>
            {
                var (desktop, store, engine) = Setup();
                engine.Gather(B);
                engine.Forget();
                Check(engine.Session is null && store.Current is null && desktop.Restored.Count == 0);
            });
            Test("no imports creates no session", () =>
            {
                var (desktop, store, engine) = Setup();
                desktop.Windows.Remove(1);
                Check(engine.Gather(B).Completed == 0 && engine.Session is null && store.Current is null);
            });
            Test("target changed before gather is rejected", () =>
            {
                var (desktop, _, engine) = Setup();
                desktop.Displays.Remove(B);
                Throws<InvalidOperationException>(() => engine.Gather(B));
                Check(desktop.Moved.Count == 0);
            });
            Test("negative coordinates and top taskbar convert correctly", () =>
            {
                Box fitted = Geometry.FitNormal(Place(new(-1820, 100, -1320, 500)), A, B);
                Check(fitted.Left == 100 && fitted.Top == 100 && fitted.Width == 500);
                Check(fitted.Top + 40 >= B.WorkArea.Top && fitted.Bottom + 40 <= B.WorkArea.Bottom);
            });
            Test("oversized windows fit work area", () =>
            {
                Box fitted = Geometry.FitNormal(Place(new(-4000, -4000, 4000, 4000)), A, B);
                Check(fitted == new Box(0, 0, 1920, 1040));
            });
            Test("minimized and maximized states remain in snapshot", () =>
            {
                var (desktop, _, engine) = Setup();
                desktop.Windows[1] = desktop.Windows[1] with { Placement = Place(new(-1800, 100, -1300, 600), 2) };
                engine.Gather(B);
                Check(engine.Session!.Windows.Single().Placement.ShowCommand == 2);
                engine.Restore();
                Check(desktop.Windows[1].Placement.ShowCommand == 2);
            });
            Test("disk recovery round trips and rejects corrupt data", TestDiskStore);
            TestShortcuts();
            if (args.Contains("--native")) TestNative();
            if (args.Contains("--ui")) TestUi();
            Console.WriteLine($"\n{passed} tests passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void TestDiskStore()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "teststate-" + Guid.NewGuid().ToString("N"));
        var store = new SessionStore(directory);
        var session = new GatherSession { Token = Guid.NewGuid().ToString(), Target = B, Windows = [Window(1, A)] };
        try
        {
            store.Save(session);
            GatherSession loaded = store.Load()!;
            Check(loaded.Token == session.Token && loaded.Windows.Single() == session.Windows.Single());
            File.WriteAllText(Path.Combine(directory, "session.json"), "{ broken");
            Throws<JsonException>(() => store.Load());
            File.WriteAllText(Path.Combine(directory, "session.json"),
                JsonSerializer.Serialize(session).Replace("\"Version\":1", "\"Version\":999"));
            Throws<InvalidDataException>(() => store.Load());
            store.Clear();
            Check(store.Load() is null);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "session.json"));
            File.Delete(Path.Combine(directory, "session.json.tmp"));
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private static void TestNative()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        var native = new NativeDesktop();
        IReadOnlyList<Display> displays = native.GetDisplays();
        Console.WriteLine($"Native display discovery: {displays.Count} displays.");
        foreach (Display display in displays) Console.WriteLine($"  {display}");
        Test("native hotkeys reserve replacements, reject conflicts, and release cleanly", () =>
        {
            using var owner = new Form { ShowInTaskbar = false };
            using var otherOwner = new Form { ShowInTaskbar = false };
            var backend = new NativeHotkeyBackend(() => owner.Handle);
            var competitor = new NativeHotkeyBackend(() => otherOwner.Handle);
            var store = new MemoryShortcutStore();
            var controller = new ShortcutController(backend, store);
            var settings = new ShortcutSettings(new(7, Keys.F23), new(7, Keys.F24));
            try
            {
                Check(controller.Activate(settings).Count == 0);
                Throws<System.ComponentModel.Win32Exception>(() => competitor.Register(100, settings.Gather));
                controller.Apply(new ShortcutSettings(settings.Restore, settings.Gather));
                Check(controller.Settings.Gather == settings.Restore);
                controller.Release();
                competitor.Register(100, settings.Gather);
                competitor.Unregister(100);
                Check(controller.GatherId is null && controller.RestoreId is null);
            }
            finally { controller.Release(); }
        });
        Test("identify labels match every physical display without taking focus", () =>
        {
            using var identifier = new DisplayIdentifier(100);
            nint foreground = GetForegroundWindow();
            identifier.ShowDisplays(displays);
            Check(identifier.Overlays.Count == displays.Count);
            Check(GetForegroundWindow() == foreground);
            for (int i = 0; i < displays.Count; i++)
            {
                Form overlay = identifier.Overlays[i];
                Check(overlay.Visible && !overlay.ShowInTaskbar && overlay.TopMost);
                Check(overlay.Text == displays[i].DeviceName.Replace(@"\\.\", ""));
                Check(native.CaptureWindow(overlay.Handle).Origin.Id == displays[i].Id);
                Check((Native.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x08000080) == 0x08000080);
                Check(overlay.Left >= displays[i].WorkArea.Left && overlay.Right <= displays[i].WorkArea.Right);
                Check(overlay.Top >= displays[i].WorkArea.Top && overlay.Bottom <= displays[i].WorkArea.Bottom);
            }
            Form[] previous = identifier.Overlays.ToArray();
            identifier.ShowDisplays(displays);
            Check(previous.All(f => f.IsDisposed) && identifier.Overlays.Count == displays.Count);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (identifier.Overlays.Count > 0 && watch.ElapsedMilliseconds < 2000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(identifier.Overlays.Count == 0, "Identification labels did not dismiss automatically.");
            identifier.ShowDisplays(displays);
            Form[] last = identifier.Overlays.ToArray();
            identifier.Dispose();
            Check(last.All(f => f.IsDisposed));
        });
        if (displays.Count < 2)
        {
            Console.WriteLine("SKIP native gather/restore: at least two displays required.");
            return;
        }
        Display origin = displays[0];
        foreach (Display target in displays.Skip(1))
        {
            Test($"native normal/minimized/maximized selective round trip to {target.DeviceName}", () =>
            {
                using Form normal = TestForm(origin, "normal");
                using Form minimized = TestForm(origin, "minimized");
                using Form maximized = TestForm(origin, "maximized");
                using Form resident = TestForm(target, "resident");
                minimized.WindowState = FormWindowState.Minimized;
                maximized.WindowState = FormWindowState.Maximized;
                Application.DoEvents();
                var handles = new[] { normal.Handle, minimized.Handle, maximized.Handle, resident.Handle };
                SavedWindow[] before = handles.Select(native.CaptureWindow).ToArray();
                for (int i = 0; i < handles.Length; i++)
                    Console.WriteLine($"  Synthetic {i}: {handles[i]}, state {before[i].Placement.ShowCommand}");
                var desktop = new OwnedDesktop(native, handles);
                var engine = new GatherEngine(desktop, new MemoryStore());
                OperationResult gathered = Pump(() => engine.Gather(target));
                Check(gathered.Completed == 3, string.Join("\n", gathered.Problems));
                foreach (nint handle in handles.Take(3))
                    Check(native.CaptureWindow(handle).Origin.Id == target.Id);
                Check(native.CaptureWindow(resident.Handle).Placement == before[3].Placement);
                resident.Location = new Point(resident.Left + 20, resident.Top + 20);
                Application.DoEvents();
                SavedWindow changedResident = native.CaptureWindow(resident.Handle);
                OperationResult restored = Pump(engine.Restore);
                Check(restored.Completed == 3, string.Join("\n", restored.Problems));
                for (int i = 0; i < 3; i++)
                {
                    SavedWindow after = native.CaptureWindow(handles[i]);
                    Check(after.Origin.Id == origin.Id);
                    Check(after.Placement.Normal == before[i].Placement.Normal);
                    Check(Geometry.State(after.Placement.ShowCommand) == Geometry.State(before[i].Placement.ShowCommand));
                }
                Check(native.CaptureWindow(resident.Handle).Placement == changedResident.Placement);
                Check(Pump(() => engine.Gather(target)).Completed == 3);
                normal.WindowState = FormWindowState.Maximized;
                minimized.WindowState = FormWindowState.Normal;
                maximized.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                OperationResult changedStateRestore = Pump(engine.Restore);
                Check(changedStateRestore.Completed == 3, string.Join("\n", changedStateRestore.Problems));
                for (int i = 0; i < 3; i++)
                {
                    SavedWindow after = native.CaptureWindow(handles[i]);
                    Check(after.Origin.Id == origin.Id);
                    Check(Geometry.State(after.Placement.ShowCommand) == Geometry.State(before[i].Placement.ShowCommand));
                }
            });
        }
    }

    private static T Pump<T>(Func<T> operation)
    {
        Task<T> task = Task.Run(operation);
        while (!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(5); }
        return task.GetAwaiter().GetResult();
    }

    private static void TestUi()
    {
        Control.CheckForIllegalCrossThreadCalls = true;
        Test("preview hit testing handles clicks, gaps, resizing, and selection lock", () =>
        {
            using var map = new MonitorMap { Size = new Size(720, 150), Displays = [A, B], SelectedId = A.Id };
            var regions = map.GetDisplayRegions().ToArray();
            int selectionEvents = 0;
            map.DisplaySelected += _ => selectionEvents++;
            Point center = Center(regions.Single(r => r.Display.Id == B.Id).Bounds);
            Check(map.HitTestDisplay(center)?.Id == B.Id);
            map.SelectDisplayAt(center);
            Check(map.SelectedId == B.Id && selectionEvents == 1);
            map.SelectDisplayAt(Point.Empty);
            Check(map.SelectedId == B.Id && selectionEvents == 1 && map.HitTestDisplay(Point.Empty) is null);
            map.Size = new Size(400, 300);
            center = Center(map.GetDisplayRegions().Single(r => r.Display.Id == A.Id).Bounds);
            Check(map.HitTestDisplay(center)?.Id == A.Id);
            map.Enabled = false;
            map.SelectDisplayAt(center);
            Check(map.SelectedId == B.Id && selectionEvents == 1);
            map.Enabled = true;
            map.SelectDisplayAt(center);
            Check(map.SelectedId == A.Id && selectionEvents == 2);
            map.Size = new Size(10, 10);
            Check(!map.GetDisplayRegions().Any() && map.HitTestDisplay(Point.Empty) is null);
        });
        Test("preview hit testing matches vertically arranged displays", () =>
        {
            Display above = B with { Bounds = new(0, -1080, 1920, 0) };
            using var map = new MonitorMap { Size = new Size(720, 150), Displays = [A, above] };
            foreach (var region in map.GetDisplayRegions())
                Check(map.HitTestDisplay(Center(region.Bounds))?.Id == region.Display.Id);
        });
        Test("native UI opens, exposes actions, and renders", () =>
        {
            var (desktop, _, engine) = Setup();
            var shortcutStore = new MemoryShortcutStore();
            var hotkeys = new FakeHotkeyBackend { Blocked = ShortcutSettings.Defaults.Gather };
            using var form = new MainForm(engine, shortcutStore, hotkeys);
            form.Show();
            Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            Check(form.Visible);
            Control[] controls = Descendants(form).ToArray();
            Button gather = controls.OfType<Button>().Single(b => b.Text.Contains("Gather to"));
            Button restore = controls.OfType<Button>().Single(b => b.Text.Contains("Restore borrowed"));
            Button identify = controls.OfType<Button>().Single(b => b.Text.Contains("Identify displays"));
            ComboBox picker = controls.OfType<ComboBox>().Single();
            MonitorMap preview = controls.OfType<MonitorMap>().Single();
            Check(gather.Enabled && !restore.Enabled);
            TextBox details = controls.OfType<TextBox>().Single();
            Check(!details.Visible);
            Label reference = controls.OfType<Label>().Single(l => l.Text.Contains("F11"));
            Check(reference.Visible && reference.Text.Contains("F12"));
            Label warning = controls.OfType<Label>().Single(l => l.Text.Contains("Simulated shortcut conflict"));
            Check(warning.Visible);
            Check(form.RectangleToClient(reference.RectangleToScreen(reference.ClientRectangle)).Bottom <= form.ClientSize.Height);
            var custom = new ShortcutSettings(new(6, Keys.G), new(6, Keys.R));
            form.ApplyShortcuts(custom);
            Check(shortcutStore.Current == custom && reference.Text.Contains("Ctrl + Shift + G") &&
                reference.Text.Contains("Ctrl + Shift + R"));
            Check(!warning.Visible);
            preview.SelectDisplayAt(Center(preview.GetDisplayRegions().Single(r => r.Display.Id == B.Id).Bounds));
            Check((picker.SelectedItem as Display)?.Id == B.Id && engine.Session is null);
            picker.SelectedItem = A;
            Check(preview.SelectedId == A.Id);
            picker.SelectedItem = B;
            identify.PerformClick();
            Check(Application.OpenForms.Cast<Form>().Count(f => f.Text is "DISPLAY1" or "DISPLAY4") == 2);
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "WindowGather-ui-ready.png"));
            }
            gather.PerformClick();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!restore.Enabled && watch.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(restore.Enabled && !gather.Enabled && engine.Session is not null);
            Check(!preview.Enabled && !picker.Enabled && identify.Enabled);
            preview.SelectDisplayAt(Center(preview.GetDisplayRegions().Single(r => r.Display.Id == A.Id).Bounds));
            Check(preview.SelectedId == B.Id && (picker.SelectedItem as Display)?.Id == B.Id);
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "WindowGather-ui-active.png"));
            }
            restore.PerformClick();
            watch.Restart();
            while ((engine.Session is not null || !gather.Enabled || restore.Enabled ||
                !preview.Enabled || !picker.Enabled) && watch.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(engine.Session is null && gather.Enabled && !restore.Enabled && preview.Enabled && picker.Enabled);
            Check(!details.Visible && reference.Visible && reference.Text.Contains("Ctrl + Shift + R"));
            Check(controls.OfType<Label>().Count(l => l.Text.StartsWith("Restored ")) == 1);
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "WindowGather-ui-restored.png"));
            }
            form.ClientSize = new Size(660, 780);
            Application.DoEvents();
            Check(form.RectangleToClient(reference.RectangleToScreen(reference.ClientRectangle)).Bottom <= form.ClientSize.Height);
            gather.PerformClick();
            watch.Restart();
            while (!restore.Enabled && watch.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(restore.Enabled);
            desktop.FailRestoreHandle = 1;
            restore.PerformClick();
            watch.Restart();
            while (!restore.Enabled && watch.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(restore.Enabled && details.Visible && details.Text.Length > 0);
            Rectangle detailBounds = details.Parent!.RectangleToClient(details.RectangleToScreen(details.ClientRectangle));
            Check(detailBounds.Top >= 0 && detailBounds.Bottom <= details.Parent.ClientSize.Height,
                "Operation errors must be scrolled into view.");
            Check(form.RectangleToClient(reference.RectangleToScreen(reference.ClientRectangle)).Bottom <= form.ClientSize.Height,
                "Errors must not push shortcuts off screen.");
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "WindowGather-ui-error.png"));
            }
            desktop.FailRestoreHandle = 0;
            restore.PerformClick();
            watch.Restart();
            while (!gather.Enabled && watch.ElapsedMilliseconds < 5000)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(gather.Enabled && !details.Visible);
        });
        Test("shortcut editor saves choices and reports invalid duplicates", () =>
        {
            ShortcutSettings? saved = null;
            using var dialog = new ShortcutForm(ShortcutSettings.Defaults, settings =>
            {
                settings.Validate();
                saved = settings;
            });
            dialog.Show();
            Application.DoEvents();
            var editors = Descendants(dialog).OfType<ShortcutEditor>().ToArray();
            var same = new Shortcut(6, Keys.G);
            editors[0].SetShortcut(same);
            editors[1].SetShortcut(same);
            Button save = Descendants(dialog).OfType<Button>().Single(b => b.Text.Contains("Save shortcuts"));
            save.PerformClick();
            Check(saved is null && dialog.Visible);
            Check(Descendants(dialog).OfType<Label>().Any(l => l.Text.Contains("different shortcuts")));
            Check(dialog.RectangleToClient(save.RectangleToScreen(save.ClientRectangle)).Bottom <= dialog.ClientSize.Height,
                "The Save button must be visible without scrolling.");
            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
            {
                dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, "WindowGather-shortcut-error.png"));
            }
            editors[1].SetShortcut(new Shortcut(6, Keys.R));
            save.PerformClick();
            Check(saved == new ShortcutSettings(same, new(6, Keys.R)) && !dialog.Visible);
        });
    }

    private static void TestShortcuts()
    {
        Test("shortcut settings validate modifiers, keys, and distinct actions", () =>
        {
            ShortcutSettings.Defaults.Validate();
            Check(new Shortcut(15, Keys.D7).ToString() == "Ctrl + Alt + Shift + Win + 7");
            Throws<InvalidDataException>(() => new Shortcut(0, Keys.G).Validate());
            Throws<InvalidDataException>(() => new Shortcut(4, Keys.G).Validate());
            Throws<InvalidDataException>(() => new Shortcut(19, Keys.G).Validate());
            Throws<InvalidDataException>(() => new Shortcut(3, Keys.ControlKey).Validate());
            Throws<InvalidDataException>(() => new ShortcutSettings(new(3, Keys.G), new(3, Keys.G)).Validate());
        });
        Test("shortcut settings persist and reject corrupt files", () =>
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "shortcut-test-" + Guid.NewGuid().ToString("N"));
            var store = new ShortcutStore(directory);
            var custom = new ShortcutSettings(new(6, Keys.G), new(9, Keys.F24));
            try
            {
                Check(store.Load() == ShortcutSettings.Defaults);
                store.Save(custom);
                Check(new ShortcutStore(directory).Load() == custom);
                File.WriteAllText(Path.Combine(directory, "shortcuts.json"), "{ broken");
                Throws<JsonException>(() => store.Load());
                File.WriteAllText(Path.Combine(directory, "shortcuts.json"), "{\"Gather\":null,\"Restore\":null}");
                Throws<InvalidDataException>(() => store.Load());
                File.WriteAllText(Path.Combine(directory, "shortcuts.json"), new string(' ', 4097));
                Throws<InvalidDataException>(() => store.Load());
                store.Save(ShortcutSettings.Defaults);
                Check(store.Load() == ShortcutSettings.Defaults);
            }
            finally
            {
                foreach (string file in new[] { "shortcuts.json", "shortcuts.json.tmp" })
                    File.Delete(Path.Combine(directory, file));
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        });
        Test("shortcut changes reserve, save, swap, and release old combinations", () =>
        {
            var backend = new FakeHotkeyBackend();
            var store = new MemoryShortcutStore();
            var controller = new ShortcutController(backend, store);
            Check(controller.Activate(ShortcutSettings.Defaults).Count == 0);
            int? oldGather = controller.GatherId;
            int? oldRestore = controller.RestoreId;
            var swapped = new ShortcutSettings(ShortcutSettings.Defaults.Restore, ShortcutSettings.Defaults.Gather);
            controller.Apply(swapped);
            Check(controller.GatherId == oldRestore && controller.RestoreId == oldGather);
            var custom = new ShortcutSettings(new(6, Keys.G), new(6, Keys.R));
            controller.Apply(custom);
            Check(store.Current == custom && controller.Settings == custom && backend.Registered.Count == 2);
            Check(!backend.Registered.ContainsKey(oldGather!.Value) && !backend.Registered.ContainsKey(oldRestore!.Value));
            controller.Release();
            Check(backend.Registered.Count == 0);
        });
        Test("shortcut conflict and disk failure retain working shortcuts", () =>
        {
            var backend = new FakeHotkeyBackend();
            var store = new MemoryShortcutStore();
            var controller = new ShortcutController(backend, store);
            controller.Activate(ShortcutSettings.Defaults);
            var custom = new ShortcutSettings(new(6, Keys.G), new(6, Keys.R));
            backend.Blocked = custom.Restore;
            Throws<System.ComponentModel.Win32Exception>(() => controller.Apply(custom));
            Check(controller.Settings == ShortcutSettings.Defaults && backend.Registered.Count == 2 &&
                store.Current == ShortcutSettings.Defaults);
            backend.Blocked = null;
            store.FailWrites = true;
            Throws<IOException>(() => controller.Apply(custom));
            Check(controller.Settings == ShortcutSettings.Defaults && backend.Registered.Count == 2);
            store.FailWrites = false;
            controller.Apply(custom);
            Check(controller.Settings == custom);
            controller.Release();
        });
        Test("startup shortcut conflicts are reported and can be repaired", () =>
        {
            var backend = new FakeHotkeyBackend { Blocked = ShortcutSettings.Defaults.Gather };
            var controller = new ShortcutController(backend, new MemoryShortcutStore());
            Check(controller.Activate(ShortcutSettings.Defaults).Count == 1);
            Check(controller.GatherId is null && controller.RestoreId is not null);
            controller.Apply(new ShortcutSettings(new(6, Keys.G), new(6, Keys.R)));
            Check(controller.GatherId is not null && controller.RestoreId is not null);
            controller.Release();
        });
    }

    private sealed class MemoryShortcutStore : IShortcutStore
    {
        public ShortcutSettings Current { get; private set; } = ShortcutSettings.Defaults;
        public bool FailWrites { get; set; }
        public ShortcutSettings Load() => Current;
        public void Save(ShortcutSettings settings)
        {
            if (FailWrites) throw new IOException("Simulated settings write failure");
            Current = settings;
        }
    }

    private sealed class FakeHotkeyBackend : IHotkeyBackend
    {
        public Dictionary<int, Shortcut> Registered { get; } = [];
        public Shortcut? Blocked { get; set; }
        public void Register(int id, Shortcut shortcut)
        {
            if (shortcut == Blocked || Registered.ContainsValue(shortcut))
                throw new System.ComponentModel.Win32Exception("Simulated shortcut conflict");
            Registered.Add(id, shortcut);
        }
        public void Unregister(int id) => Check(Registered.Remove(id));
    }

    private static Point Center(Rectangle rectangle) =>
        new(rectangle.Left + rectangle.Width / 2, rectangle.Top + rectangle.Height / 2);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control child in Descendants(control)) yield return child;
        }
    }

    private static Form TestForm(Display display, string name)
    {
        var form = new Form
        {
            Text = "Window Gather isolated test: " + name,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(display.WorkArea.Left + 80, display.WorkArea.Top + 80),
            Size = new Size(420, 280), ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static (FakeDesktop, MemoryStore, GatherEngine) Setup()
    {
        var desktop = new FakeDesktop();
        var store = new MemoryStore();
        return (desktop, store, new GatherEngine(desktop, store));
    }

    private static SavedWindow Window(long handle, Display display) =>
        new(handle, 123, 1000, "TestWindow", Place(new(display.Bounds.Left + 100, 100, display.Bounds.Left + 600, 500)), display);
    private static Placement Place(Box box, uint state = 1) => new(0, state, new(-1, -1), new(-1, -1), box);
    private static void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }
    private static void Check(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed. " + message);
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class MemoryStore : ISessionStore
    {
        public GatherSession? Current { get; private set; }
        public bool FailWrites { get; set; }
        public GatherSession? Load() => Current;
        public void Save(GatherSession session)
        {
            if (FailWrites) throw new IOException("Simulated disk failure");
            Current = session;
        }
        public void Clear()
        {
            if (FailWrites) throw new IOException("Simulated disk failure");
            Current = null;
        }
    }

    private sealed class FakeDesktop : IDesktop
    {
        public List<Display> Displays { get; } = [A, B];
        public Dictionary<long, SavedWindow> Windows { get; } = new() { [1] = Window(1, A), [2] = Window(2, B) };
        public Dictionary<long, string> Markers { get; } = [];
        public List<long> Moved { get; } = [];
        public List<long> Restored { get; } = [];
        public bool FailMove { get; set; }
        public long FailRestoreHandle { get; set; }
        public IReadOnlyList<Display> GetDisplays() => Displays;
        public Display GetPointerDisplay() => B;
        public WindowScan CaptureWindows(string token, Display target)
        {
            var windows = Windows.Values.Where(w => w.Origin.Id != target.Id).ToList();
            foreach (SavedWindow window in windows) Markers[window.Handle] = token;
            return new(windows, []);
        }
        public bool Matches(SavedWindow window, string token) =>
            Windows.ContainsKey(window.Handle) && Markers.GetValueOrDefault(window.Handle) == token;
        public void Mark(SavedWindow window, string token)
        {
            if (!Matches(window, token)) throw new InvalidOperationException("Window lifetime changed");
        }
        public void Unmark(SavedWindow window, string token) => Markers.Remove(window.Handle);
        public void Gather(SavedWindow window, Display target)
        {
            if (FailMove) throw new InvalidOperationException("Simulated movement failure");
            Moved.Add(window.Handle);
            Windows[window.Handle] = window with { Origin = target };
        }
        public void Restore(SavedWindow window)
        {
            if (FailRestoreHandle == window.Handle) throw new InvalidOperationException("Simulated restoration failure");
            Restored.Add(window.Handle);
            Windows[window.Handle] = window;
        }
    }

    private sealed class OwnedDesktop(NativeDesktop native, nint[] handles) : IDesktop
    {
        public IReadOnlyList<Display> GetDisplays() => native.GetDisplays();
        public Display GetPointerDisplay() => native.GetPointerDisplay();
        public WindowScan CaptureWindows(string token, Display target)
        {
            var windows = handles.Select(native.CaptureWindow).Where(w => w.Origin.Id != target.Id).ToList();
            foreach (SavedWindow window in windows) SetProp(new nint(window.Handle), $"WindowGather.{token}", 1);
            return new(windows, []);
        }
        public bool Matches(SavedWindow window, string token) => native.Matches(window, token);
        public void Mark(SavedWindow window, string token) => native.Mark(window, token);
        public void Unmark(SavedWindow window, string token) => native.Unmark(window, token);
        public void Gather(SavedWindow window, Display target) => native.Gather(window, target);
        public void Restore(SavedWindow window) => native.Restore(window);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool SetProp(nint handle, string name, nint value);
    }
}
