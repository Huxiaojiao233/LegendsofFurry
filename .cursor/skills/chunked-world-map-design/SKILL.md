---
name: chunked-world-map-design
description: Design or review chunk-based game world storage that supports finite hand-authored maps now and procedural or hybrid infinite maps later. Use for map JSON schemas, chunk loading, coordinate systems, save separation, editor formats, and migration planning; not for detailed terrain-generation algorithms unless requested.
---

# Chunked World Map Design

Design one chunk abstraction shared by all map modes. Keep gameplay, rendering, movement, and saving dependent on a chunk provider rather than on how a chunk was produced.

## Default model

- Treat one overworld cell as one chunk. The current project convention is a `10 x 10` local tile grid, but keep chunk dimensions in world metadata rather than hard-coding them.
- Represent chunk positions with signed integer coordinates. A finite `5 x 5` world is only an initial configured boundary, not an engine limit.
- Support these source modes:
  - `finite`: every valid chunk is authored and bounded.
  - `procedural`: missing chunks are generated deterministically from world seed, chunk coordinates, generator ID, and generator version.
  - `hybrid`: an authored chunk overrides procedural generation at the same coordinate.
- Obtain chunks through an abstraction such as `IChunkProvider`. Do not make upper systems branch on map mode.
- Keep immutable world definitions separate from mutable player save data.

## Storage decisions

- Store world-level metadata in `world.json`: format version, world ID, mode, chunk size, start position, optional bounds, seed, generator ID, and generator version.
- During authoring, prefer one file per chunk for independent editing, validation, diffing, and partial loading. Publishing may package the directory into a ZIP-derived extension format.
- Store compact tile grids as arrays of terrain identifiers. Store sparse objects, encounters, triggers, heights, and exceptional tiles as structured records.
- Use namespaced definition IDs such as `base:grass` so extension packs can add terrain without engine hard-coding.
- Load only the visible working set. Default to current chunk plus four cardinal neighbors; use a `3 x 3` set when diagonal visibility requires it.
- For procedural worlds, do not persist every generated chunk. Regenerate unchanged content and store only player-caused deltas, explored-state data when needed, and entities whose state must persist.
- Make neighboring chunks share deterministic edge constraints so roads, rivers, exits, and heights agree across boundaries.

## Current implementation boundary

When planning an early version, implement finite authored maps completely while preserving replaceable provider interfaces and unrestricted signed coordinates. Do not require procedural generation, cache eviction, biome blending, or delta compaction until the user is implementing infinite maps.

## Review checklist

Before recommending or approving a design, verify:

- Map dimensions are configuration, not array-size assumptions spread through gameplay code.
- Negative-coordinate division and modulo are handled by centralized coordinate conversion functions.
- Chunk IDs and object IDs are stable enough for save deltas to reference them.
- `formatVersion` and generator version are recorded for migration and deterministic compatibility.
- Original extension content is never overwritten by a player save.
- Missing authored chunks in a finite world produce validation errors; missing chunks in procedural or hybrid worlds follow their provider policy.
- JSON contains metadata and structured game data, not embedded Base64 images, audio, or models.

For concrete schemas, directory layouts, and loading precedence, read [references/schemas.md](references/schemas.md).
