# Chunked world schemas

Use these examples as adaptable shapes, not mandatory field names.

## Package layout

```text
maps/<world-id>/
|-- world.json
`-- chunks/
    |-- 0_0.json
    |-- 0_1.json
    `-- -1_0.json
```

Keep player state outside this directory, under a save slot keyed by world ID.

## Finite world

```json
{
  "formatVersion": 1,
  "id": "greenland",
  "mode": "finite",
  "chunkSize": [10, 10],
  "startPosition": { "chunk": [2, 2], "tile": [5, 5] },
  "bounds": { "min": [0, 0], "max": [4, 4] }
}
```

## Procedural world

```json
{
  "formatVersion": 1,
  "id": "endless-grassland",
  "mode": "procedural",
  "chunkSize": [10, 10],
  "seed": 184726,
  "generatorId": "base:grassland-v1",
  "generatorVersion": 1,
  "startPosition": { "chunk": [0, 0], "tile": [5, 5] }
}
```

## Authored chunk

```json
{
  "position": [0, 0],
  "terrainLegend": {
    "G": "base:grass",
    "S": "base:stone"
  },
  "terrain": [
    ["G", "G", "G", "G", "G", "G", "G", "G", "G", "G"],
    ["G", "G", "G", "S", "S", "G", "G", "G", "G", "G"]
  ],
  "objects": [
    {
      "id": "tree-01",
      "definition": "base:oak-tree",
      "position": [3, 6],
      "rotation": 90
    }
  ],
  "encounter": { "definition": "base:slime-encounter" }
}
```

The terrain array must contain exactly the configured chunk width and height. A validator should reject malformed authored chunks before packaging.

## Procedural save delta

```json
{
  "chunk": [982, -351],
  "generatorVersion": 1,
  "removedObjects": ["tree-03"],
  "completedEvents": ["battle-01"],
  "tileOverrides": [
    {
      "position": [4, 7],
      "terrain": "base:burned-grass"
    }
  ]
}
```

Identifiers referenced by deltas must be deterministically reproduced by the generator. Changing generator behavior under the same version can invalidate saves, so algorithm changes that affect output require a new generator version or a migration.

## Provider contract

```csharp
public interface IChunkProvider
{
    bool CanProvide(ChunkPosition position);
    ValueTask<ChunkData> GetChunkAsync(
        ChunkPosition position,
        CancellationToken cancellationToken = default);
}
```

Suggested implementations:

- `FiniteChunkProvider`: reads authored chunks and enforces bounds.
- `ProceduralChunkProvider`: generates deterministically from seed and position.
- `HybridChunkProvider`: reads authored overrides first, otherwise delegates to the procedural provider.

Loading precedence for hybrid maps:

1. Resolve authored chunk at the requested coordinate.
2. If absent, generate the procedural base chunk.
3. Apply the current save slot's delta.
4. Return the runtime chunk without modifying either source.

## Coordinate conversion

Conceptually:

```text
world tile = chunk coordinate * chunk size + local tile
```

Centralize the inverse operation and use floor division so negative world coordinates map correctly. Do not rely on language-default truncation toward zero.
