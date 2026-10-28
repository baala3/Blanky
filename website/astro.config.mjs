// @ts-check
import { defineConfig } from "astro/config";
import { cpSync } from "node:fs";

// The hero mixer plays the app's own sound files. Mirror them into public/
// (gitignored) before dev or build, so there is one copy in the repo.
/** @type {import("astro").AstroIntegration} */
const appSounds = {
  name: "blanky-app-sounds",
  hooks: {
    "astro:config:setup": ({ config }) => {
      cpSync(new URL("../src/Blanky/Assets/Sounds/", import.meta.url), new URL("sounds/", config.publicDir), {
        recursive: true,
        force: true,
      });
    },
  },
};

// Served from GitHub Pages at the custom domain in public/CNAME.
export default defineConfig({
  site: "https://blanky.balashekhar.me",
  trailingSlash: "always",
  integrations: [appSounds],
});
