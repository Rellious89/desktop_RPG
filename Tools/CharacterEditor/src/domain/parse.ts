import { actorSchema, worldSchema } from "../schema";

export const parseWorld = (input: unknown) => worldSchema.parse(input);
export const parseActor = (input: unknown) => {
  if (input && typeof input === "object" && "documentKind" in input && (input as { documentKind?: string }).documentKind === "actor-export")
    return actorSchema.parse((input as { authored?: unknown }).authored);
  return actorSchema.parse(input);
};

