// Demo presets for the hero mixer. Same mixes as scripts/screenshots.py, so the
// site plays what its screenshots show.

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
  { id: "ocean-focus", name: "Ocean Focus", icon: "water", accent: "teal", sounds: { "green-noise": 0.5, waves: 0.7, "lofi-beats": 0.45 } },
];
