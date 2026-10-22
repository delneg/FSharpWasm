import assert from "node:assert/strict";
import test from "node:test";
import { createReactorOracleImports } from "./reactor-oracle-imports.mjs";

for (const [target, canonicalPrefix] of [
  ["wasm32", "cm32p2"],
  ["wasm64", "cm64p2"],
]) {
  test(`creates the complete ${target} reactor oracle import contract`, () => {
    const imports = createReactorOracleImports(target);

    assert.deepEqual(Object.keys(imports).sort(), [
      `${canonicalPrefix}|netwasm:runtime/reactor-host@1`,
      `${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`,
      `${canonicalPrefix}|wasi:io/poll@0.2`,
    ]);
    assert.equal(imports[`${canonicalPrefix}|wasi:io/poll@0.2`].poll(1, 1, 8), undefined);
    assert.equal(imports[`${canonicalPrefix}|wasi:io/poll@0.2`].pollable_drop(1), undefined);
    assert.equal(
      imports[`${canonicalPrefix}|netwasm:runtime/reactor-host@1`].watch(7, 11),
      undefined,
    );
    assert.equal(
      imports[`${canonicalPrefix}|netwasm:runtime/reactor-host@1`].cancel(7),
      undefined,
    );
    assert.equal(
      typeof imports[`${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`].now(),
      "bigint",
    );
    assert.equal(
      imports[`${canonicalPrefix}|wasi:clocks/monotonic-clock@0.2`]["subscribe-duration"](0n),
      1,
    );
  });

  test(`forwards ${target} reactor scheduling to supplied hooks`, () => {
    const events = [];
    const imports = createReactorOracleImports(target, {
      watch: (pollable, token) => events.push(["watch", pollable, token]),
      cancel: token => events.push(["cancel", token]),
    });
    const reactor = imports[
      `${canonicalPrefix}|netwasm:runtime/reactor-host@1`];

    reactor.watch(7, 11);
    reactor.cancel(11);

    assert.deepEqual(events, [["watch", 7, 11], ["cancel", 11]]);
  });

  test(`forwards ${target} command-world polling to supplied hooks`, () => {
    const events = [];
    const imports = createReactorOracleImports(target, {
      poll: (pollables, count, result) => events.push(["poll", pollables, count, result]),
      drop: pollable => events.push(["drop", pollable]),
    });
    const poll = imports[`${canonicalPrefix}|wasi:io/poll@0.2`];

    poll.poll(16, 2, 32);
    poll.pollable_drop(5);

    assert.deepEqual(events, [["poll", 16, 2, 32], ["drop", 5]]);
  });
}
