# Fake-native window lifecycle checks

`TrackerTests.Run()` returns the number of passed tracker cases and throws a named failure.
The infrastructure test entry point can add that count to its existing checks.

The tracker tests inject a fake lifecycle API, capture/marker callbacks, a clock, and a
dedicated-thread dispatcher. They never install real hooks, enumerate user windows,
capture native window properties, or move windows. Cases cover new CREATE/SHOW pairs,
hidden baselines, duplicates, destruction/reuse races, restarts, filtering, bounded
candidate expiry/capacity, hook failures, exception containment, marker ownership,
and off-thread Start/Stop dispatch.

The production dispatcher must invoke inline when already on its UI owner thread.
Marker ownership transfers when `WindowOpened` is invoked, not when an asynchronous
subscriber eventually finishes; stopping the tracker does not clear delivered markers.
