#!/usr/bin/env node
// Builds src/Blanky/Assets/icons.json from pinned upstream icon sets.
//
// Fluent UI System Icons (MIT, Microsoft) supply almost every glyph. Material Symbols
// Rounded (Apache 2.0, Google) fill the three gaps Fluent has for built-in sounds
// (bird, fan, piano), at weight 300 so their strokes match Fluent's regular weight.
//
// Usage: node scripts/generate-icons.mjs

import { writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const FLUENT_SHA = "60d561a815310bd52914fa0bccee14836428e23b";
const MATERIAL_SHA = "10ecc14790612ca8fd164e3f4ffe9c16fc50d5b7";

// Icon picker categories. Fluent entries are asset folder names; "m:" entries are
// Material Symbols names. The first category also lists every built-in sound icon.
const CATEGORIES = {
  "Nature": [
    "Weather Rain", "Weather Thunderstorm", "Weather Squalls", "Water", "Weather Humidity",
    "m:raven", "Weather Moon", "Weather Partly Cloudy Night", "Weather Drizzle",
    "Weather Rain Showers Night", "Weather Snow", "Weather Snowflake", "Weather Fog",
    "Weather Cloudy", "Weather Sunny", "Weather Sunny Low", "Drop", "Fire", "Tree Deciduous",
    "Leaf One", "Leaf Three", "Plant Grass", "Plant Cattail", "Beach", "Tent",
    "Earth", "Animal Cat", "Animal Dog", "Animal Paw Print", "Animal Turtle", "Animal Rabbit",
  ],
  "Places": [
    "City", "Building Multiple", "Building", "Building Skyscraper", "Home", "Building Lighthouse",
    "Building Shop", "Fireplace", "Drink Coffee", "Drink Wine", "Drink Beer", "Food", "Washer",
    "m:mode_fan", "Bed", "Desk", "Library", "Book Open", "Hat Graduation", "Lightbulb",
  ],
  "Travel": [
    "Airplane", "Vehicle Ship", "Vehicle Subway", "Vehicle Bus", "Vehicle Car",
    "Vehicle Bicycle", "Vehicle Truck", "Vehicle Cable Car", "Globe", "Map",
  ],
  "Sound": [
    "Pulse", "Sound Wave Circle", "Sine Wave Dots", "Data Bar Vertical", "m:graphic_eq",
    "m:airwave", "m:equalizer", "Music Note 1", "Music Note 2", "Guitar", "m:piano",
    "Headphones", "Headphones Sound Wave", "Speaker 2", "Sound Source", "Mic", "Album",
  ],
  "Other": [
    "Star", "Heart", "Sparkle", "Emoji", "Brain Circuit", "Sleep", "Clock", "Hourglass",
    "Person", "People", "Settings", "Shield", "Diamond", "Gift", "Ribbon",
  ],
};

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const out = path.join(root, "src", "Blanky", "Assets", "icons.json");

function fluentUrl(name, style) {
  const snake = name.toLowerCase().replaceAll(" ", "_");
  const dir = encodeURIComponent(name);
  return `https://raw.githubusercontent.com/microsoft/fluentui-system-icons/${FLUENT_SHA}/assets/${dir}/SVG/ic_fluent_${snake}_24_${style}.svg`;
}

function materialUrl(name, filled) {
  const variant = filled ? "wght300fill1" : "wght300";
  return `https://raw.githubusercontent.com/google/material-design-icons/${MATERIAL_SHA}/symbols/web/${name}/materialsymbolsrounded/${name}_${variant}_24px.svg`;
}

async function fetchSvg(url) {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${res.status} ${url}`);
  return res.text();
}

// Flattens every <path d> in the SVG into one geometry. Both sets draw single-color
// glyphs, so concatenating subpaths keeps the shape.
function parse(svg) {
  const viewBox = /viewBox="([^"]+)"/.exec(svg)?.[1];
  const paths = [...svg.matchAll(/<path[^>]*\sd="([^"]+)"/g)].map((m) => m[1]);
  if (!viewBox || paths.length === 0) throw new Error("Unexpected SVG shape");
  if (/<(circle|rect|ellipse|polygon|line)\b/.test(svg)) throw new Error("Non-path element");
  return { viewBox, data: paths.map(normalizePath).join(" ") };
}

// Rewrites SVG path data into a form XAML's path markup parser accepts. Optimized
// SVGs pack arc flags against their neighbors ("a1 1 0 011 1"), which XAML
// rejects, so every token is re-emitted with an explicit separator.
const ARGS = { m: 2, l: 2, h: 1, v: 1, c: 6, s: 4, q: 4, t: 2, a: 7, z: 0 };

function normalizePath(d) {
  const out = [];
  let i = 0;
  let command = null;
  let index = 0;
  const skip = () => {
    while (i < d.length && /[\s,]/.test(d[i])) i++;
  };
  const number = () => {
    const match = /^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?/.exec(d.slice(i));
    if (!match) throw new Error(`Bad path data near "${d.slice(i, i + 12)}"`);
    i += match[0].length;
    return match[0];
  };
  for (skip(); i < d.length; skip()) {
    if (/[a-zA-Z]/.test(d[i])) {
      command = d[i++];
      index = 0;
      out.push(command);
      if (ARGS[command.toLowerCase()] === undefined) throw new Error(`Unsupported path command ${command}`);
      continue;
    }
    const arity = ARGS[command.toLowerCase()];
    if (arity === 0) throw new Error("Numbers after Z");
    const slot = index % arity;
    // Arc flags (4th and 5th arguments) are single digits with no separator required.
    out.push(command.toLowerCase() === "a" && (slot === 3 || slot === 4) ? d[i++] : number());
    index++;
  }
  return out.join(" ");
}

async function load(entry) {
  if (entry.startsWith("m:")) {
    const name = entry.slice(2);
    const [regular, filled] = await Promise.all([
      fetchSvg(materialUrl(name, false)),
      fetchSvg(materialUrl(name, true)),
    ]);
    return [name, parse(regular), parse(filled)];
  }
  const key = entry.toLowerCase().replaceAll(" ", "_");
  const [regular, filled] = await Promise.all([
    fetchSvg(fluentUrl(entry, "regular")),
    fetchSvg(fluentUrl(entry, "filled")),
  ]);
  return [key, parse(regular), parse(filled)];
}

const icons = {};
const categories = {};
const missing = [];
for (const [category, entries] of Object.entries(CATEGORIES)) {
  categories[category] = [];
  for (const entry of entries) {
    try {
      const [key, regular, filled] = await load(entry);
      if (regular.viewBox !== filled.viewBox) throw new Error("viewBox mismatch");
      icons[key] = { viewBox: regular.viewBox, regular: regular.data, filled: filled.data };
      categories[category].push(key);
    } catch (error) {
      missing.push(`${entry}: ${error.message}`);
    }
  }
}

if (missing.length > 0) {
  console.error(`Skipped ${missing.length} icon(s):\n  ${missing.join("\n  ")}`);
}

await writeFile(out, JSON.stringify({ categories, icons }, null, 1) + "\n");
console.log(`Wrote ${Object.keys(icons).length} icons to ${path.relative(root, out)}`);
