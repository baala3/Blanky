// @ts-check
import { defineConfig } from "astro/config";

// Served from GitHub Pages at the custom domain in public/CNAME.
export default defineConfig({
  site: "https://blanky.balashekhar.me",
  trailingSlash: "always",
});
