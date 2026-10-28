// Demo presets for the hero mixer. The mixes from scripts/screenshots.py, except
// Ocean Focus swaps Green Noise for Brown Noise to keep the sampler at 8 sounds.
// The site only ships the sounds these use; the rest are a reason to download the app.

/** Dark-theme accents from src/Blanky.Core/Model/AccentColors.cs. */
export const accents = {
  blue: "#0A84FF",
  orange: "#FF9F0A",
  teal: "#40C8E0",
  indigo: "#5E5CE6",
} as const;

export type Accent = keyof typeof accents;

export interface MixState {
  accent: Accent;
  name: string;
  /** Selected sounds and their slider positions (0..1). */
  sounds: Record<string, number>;
}

export const presets: (MixState & { id: string; icon: string })[] = [
  { id: "heavy-rain", name: "Heavy Rain Sleep", icon: "weather_rain", accent: "indigo", sounds: { rain: 0.75, "brown-noise": 0.5, fan: 0.4 } },
  { id: "cozy-night", name: "Cozy Night In", icon: "fire", accent: "orange", sounds: { fireplace: 0.7, wind: 0.45, "warm-piano": 0.55 } },
  { id: "ocean-focus", name: "Ocean Focus", icon: "water", accent: "teal", sounds: { waves: 0.7, "brown-noise": 0.4, "lofi-beats": 0.45 } },
];

/** The sounds the website ships and plays: those the demo presets use. */
export const demoSounds: string[] = [...new Set(presets.flatMap((p) => Object.keys(p.sounds)))];
