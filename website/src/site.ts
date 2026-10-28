// Links and names used across the site. Keep in step with src/Blanky.Core/AppInfo.cs.
export const site = {
  name: "Blanky",
  description:
    "Free, open-source ambient sound mixer for Windows. Mix rain, waves, fireplace, white noise and more to sleep, focus or relax.",
  repo: "https://github.com/baala3/Blanky",
  releases: "https://github.com/baala3/Blanky/releases/latest",
  // Stable names, so these always fetch the newest installer.
  setupX64: "https://github.com/baala3/Blanky/releases/latest/download/Blanky-Setup-x64.exe",
  setupArm64: "https://github.com/baala3/Blanky/releases/latest/download/Blanky-Setup-arm64.exe",
  issues: "https://github.com/baala3/Blanky/issues/new",
  contributing: "https://github.com/baala3/Blanky/blob/main/CONTRIBUTING.md",
  buyMeACoffee: "https://buymeacoffee.com/baala3",
  sponsors: "https://github.com/sponsors/baala3",
  blankie: "https://github.com/codybrom/Blankie",
  blankieSite: "https://blankie.rest",
  blanket: "https://github.com/rafaelmardojai/blanket",
  author: "Bala",
};

/** Prefixes a site path with the deploy base ("/"). */
export const url = (path = "") => `${import.meta.env.BASE_URL.replace(/\/$/, "")}/${path.replace(/^\//, "")}`;
