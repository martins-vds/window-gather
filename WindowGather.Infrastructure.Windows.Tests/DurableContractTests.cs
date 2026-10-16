using System.Text.Json;
using System.Text.Json.Nodes;
using WindowGather;

internal static class DurableContractTests
{
    private static readonly Display Original = new("physical-original", @"\\.\DISPLAY1", "Original",
        new(-1920, 0, 0, 1080), new(-1920, 0, 0, 1040));
    private static readonly Display Destination = new("physical-destination", @"\\.\DISPLAY2", "Destination",
        new(0, 0, 1920, 1080), new(0, 40, 1920, 1080));
    private static readonly Placement Placement = new(2, 3, new(-1, -1), new(-1, -1), new(100, 100, 900, 700));

    internal static void RunAll(Action<string, Action> run)
    {
        run("missing recovery file returns no session without creating a directory", () => WithDirectory(directory =>
        {
            Check(new SessionStore(directory).Load() is null);
            Check(!Directory.Exists(directory));
        }));
        foreach (PrimaryPhase phase in Enum.GetValues<PrimaryPhase>())
        {
            run($"primary recovery roundtrip preserves {phase} and follower ownership", () => WithDirectory(directory =>
            {
                var store = new SessionStore(directory);
                GatherSession expected = Session(phase);
                store.Save(expected);
                GatherSession actual = store.Load() ?? throw new InvalidOperationException("Missing recovery.");
                Check(actual.Version == expected.Version && actual.Token == expected.Token &&
                    actual.CreatedUtc == expected.CreatedUtc && actual.Target == expected.Target &&
                    actual.AutomaticReturnAttempted == expected.AutomaticReturnAttempted &&
                    actual.Windows.SequenceEqual(expected.Windows));
                PrimaryRecovery actualPrimary = actual.Primary ?? throw new InvalidOperationException("Missing primary recovery.");
                PrimaryRecovery expectedPrimary = expected.Primary!;
                Check(actualPrimary.OriginalId == expectedPrimary.OriginalId &&
                    actualPrimary.Phase == phase && actualPrimary.FollowNewWindows &&
                    actualPrimary.TrackingInterrupted && actualPrimary.RestoreStarted &&
                    actualPrimary.FollowTargetId == expectedPrimary.FollowTargetId &&
                    actualPrimary.Followers.SequenceEqual(expectedPrimary.Followers));
                Check(!File.Exists(Path.Combine(directory, "session.json.tmp")));
                store.Clear();
                Check(store.Load() is null);
            }));
        }
        run("primary-only recovery survives an empty baseline", () => WithDirectory(directory =>
        {
            var store = new SessionStore(directory);
            GatherSession session = Session();
            session.Windows.Clear();
            session.Primary = new(Original.Id, PrimaryPhase.Prepared, false);
            store.Save(session);
            GatherSession loaded = store.Load()!;
            Check(loaded.Windows.Count == 0 && loaded.Primary is
                { Phase: PrimaryPhase.Prepared, FollowNewWindows: false, Followers.Count: 0,
                    TrackingInterrupted: false, RestoreStarted: false, FollowTargetId: null });
        }));
        run("old recovery schema omits all new fields and roundtrips unchanged", () => WithDirectory(directory =>
        {
            var store = new SessionStore(directory);
            GatherSession session = Session();
            session.Primary = null;
            store.Save(session);
            string path = Path.Combine(directory, "session.json");
            JsonObject oldSchema = ReadObject(path);
            Check(oldSchema.Select(property => property.Key).SequenceEqual(
                ["Version", "Token", "CreatedUtc", "Target", "Windows"]));
            Check(oldSchema["Target"]!.AsObject().Select(property => property.Key).SequenceEqual(
                ["Id", "DeviceName", "Name", "Bounds", "WorkArea"]));
            Check(oldSchema["Target"]!["Bounds"]!.AsObject().Select(property => property.Key).SequenceEqual(
                ["Left", "Top", "Right", "Bottom"]));
            GatherSession loaded = store.Load()!;
            Check(loaded.Primary is null && !loaded.AutomaticReturnAttempted);
            store.Save(loaded);
            Check(JsonNode.DeepEquals(oldSchema, ReadObject(path)));
        }));
        run("automatic return attempt is durable without a primary recovery", () => WithDirectory(directory =>
        {
            var store = new SessionStore(directory);
            GatherSession session = Session(automaticReturnAttempted: true);
            session.Primary = null;
            store.Save(session);
            Check(store.Load() is { AutomaticReturnAttempted: true, Primary: null });
        }));

        (string Name, Action<JsonObject> Mutate)[] malformed =
        [
            ("missing original identity", primary => primary.Remove("OriginalId")),
            ("blank original identity", primary => primary["OriginalId"] = " "),
            ("unknown phase", primary => primary["Phase"] = 99),
            ("null followers", primary => primary["Followers"] = null),
            ("following disabled with owned followers", primary => primary["FollowNewWindows"] = false),
            ("blank follow target identity", primary => primary["FollowTargetId"] = " "),
            ("follower with invalid handle", primary => Follower(primary)["Handle"] = 0),
            ("follower with invalid process", primary => Follower(primary)["ProcessId"] = 0),
            ("follower with invalid process start", primary => Follower(primary)["ProcessStartedUtcTicks"] = -1),
            ("follower with blank class", primary => Follower(primary)["ClassName"] = " "),
            ("follower with missing placement", primary => Follower(primary)["Placement"] = null),
            ("follower with empty placement", primary => Follower(primary)["Placement"]!["Normal"]!["Right"] = 100),
            ("follower with wrong physical origin", primary => Follower(primary)["Origin"]!["Id"] = Original.Id),
            ("follower with invalid origin work area", primary => Follower(primary)["Origin"]!["WorkArea"]!["Right"] = 0),
            ("duplicate follower handles", primary => primary["Followers"]!.AsArray().Add(Follower(primary).DeepClone())),
            ("follower overlaps baseline ownership", primary => Follower(primary)["Handle"] = 101)
        ];
        foreach ((string name, Action<JsonObject> mutate) in malformed)
        {
            run("reject malformed primary recovery: " + name, () => WithDirectory(directory =>
            {
                var store = new SessionStore(directory);
                store.Save(Session());
                string path = Path.Combine(directory, "session.json");
                JsonObject json = ReadObject(path);
                mutate(json["Primary"]!.AsObject());
                File.WriteAllText(path, json.ToJsonString());
                Throws<InvalidDataException>(() => store.Load());
            }));
        }
        run("truncated recovery JSON fails explicitly", () => WithDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "session.json"), "{\"Primary\":");
            Throws<JsonException>(() => new SessionStore(directory).Load());
        }));

        run("gather preferences default off without creating a directory", () => WithDirectory(directory =>
        {
            Check(new GatherPreferencesStore(directory).Load() == new GatherPreferences(false, false));
            Check(!Directory.Exists(directory));
        }));
        foreach (GatherPreferences preferences in new GatherPreferences[] { new(), new(true, false), new(true, true) })
        {
            run($"gather preferences roundtrip primary={preferences.MakeDestinationPrimary} follow={preferences.FollowNewWindows}",
                () => WithDirectory(directory =>
                {
                    var store = new GatherPreferencesStore(directory);
                    store.Save(preferences);
                    Check(store.Load() == preferences);
                    Check(!File.Exists(Path.Combine(directory, "gather-options.json.tmp")));
                    store.Save(new());
                    Check(store.Load() == new GatherPreferences());
                }));
        }
        run("invalid preference save leaves durable options unchanged", () => WithDirectory(directory =>
        {
            var store = new GatherPreferencesStore(directory);
            store.Save(new(true, false));
            string path = Path.Combine(directory, "gather-options.json");
            string previous = File.ReadAllText(path);
            Throws<ArgumentException>(() => store.Save(new(false, true)));
            Check(File.ReadAllText(path) == previous && store.Load() == new GatherPreferences(true, false));
        }));
        run("invalid preference combination is rejected on load", () => WithDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gather-options.json"),
                """{"MakeDestinationPrimary":false,"FollowNewWindows":true}""");
            Throws<ArgumentException>(() => new GatherPreferencesStore(directory).Load());
        }));
        run("malformed preference JSON fails explicitly", () => WithDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gather-options.json"), "{\"FollowNewWindows\":");
            Throws<JsonException>(() => new GatherPreferencesStore(directory).Load());
        }));
        run("null preference JSON fails explicitly", () => WithDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gather-options.json"), "null");
            Throws<InvalidDataException>(() => new GatherPreferencesStore(directory).Load());
        }));
        run("oversized preference JSON is rejected before parsing", () => WithDirectory(directory =>
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gather-options.json"), new string(' ', 4097));
            Throws<InvalidDataException>(() => new GatherPreferencesStore(directory).Load());
        }));
        run("tracker dispatcher failure is reported without installing native hooks", () =>
        {
            int problems = 0;
            var tracker = new NativeWindowTracker(new NativeDesktop(),
                _ => throw new InvalidOperationException("Dispatcher unavailable."));
            tracker.Problem += _ => problems++;
            Throws<InvalidOperationException>(() => tracker.Start(Guid.NewGuid().ToString(), Destination));
            Check(problems == 1);
            Throws<InvalidOperationException>(() => tracker.Stop());
            Check(problems == 2);
            Throws<InvalidOperationException>(() => tracker.Dispose());
            Throws<ObjectDisposedException>(() => tracker.Start(Guid.NewGuid().ToString(), Destination));
        });
    }

    private static GatherSession Session(PrimaryPhase phase = PrimaryPhase.Applied, bool automaticReturnAttempted = false) => new()
    {
        Token = "e0bed065-0c5d-4ed0-a6d7-b16329b8adf1",
        CreatedUtc = new(2026, 10, 16, 17, 0, 0, DateTimeKind.Utc),
        Target = Destination,
        Windows = [new(101, 1001, 123456789, "BaselineWindow", Placement, Original)],
        AutomaticReturnAttempted = automaticReturnAttempted,
        Primary = new(Original.Id, phase, true)
        {
            TrackingInterrupted = true,
            RestoreStarted = true,
            FollowTargetId = Destination.Id,
            Followers = [new(202, 2002, 987654321, "FollowerWindow", Placement, Destination)]
        }
    };

    private static JsonObject Follower(JsonObject primary) => primary["Followers"]![0]!.AsObject();
    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void WithDirectory(Action<string> test)
    {
        // Ephemeral test data remains beneath the checkout, never in the user's real settings directory.
        string directory = Path.Combine(Environment.CurrentDirectory, ".native-contract-tests-" + Guid.NewGuid().ToString("N"));
        try { test(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Durable contract assertion failed.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
