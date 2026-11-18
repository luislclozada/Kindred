/** Stable pseudo-random tint (0-3) for an avatar, derived from any string. */
export function tintOf(seed: string): number {
  let hash = 0;
  for (const ch of seed) {
    hash = (hash * 31 + ch.charCodeAt(0)) >>> 0;
  }
  return hash % 4;
}

export function initialOf(name: string): string {
  return (name.trim().charAt(0) || '?').toUpperCase();
}
