import assert from "node:assert/strict";
import test from "node:test";
import { RunCoordinator } from "./run-coordinator";

test("replacement cancels the previous global run and stale cleanup cannot clear the new run", () => {
    const coordinator = new RunCoordinator<{ cancelled: boolean; cancel: () => void }>();
    const cancelled: string[] = [];
    const first = { cancelled: false, cancel: () => cancelled.push("first") };
    const second = { cancelled: false, cancel: () => cancelled.push("second") };

    coordinator.replace(first);
    coordinator.replace(second);

    assert.equal(first.cancelled, true);
    assert.deepEqual(cancelled, ["first"]);
    coordinator.clear(first);
    assert.equal(coordinator.current, second);
    coordinator.cancel();
    assert.equal(second.cancelled, true);
    assert.deepEqual(cancelled, ["first", "second"]);
});

test("clear is identity-safe for completed stale runs", () => {
    const coordinator = new RunCoordinator<{ cancelled: boolean; cancel: () => void }>();
    const first = { cancelled: false, cancel: () => undefined };
    const second = { cancelled: false, cancel: () => undefined };
    coordinator.replace(first);
    coordinator.replace(second);
    coordinator.clear(first);
    assert.equal(coordinator.current, second);
});

test("nextReservation cancels any active run and invalidates earlier reservations", () => {
    const coordinator = new RunCoordinator<{ cancelled: boolean; cancel: () => void }>();
    const cancelled: string[] = [];
    const first = { cancelled: false, cancel: () => cancelled.push("first") };
    coordinator.replace(first);

    const res1 = coordinator.nextReservation();
    assert.equal(first.cancelled, true);
    assert.deepEqual(cancelled, ["first"]);
    assert.equal(coordinator.isReservationCurrent(res1), true);

    const res2 = coordinator.nextReservation();
    assert.equal(coordinator.isReservationCurrent(res1), false);
    assert.equal(coordinator.isReservationCurrent(res2), true);
});
