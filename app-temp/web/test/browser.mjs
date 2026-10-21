// The browser every check drives: Edge on Windows, or the Chromium executable PLAYBACK_BROWSER names.
import { chromium } from "playwright-core";

export const browserPath =
  process.env.PLAYBACK_BROWSER ||
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";

export function launchBrowser(args = []) {
  return chromium.launch({ headless: true, executablePath: browserPath, args });
}
