#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using NUnit.Framework;
using UnityEngine;

/// <summary>Verifies the stable identity and owner-neutral behavior foundations introduced in phase 1.</summary>
public sealed class ContentArchitectureFoundationTests
{
    /// <summary>Ensures the shared ID grammar accepts package IDs and rejects culture-sensitive or unsafe forms.</summary>
    [Test]
    public void StableContentIdGrammarIsSharedAndStrict()
    {
        Assert.That(ContentId.IsValid("base.card-01"), Is.True);
        Assert.That(ContentId.IsValid("status_test"), Is.True);
        Assert.That(ContentId.IsValid("Base.Card"), Is.False);
        Assert.That(ContentId.IsValid("1card"), Is.False);
        Assert.That(ContentId.IsValid("card/escape"), Is.False);
        Assert.That(ContentId.IsValid(string.Empty), Is.False);
    }

    /// <summary>Ensures runtime registry construction rejects invalid external definition IDs.</summary>
    [Test]
    public void RegistryRejectsInvalidDefinitionIdAtRuntimeBoundary()
    {
        ContentPackage package = new ContentPackage();
        package.Cards.Add(new CardDefinition { CardId = "Invalid Card", Enabled = true });

        Assert.Throws<InvalidDataException>(() => new ContentRegistry(package));
    }

