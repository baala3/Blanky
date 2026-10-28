// @ts-check
import { defineConfig } from "astro/config";
import { copyFileSync, existsSync, mkdirSync, readdirSync, rmSync, statSync } from "node:fs";
import { demoSounds } from "./src/components/mixer-data.ts";

// The hero mixer plays a few of the app's own sound files. Mirror just those
// into public/ (gitignored) before dev or build, so there is one copy in the
// repo and the site doesn't give away the full library. Syncs rather than
// wipes, so a running dev server never sees the files disappear.
/** @type {import("astro").AstroIntegration} */
const appSounds = {
  name: "blanky-app-sounds",
  hooks: {
    "astro:config:setup": ({ config }) => {
      const target = new URL("sounds/", config.publicDir);
      mkdirSync(target, { recursive: true });
      const wanted = new Set(demoSounds.map((name) => `${name}.m4a`));
      for (const file of readdirSync(target)) {
        if (!wanted.has(file)) rmSync(new URL(file, target));
      }
      for (const file of wanted) {
        const from = new URL(`../src/Blanky/Assets/Sounds/${file}`, import.meta.url);
        const to = new URL(file, target);
        if (!existsSync(to) || statSync(to).mtimeMs < statSync(from).mtimeMs || statSync(to).size !== statSync(from).size) {
          copyFileSync(from, to);
        }
      }
    },
  },
};

// Served from GitHub Pages at the custom domain in public/CNAME.
export default defineConfig({
  site: "https://blanky.balashekhar.me",
  trailingSlash: "always",
  integrations: [appSounds],
});
