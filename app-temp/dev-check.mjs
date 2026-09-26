import assert from "node:assert/strict";
import { ensureMongo } from "./dev.mjs";

const previousUri = process.env.PLAYBACK_MONGO_URI;
let starts = 0;
let checks = 0;
const start = () => {
  starts++;
};
const check = async () => ++checks === 3;
const pause = async () => {};

try {
  delete process.env.PLAYBACK_MONGO_URI;
  assert.equal(
    await ensureMongo({ mongo: true }, { start, check, pause }),
    true,
  );
  assert.equal(starts, 0);

  process.env.PLAYBACK_MONGO_URI = "mongodb://example.invalid:27017";
  assert.equal(
    await ensureMongo({ mongo: false }, { start, check, pause }),
    false,
  );
  assert.equal(starts, 0);

  delete process.env.PLAYBACK_MONGO_URI;
  assert.equal(
    await ensureMongo({ mongo: false }, { start, check, pause }),
    true,
  );
  assert.equal(starts, 1);
  assert.equal(checks, 3);

  let pendingChecks = 0;
  assert.equal(
    await ensureMongo(
      { mongo: false },
      {
        start,
        check: async () => {
          pendingChecks++;
          return false;
        },
        pause,
      },
    ),
    false,
  );
  assert.equal(pendingChecks, 15);

  assert.equal(
    await ensureMongo(
      { mongo: false },
      {
        start: () => {
          throw new Error("Docker unavailable");
        },
        check,
        pause,
      },
    ),
    false,
  );
  console.log(
    "Startup DB check passed: reuse ready DB, respect custom URI, start and wait, report Docker failure",
  );
} finally {
  if (previousUri === undefined) delete process.env.PLAYBACK_MONGO_URI;
  else process.env.PLAYBACK_MONGO_URI = previousUri;
}
