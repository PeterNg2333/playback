import assert from "node:assert/strict";
import { ensureMongo, restoredPackagesAvailable } from "./dev.mjs";

const assets = {
  packageFolders: { "C:/old-cache/": {} },
  libraries: { "Example/1.0": { path: "example/1.0" } },
  targets: { net10: { "Example/1.0": { compile: { "lib/example.dll": {} } } } },
};
const cache = {
  success: true,
  expectedPackageFiles: ["C:/old-cache/library.dll"],
};
assert.equal(
  restoredPackagesAvailable(assets, cache, () => false),
  false,
  "An assets file pointing to an unavailable NuGet cache must trigger restore",
);
assert.equal(
  restoredPackagesAvailable(
    assets,
    cache,
    (file) => !file.endsWith("example.dll"),
  ),
  false,
  "Missing package assemblies must trigger restore even if cache metadata exists",
);
assert.equal(
  restoredPackagesAvailable(assets, cache, () => true),
  true,
);

const previousUri = process.env.PLAYBACK_MONGO_URI;
const previousStage = process.env.ENVIRONMENT;
delete process.env.ENVIRONMENT;
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

  process.env.ENVIRONMENT = "prod";
  assert.equal(
    await ensureMongo({ mongo: false }, { start, check, pause }),
    false,
  );
  assert.equal(starts, 1, "ENVIRONMENT=prod must never start the local container");

  process.env.ENVIRONMENT = "local";
  process.env.PLAYBACK_MONGO_URI = "mongodb://example.invalid:27017";
  checks = 2;
  assert.equal(
    await ensureMongo({ mongo: false }, { start, check, pause }),
    true,
  );
  assert.equal(starts, 2, "ENVIRONMENT=local must start the local container whatever the URI says");
  delete process.env.ENVIRONMENT;
  delete process.env.PLAYBACK_MONGO_URI;

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
    "Startup DB check passed: reuse ready DB, respect custom URI, start and wait, report Docker failure, ENVIRONMENT=local starts the container and prod never does",
  );
} finally {
  if (previousUri === undefined) delete process.env.PLAYBACK_MONGO_URI;
  else process.env.PLAYBACK_MONGO_URI = previousUri;
  if (previousStage === undefined) delete process.env.ENVIRONMENT;
  else process.env.ENVIRONMENT = previousStage;
}
