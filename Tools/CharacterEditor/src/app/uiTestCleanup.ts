import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

/**
 * Testing Library's auto-cleanup relies on a global `afterEach`, which only
 * exists if vite.config.ts sets `test.globals: true` (a config file owned
 * by the Coordinator, not this app path). It doesn't here, so without this,
 * multiple `render()` calls across `it()` blocks in the same file pile up
 * in the DOM and later queries return "found multiple elements" false
 * failures. Import this once at the top of every `*.ui.test.tsx` file.
 */
afterEach(() => {
  cleanup();
});