    /// <summary>
    /// Ensures a status executes its own graph and produces owner-aware logs without creating a synthetic card.
    /// </summary>
    [Test]
    public void StatusBehaviorExecutesWithRealOwnerInsteadOfSyntheticCard()
    {
        GameObject host = new GameObject("Phase1StatusOwner");
        try
        {
            Unit unit = host.AddComponent<Unit>();
            StatusDefinition status = CreateArmorStatus();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                ContentBehaviorOwner.FromStatus(status, null),
                unit, unit, null, null, null, null, new CardPlayResult(), false);

            bool executed = ContentCardEffectExecutor.TryExecuteOwnedTrigger(
                context, status.Behaviors, "on_unit_turn_start");

            Assert.That(executed, Is.True);
            Assert.That(context.Card, Is.Null);
            Assert.That(unit.Armor, Is.EqualTo(3));
            Assert.That(context.CombatLog, Has.Count.EqualTo(1));
            Assert.That(context.CombatLog[0].OwnerKind, Is.EqualTo(ContentDefinitionKinds.Status));
            Assert.That(context.CombatLog[0].OwnerId, Is.EqualTo(status.StatusId));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    /// <summary>Ensures invalid card-zone parameters fail preflight before a card can commit resources.</summary>
    [Test]
    public void InvalidCardZoneFailsBehaviorPreflight()
    {
        CardDefinition card = new CardDefinition { CardId = "invalid_zone_test" };
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = "invalid_zone_test.on_play",
            OwnerKind = ContentDefinitionKinds.Card,
            OwnerId = card.CardId,
            TriggerKey = "on_play"
        };
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "invalid_zone_test.root",
            NodeKind = "sequence",
            OperationKey = "sequence"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "invalid_zone_test.generate",
            ParentNodeId = "invalid_zone_test.root",
            BranchKey = "children",
            NodeKind = "effect",
            OperationKey = "generate_card",
            ParametersJson = "{\"target\":\"self\",\"amount\":{\"kind\":\"constant\",\"value\":1},\"query\":{\"cardId\":\"hit_01\"},\"destinationZone\":\"graveyard\"}"
        });
        card.Behaviors.Add(behavior);

        Assert.That(ContentCardEffectExecutor.CanExecuteOnPlay(card), Is.False);
    }

    /// <summary>Ensures the shared damage event can modify a request before the target applies armor and health.</summary>
    [Test]
    public void DamagePipelinePublishesMutableRequestAndStructuredResolution()
    {
        GameObject host = new GameObject("Phase2DamageTarget");
        IDisposable subscription = null;
        try
        {
            Unit target = host.AddComponent<Unit>();
            target.ConfigureCombatant("Target", 10, 0, 1);
            subscription = CombatEventBus.Shared.Subscribe<DamageRequestedEvent>(message => message.Request.Amount = 2);

            DamageResolution resolution = target.ResolveDamage(
                new DamageRequest(null, target, 7, DamageTypeIds.Normal, false));

            Assert.That(resolution.HealthDamage, Is.EqualTo(2));
            Assert.That(resolution.Request.DamageTypeId, Is.EqualTo(DamageTypeIds.Normal));
            Assert.That(target.CurrentHealth, Is.EqualTo(8));
        }
        finally
        {
            subscription?.Dispose();
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    /// <summary>Ensures status mutations are observable through stable IDs without requiring the legacy enum.</summary>
    [Test]
    public void StatusMutationPublishesStableIdEvent()
    {
        GameObject host = new GameObject("Phase2StatusTarget");
        StatusChangedEvent observed = null;
        IDisposable subscription = null;
        try
        {
            Unit unit = host.AddComponent<Unit>();
            subscription = CombatEventBus.Shared.Subscribe<StatusChangedEvent>(message => observed = message);

            unit.State.Add("external_status", 2);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.StatusId, Is.EqualTo("external_status"));
            Assert.That(observed.PreviousStacks, Is.Zero);
            Assert.That(observed.CurrentStacks, Is.EqualTo(2));
        }
        finally
        {
            subscription?.Dispose();
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    /// <summary>Ensures external layers cannot silently replace base definitions without an explicit declaration.</summary>
    [Test]
    public void ContentPackComposerRequiresExplicitOverride()
    {
        ContentPackage basePackage = new ContentPackage();
        basePackage.Units.Add(new UnitDefinition { UnitId = "hero", DisplayName = "Base" });
        ContentPackDefinition pack = new ContentPackDefinition { PackId = "expansion" };
        pack.Content.Units.Add(new UnitDefinition { UnitId = "hero", DisplayName = "Expansion" });

        Assert.Throws<InvalidDataException>(() => ContentPackageComposer.Compose(basePackage, new[] { pack }));

        pack.Overrides.Add(new ContentOverrideDefinition
        {
            DefinitionKind = ContentDefinitionKinds.Unit,
            DefinitionId = "hero"
        });
        ContentPackage composed = ContentPackageComposer.Compose(basePackage, new[] { pack });
        Assert.That(composed.Units[0].DisplayName, Is.EqualTo("Expansion"));
        Assert.That(basePackage.Units[0].DisplayName, Is.EqualTo("Base"));
    }

    /// <summary>Ensures dependencies take precedence over numeric load order and cycles are rejected.</summary>
    [Test]
    public void ContentPackComposerUsesDependencyTopologyBeforeLoadOrder()
    {
        ContentPackDefinition dependency = new ContentPackDefinition { PackId = "dependency", LoadOrder = 500 };
        dependency.Content.Units.Add(new UnitDefinition { UnitId = "hero", DisplayName = "Dependency" });
        ContentPackDefinition consumer = new ContentPackDefinition { PackId = "consumer", LoadOrder = 1 };
        consumer.Dependencies.Add(dependency.PackId);
        consumer.Overrides.Add(new ContentOverrideDefinition
        {
            DefinitionKind = ContentDefinitionKinds.Unit, DefinitionId = "hero"
        });
        consumer.Content.Units.Add(new UnitDefinition { UnitId = "hero", DisplayName = "Consumer" });

        ContentPackage composed = ContentPackageComposer.Compose(new ContentPackage(), new[] { consumer, dependency });

        Assert.That(composed.Units.Single().DisplayName, Is.EqualTo("Consumer"));
        dependency.Dependencies.Add(consumer.PackId);
        Assert.Throws<InvalidDataException>(() =>
            ContentPackageComposer.Compose(new ContentPackage(), new[] { consumer, dependency }));
    }

    /// <summary>Token visuals build runtime URP materials and a two-submesh token instead of instancing scene assets.</summary>
    [Test]
    public void TokenVisualApplyCreatesRuntimeMaterials()
    {
        GameObject host = new GameObject("TokenVisualHost");
        try
        {
            UnitDefinition definition = new UnitDefinition
            {
                UnitId = "token_visual_host",
                DisplayName = "Token",
                TokenFrameColor = "#C15254",
                PortraitKey = string.Empty
            };

            TokenVisualRuntime.Apply(host.transform, definition);

            MeshRenderer renderer = host.GetComponent<MeshRenderer>();
            MeshFilter filter = host.GetComponent<MeshFilter>();
            Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(2));
            Assert.That(renderer.sharedMaterials[0].name, Does.StartWith("RuntimeToken"));
            Assert.That(renderer.sharedMaterials[1].name, Does.StartWith("RuntimeToken"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    /// <summary>Pack GameSettings replace the engine stub when the pack authors a positive hand limit.</summary>
    [Test]
    public void ContentPackComposerOverlaysAuthoredGameSettings()
    {
        ContentPackage basePackage = new ContentPackage
        {
            GameSettings = new GameSettingsDefinition { HandLimit = 10, PlayerUnitId = "" }
        };
        ContentPackDefinition pack = new ContentPackDefinition { PackId = "core" };
        pack.Content.GameSettings = new GameSettingsDefinition
        {
            HandLimit = 12,
            PlayerUnitId = "hero"
        };
        pack.Content.Units.Add(new UnitDefinition { UnitId = "hero", DisplayName = "Hero", Enabled = true });

        ContentPackage composed = ContentPackageComposer.Compose(basePackage, new[] { pack });
        Assert.That(composed.GameSettings.HandLimit, Is.EqualTo(12));
        Assert.That(composed.GameSettings.PlayerUnitId, Is.EqualTo("hero"));
    }

    /// <summary>后加载的包如果没写玩家单位 ID，不得清掉前一层编制。</summary>
    [Test]
    public void ContentPackComposerKeepsPlayerUnitIdWhenLaterPackOmitsIt()
    {
        ContentPackage basePackage = new ContentPackage();
        ContentPackDefinition core = new ContentPackDefinition { PackId = "core", LoadOrder = 0 };
        core.Content.GameSettings = new GameSettingsDefinition
        {
            HandLimit = 10,
            PlayerUnitId = "hongye"
        };
        core.Content.Units.Add(new UnitDefinition { UnitId = "hongye", Enabled = true });
        core.Content.Units.Add(new UnitDefinition { UnitId = "taigao", Enabled = true });
        ContentPackDefinition overlay = new ContentPackDefinition { PackId = "planner_pack", LoadOrder = 1000 };
        overlay.Content.GameSettings = new GameSettingsDefinition
        {
            HandLimit = 10,
            PlayerUnitId = ""
        };

        ContentPackage composed = ContentPackageComposer.Compose(basePackage, new[] { core, overlay });
        Assert.That(composed.GameSettings.PlayerUnitId, Is.EqualTo("hongye"));
    }

    /// <summary>Later unit overrides without portrait keys keep earlier token art fields.</summary>
    [Test]
    public void ContentPackComposerKeepsPortraitWhenLaterUnitOmitsIt()
    {
        ContentPackage basePackage = new ContentPackage();
        ContentPackDefinition core = new ContentPackDefinition { PackId = "core", LoadOrder = 0 };
        core.Content.Units.Add(new UnitDefinition
        {
            UnitId = "hongye",
            DisplayName = "鸿叶",
            PortraitKey = "portrait.hongye",
            TokenFrameColor = "#9AB041",
            Enabled = true
        });
        ContentPackDefinition overlay = new ContentPackDefinition { PackId = "planner_pack", LoadOrder = 1000 };
        overlay.Overrides.Add(new ContentOverrideDefinition
        {
            DefinitionKind = ContentDefinitionKinds.Unit,
            DefinitionId = "hongye"
        });
        overlay.Content.Units.Add(new UnitDefinition
        {
            UnitId = "hongye",
            DisplayName = "鸿叶",
            InitialHealth = 30,
            Enabled = true
        });

        ContentPackage composed = ContentPackageComposer.Compose(basePackage, new[] { core, overlay });
        Assert.That(composed.Units[0].PortraitKey, Is.EqualTo("portrait.hongye"));
        Assert.That(composed.Units[0].TokenFrameColor, Is.EqualTo("#9AB041"));
        Assert.That(composed.Units[0].InitialHealth, Is.EqualTo(30));
    }

    [Test]
    public void GameDirectoryPacksRootIsBesideProjectOrPlayer()
    {
        string root = ContentRuntime.ResolveGameDirectoryPacksRoot();
        Assert.That(root.Replace('\\', '/').EndsWith("Content/Packs"), Is.True);
        Assert.That(Directory.GetParent(root)?.Name, Is.EqualTo("Content"));
    }

    /// <summary>Ensures a physical pack is hash checked, composed, and assigned safe external asset paths.</summary>
    [Test]
    public void PhysicalContentPackLoadsFromImmediatePackDirectory()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string packRoot = Path.Combine(temporary, "Packs");
            string packDirectory = Path.Combine(packRoot, "sample");
            string assetDirectory = Path.Combine(packDirectory, "assets");
            Directory.CreateDirectory(assetDirectory);
            string assetPath = Path.Combine(assetDirectory, "art.png");
            File.WriteAllBytes(assetPath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            string catalog = "{\"schemaVersion\":3,\"contentVersion\":\"1.0.0\",\"units\":[{\"unitId\":\"external_hero\",\"displayName\":\"External Hero\",\"initialHealth\":20,\"baseDamage\":3,\"moveSteps\":2,\"enabled\":true}],\"assets\":[{\"assetKey\":\"external.art\",\"assetKind\":\"artwork\",\"relativePath\":\"assets/art.png\"}]}";
            File.WriteAllText(Path.Combine(packDirectory, "catalog.json"), catalog, new UTF8Encoding(false));
            string manifest = $"{{\"formatVersion\":2,\"packId\":\"sample_pack\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"loadOrder\":100,\"dependencies\":[],\"overrides\":[],\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"{ComputeSha256(catalog)}\"}}";
            File.WriteAllText(Path.Combine(packDirectory, ContentPackLoader.ManifestFileName), manifest,
                new UTF8Encoding(false));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");

            ContentLoadResult result = ContentPackLoader.Load(baseRoot, new[] { packRoot });

            Assert.That(result.LoadedPacks.Single().Definition.PackId, Is.EqualTo("sample_pack"));
            Assert.That(result.Package.Units.Any(item => item.UnitId == "external_hero"), Is.True);
            Assert.That(result.TryGetExternalAssetPath("external.art", out string resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(assetPath)));
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    /// <summary>Ensures a zipped .lofepackage with split catalog loads and resolves assets.</summary>
    [Test]
    public void PhysicalContentPackLoadsFromLofePackageFile()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string packRoot = Path.Combine(temporary, "Packs");
            string staging = Path.Combine(temporary, "staging");
            Directory.CreateDirectory(Path.Combine(staging, "assets"));
            string assetPath = Path.Combine(staging, "assets", "art.png");
            File.WriteAllBytes(assetPath, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            string units = "{\"units\":[{\"unitId\":\"zip_hero\",\"displayName\":\"Zip Hero\",\"initialHealth\":20,\"baseDamage\":3,\"moveSteps\":2,\"enabled\":true}]}";
            string assets = "{\"assets\":[{\"assetKey\":\"zip.art\",\"assetKind\":\"artwork\",\"relativePath\":\"assets/art.png\"}]}";
            File.WriteAllText(Path.Combine(staging, "units.json"), units, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "assets.json"), assets, new UTF8Encoding(false));
            string catalog = $"{{\"schemaVersion\":3,\"contentVersion\":\"1.0.0\",\"layout\":\"split\",\"parts\":[{{\"kind\":\"units\",\"file\":\"units.json\",\"sha256\":\"{ComputeSha256(units)}\"}},{{\"kind\":\"assets\",\"file\":\"assets.json\",\"sha256\":\"{ComputeSha256(assets)}\"}}]}}";
            File.WriteAllText(Path.Combine(staging, "catalog.json"), catalog, new UTF8Encoding(false));
            string manifest = $"{{\"formatVersion\":2,\"packId\":\"zip_pack\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"loadOrder\":100,\"dependencies\":[],\"overrides\":[],\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"{ComputeSha256(catalog)}\"}}";
            File.WriteAllText(Path.Combine(staging, ContentPackLoader.ManifestFileName), manifest, new UTF8Encoding(false));
            Directory.CreateDirectory(packRoot);
            CreateZipFromDirectory(staging, Path.Combine(packRoot, "zip_pack.lofepackage"));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");

            ContentLoadResult result = ContentPackLoader.Load(baseRoot, new[] { packRoot });

            Assert.That(result.LoadedPacks.Single().Definition.PackId, Is.EqualTo("zip_pack"));
            Assert.That(result.Package.Units.Any(item => item.UnitId == "zip_hero"), Is.True);
            Assert.That(result.TryGetExternalAssetPath("zip.art", out string resolved), Is.True);
            Assert.That(File.Exists(resolved), Is.True);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    /// <summary>schema 3 分文件包必须合并 units 与 aiProfiles。</summary>
    [Test]
    public void PhysicalContentPackLoadsSchema3UnitsAndAiProfiles()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string packRoot = Path.Combine(temporary, "Packs");
            string staging = Path.Combine(temporary, "staging");
            Directory.CreateDirectory(staging);
            string units = "{\"units\":[{\"unitId\":\"schema3_slime\",\"displayName\":\"史莱姆\",\"unitKind\":\"monster\",\"defaultFaction\":\"enemy\",\"controller\":\"ai\",\"enabled\":true,\"initialHealth\":20,\"capabilities\":[\"wading\"],\"aiProfileId\":\"general\",\"aiOverrides\":[{\"key\":\"killWeight\",\"value\":2}]}]}";
            string profiles = "{\"aiProfiles\":[{\"aiProfileId\":\"general\",\"displayName\":\"通用型\",\"attackWeight\":1,\"defenseWeight\":1,\"healingWeight\":1,\"approachWeight\":1,\"retreatWeight\":1,\"killWeight\":1,\"preferredRange\":1,\"lowHealthThreshold\":0.3,\"enabled\":true}]}";
            File.WriteAllText(Path.Combine(staging, "units.json"), units, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "ai-profiles.json"), profiles, new UTF8Encoding(false));
            string catalog = $"{{\"schemaVersion\":3,\"contentVersion\":\"1.0.0\",\"layout\":\"split\",\"parts\":[{{\"kind\":\"units\",\"file\":\"units.json\",\"sha256\":\"{ComputeSha256(units)}\"}},{{\"kind\":\"aiProfiles\",\"file\":\"ai-profiles.json\",\"sha256\":\"{ComputeSha256(profiles)}\"}}]}}";
            File.WriteAllText(Path.Combine(staging, "catalog.json"), catalog, new UTF8Encoding(false));
            string manifest = $"{{\"formatVersion\":2,\"packId\":\"schema3_pack\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"loadOrder\":100,\"dependencies\":[],\"overrides\":[],\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"{ComputeSha256(catalog)}\"}}";
            File.WriteAllText(Path.Combine(staging, ContentPackLoader.ManifestFileName), manifest, new UTF8Encoding(false));
            Directory.CreateDirectory(packRoot);
            CreateZipFromDirectory(staging, Path.Combine(packRoot, "schema3_pack.lofepackage"));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");

            ContentLoadResult result = ContentPackLoader.Load(baseRoot, new[] { packRoot });

            Assert.That(result.Package.Units.Any(item => item.UnitId == "schema3_slime"), Is.True);
            UnitDefinition slime = result.Package.Units.Single(item => item.UnitId == "schema3_slime");
            Assert.That(slime.AiOverrides.KillWeight, Is.EqualTo(2f));
            Assert.That(float.IsNaN(slime.AiOverrides.AttackWeight), Is.True);
            Assert.That(result.Package.AiProfiles.Any(item => item.AiProfileId == "general"), Is.True);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    /// <summary>Ensures a tampered catalog never reaches package composition.</summary>
    [Test]
    public void PhysicalContentPackRejectsCatalogHashMismatch()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string packDirectory = Path.Combine(temporary, "Packs", "tampered");
            Directory.CreateDirectory(packDirectory);
            File.WriteAllText(Path.Combine(packDirectory, "catalog.json"), "{}", new UTF8Encoding(false));
            string manifest = "{\"formatVersion\":2,\"packId\":\"tampered\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"deadbeef\"}";
            File.WriteAllText(Path.Combine(packDirectory, ContentPackLoader.ManifestFileName), manifest,
                new UTF8Encoding(false));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");

            Assert.Throws<InvalidDataException>(() =>
                ContentPackLoader.Load(baseRoot, new[] { Path.Combine(temporary, "Packs") }));
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    /// <summary>Ensures manifest paths cannot escape the physical pack directory.</summary>
    [Test]
    public void PhysicalContentPackRejectsCatalogPathTraversal()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string packDirectory = Path.Combine(temporary, "Packs", "escape");
            Directory.CreateDirectory(packDirectory);
            string catalog = "{\"schemaVersion\":3,\"contentVersion\":\"1.0.0\"}";
            File.WriteAllText(Path.Combine(temporary, "Packs", "outside.json"), catalog,
                new UTF8Encoding(false));
            string manifest = $"{{\"formatVersion\":2,\"packId\":\"escape\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"catalogFile\":\"../outside.json\",\"catalogSha256\":\"{ComputeSha256(catalog)}\"}}";
            File.WriteAllText(Path.Combine(packDirectory, ContentPackLoader.ManifestFileName), manifest,
                new UTF8Encoding(false));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");

            Assert.Throws<InvalidDataException>(() =>
                ContentPackLoader.Load(baseRoot, new[] { Path.Combine(temporary, "Packs") }));
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    /// <summary>Ensures invalid references introduced by a composed pack fail before registry construction.</summary>
    [Test]
    public void RuntimeCapabilityValidationRejectsMissingDeckCard()
    {
        ContentPackage package = new ContentPackage();
        DeckDefinition deck = new DeckDefinition { DeckId = "broken_deck", Enabled = true };
        deck.Entries.Add(new DeckEntryDefinition { CardId = "missing_card", Amount = 1 });
        package.Decks.Add(deck);

        Assert.Throws<InvalidDataException>(() => ContentRuntimeCapabilityValidator.ValidateOrThrow(package));
    }

    /// <summary>Ensures a broken optional user pack is isolated and reported instead of disabling required content.</summary>
    [Test]
    public void OptionalUserPackFailureProducesDiagnosticAndKeepsBasePackage()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string validDirectory = Path.Combine(temporary, "Valid", "keep");
            Directory.CreateDirectory(validDirectory);
            string catalog = "{\"schemaVersion\":3,\"contentVersion\":\"1.0.0\",\"cards\":[{\"cardId\":\"hit_01\",\"displayName\":\"爪击\",\"enabled\":true}]}";
            File.WriteAllText(Path.Combine(validDirectory, "catalog.json"), catalog, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(validDirectory, ContentPackLoader.ManifestFileName),
                $"{{\"formatVersion\":2,\"packId\":\"keep_pack\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"{ComputeSha256(catalog)}\"}}",
                new UTF8Encoding(false));
            string brokenDirectory = Path.Combine(temporary, "Broken", "broken-user-pack");
            Directory.CreateDirectory(brokenDirectory);
            File.WriteAllText(Path.Combine(brokenDirectory, "catalog.json"), "{}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(brokenDirectory, ContentPackLoader.ManifestFileName),
                "{\"formatVersion\":2,\"packId\":\"broken_user_pack\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"invalid\"}",
                new UTF8Encoding(false));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");

            ContentLoadResult result = ContentPackLoader.Load(baseRoot,
                new[]
                {
                    new ContentPackRoot(Path.Combine(temporary, "Valid"), true),
                    new ContentPackRoot(Path.Combine(temporary, "Broken"), false)
                }, Array.Empty<string>());

            Assert.That(result.LoadedPacks.Select(item => item.Definition.PackId), Is.EqualTo(new[] { "keep_pack" }));
            Assert.That(result.Diagnostics.Any(item => item.Severity == "error"), Is.True);
            Assert.That(result.Package.Cards.Any(item => item.CardId == "hit_01"), Is.True);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    /// <summary>Ensures user-disabled packs do not participate in composition or its deterministic fingerprint.</summary>
    [Test]
    public void DisabledPackIsExcludedFromCompositionAndFingerprint()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "lof-pack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string packDirectory = Path.Combine(temporary, "Packs", "toggle");
            Directory.CreateDirectory(packDirectory);
            string catalog = "{\"schemaVersion\":3,\"contentVersion\":\"1.0.0\",\"units\":[{\"unitId\":\"toggle_hero\",\"displayName\":\"Toggle\",\"initialHealth\":10,\"baseDamage\":1,\"moveSteps\":1,\"enabled\":true}]}";
            File.WriteAllText(Path.Combine(packDirectory, "catalog.json"), catalog, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(packDirectory, ContentPackLoader.ManifestFileName),
                $"{{\"formatVersion\":2,\"packId\":\"toggle_pack\",\"packVersion\":\"1.0.0\",\"schemaVersion\":3,\"catalogFile\":\"catalog.json\",\"catalogSha256\":\"{ComputeSha256(catalog)}\"}}",
                new UTF8Encoding(false));
            string baseRoot = Path.Combine(Application.dataPath, "StreamingAssets", "Content");
            ContentPackRoot root = new ContentPackRoot(Path.Combine(temporary, "Packs"), false);

            ContentLoadResult enabled = ContentPackLoader.Load(baseRoot, new[] { root }, Array.Empty<string>());
            ContentLoadResult disabled = ContentPackLoader.Load(baseRoot, new[] { root }, new[] { "toggle_pack" });

            Assert.That(enabled.LoadedPacks, Has.Count.EqualTo(1));
            Assert.That(disabled.LoadedPacks, Is.Empty);
            Assert.That(disabled.Diagnostics.Any(item => item.Severity == "info"), Is.True);
            Assert.That(enabled.Fingerprint, Is.Not.EqualTo(disabled.Fingerprint));
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static void CreateZipFromDirectory(string sourceDirectory, string destinationFile)
    {
        using FileStream stream = File.Create(destinationFile);
        using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            ZipArchiveEntry entry = archive.CreateEntry(relative);
            using Stream destination = entry.Open();
            using FileStream source = File.OpenRead(file);
            source.CopyTo(destination);
        }
    }

    private static string ComputeSha256(string value)
    {
        using SHA256 algorithm = SHA256.Create();
        byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
        return string.Concat(digest.Select(item => item.ToString("x2")));
    }

    /// <summary>Creates a minimal status graph that grants armor to its owning unit.</summary>
    /// <returns>A valid status definition with one turn-start graph.</returns>
    private static StatusDefinition CreateArmorStatus()
    {
        StatusDefinition status = new StatusDefinition
        {
            StatusId = "phase1_armor_status",
            DisplayName = "Phase 1 Armor"
        };
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = "phase1_armor_status.turn_start",
            OwnerKind = ContentDefinitionKinds.Status,
            OwnerId = status.StatusId,
            TriggerKey = "on_unit_turn_start"
        };
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "phase1_armor_status.root",
            NodeKind = "sequence",
            OperationKey = "sequence"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "phase1_armor_status.armor",
            ParentNodeId = "phase1_armor_status.root",
            BranchKey = "children",
            NodeKind = "effect",
            OperationKey = "gain_armor",
            ParametersJson = "{\"target\":\"self\",\"amount\":{\"kind\":\"constant\",\"value\":3}}"
        });
        status.Behaviors.Add(behavior);
        return status;
    }
}
#endif
