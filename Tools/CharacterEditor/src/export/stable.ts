export function stableValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stableValue);
  if (value && typeof value === "object") return Object.fromEntries(Object.keys(value).sort().map((key) => [key, stableValue((value as Record<string, unknown>)[key])]));
  return value;
}
export const stableJson = (value: unknown) => `${JSON.stringify(stableValue(value), null, 2)}\n`;

