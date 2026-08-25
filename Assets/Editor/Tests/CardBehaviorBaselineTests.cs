#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LegendsOfFurry.Content.Contracts;
using LegendsOfFurry.Content.Runtime;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 验证数据库内容、通用行为执行器和战斗运行时在切换后的回归行为。
/// </summary>
public sealed class CardBehaviorBaselineTests
{
    /// <summary>
    /// 验证正式构建使用的内容门禁可以接受当前数据库发布包。
    /// </summary>
    [Test]
    public void CurrentPublishedContentPassesBuildGate()
    {
        ContentPackage package = ContentBuildValidator.ValidatePublishedContent();
        Assert.That(package.Cards, Has.Count.EqualTo(66));
        Assert.That(package.ClassProfiles, Has.Count.EqualTo(5));
    }

    /// <summary>
    /// 验证旧卡资产、旧牌组、硬编码目录和旧执行分支已经从工程中物理删除。
    /// </summary>
    [Test]
    public void LegacyCardAuthorityAndExecutionFilesAreAbsent()
    {
        string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        string[] removedPaths =
        {
            "Assets/Cards/Data/CD_Block.asset",
            "Assets/Cards/Data/CD_FireBall.asset",
            "Assets/Cards/Data/CD_Heal.asset",
            "Assets/Cards/Data/CD_Hit.asset",
            "Assets/Cards/Data/CD_Run.asset",
            "Assets/Cards/Decks/PlayerStartingDeck.asset",
            "Assets/Script/Cards/Data/CardCatalog.cs",
            "Assets/Script/Cards/Data/DeckData.cs"
        };
        foreach (string relativePath in removedPaths)
        {
            Assert.That(File.Exists(Path.Combine(projectRoot, relativePath)), Is.False, relativePath);
        }

        string resolverSource = File.ReadAllText(Path.Combine(
            projectRoot, "Assets/Script/Cards/Effects/CardEffectResolver.cs"));
        Assert.That(resolverSource, Does.Not.Contain("ResolveCard("));
    }

    /// <summary>
    /// 验证状态层数上限和寒冷转冻结的当前规则，作为后续状态数据化的迁移基线。
    /// </summary>
    [Test]
    public void StatusStackCapsAndColdConversionStayStable()
    {
        UnityEngine.GameObject owner = new UnityEngine.GameObject("CombatantStateBaselineOwner");
        try
        {
            CombatantState state = owner.AddComponent<CombatantState>();
            state.Add(CombatStatus.Broken, 50);
            state.Add(CombatStatus.Exhaustion, 50);
            state.Add(CombatStatus.Cold, 3);

            Assert.That(state.Get(CombatStatus.Broken), Is.EqualTo(10));
            Assert.That(state.Get(CombatStatus.Exhaustion), Is.EqualTo(3));
            Assert.That(state.Has(CombatStatus.Cold), Is.False);
            Assert.That(state.Get(CombatStatus.Frozen), Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    /// <summary>
    /// 验证 Unity 运行时加载器可以读取桌面发布器生成的当前内容包并建立完整只读注册表。
    /// </summary>
    [Test]
    public void PublishedDevelopmentPackageLoadsIntoRuntimeRegistry()
    {
        string contentRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Content");
        ContentPackage package = ContentPackageLoader.LoadFromDirectory(contentRoot);
        ContentRegistry registry = new ContentRegistry(package);

        Assert.That(package.SchemaVersion, Is.EqualTo(ContentPackageLoader.SupportedSchemaVersion));
        Assert.That(package.ContentVersion, Is.Not.Empty);
        Assert.That(registry.GetCard("hit_01").DisplayName, Is.EqualTo("爪击"));
        Assert.That(registry.Cards, Has.Count.EqualTo(66));
        Assert.That(registry.GetDeck("planner_test").IsTestDeck, Is.True);
        Assert.That(registry.Statuses, Has.Count.EqualTo(20));
        Assert.That(registry.ClassProfiles, Has.Count.EqualTo(5));
        Assert.That(registry.GameSettings.HandLimit, Is.EqualTo(10));
        Assert.That(registry.ClassProfiles.Single(item => item.ClassId == "ranger")
            .GetTraitInt("bow_range_bonus"), Is.EqualTo(1));
        Assert.That(registry.ClassProfiles.Single(item => item.ClassId == "priest")
            .GetTraitBool("revive_available"), Is.True);
    }

    /// <summary>
    /// 验证数据库卡牌实例能投影成现有手牌界面读取的 CardData，同时保留权威共享定义供新执行器使用。
    /// </summary>
    [Test]
    public void PublishedCardCreatesLegacyViewWithoutLosingDefinition()
    {
        string contentRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Content");
        ContentRegistry registry = new ContentRegistry(ContentPackageLoader.LoadFromDirectory(contentRoot));
        CardDefinition definition = registry.GetCard("hit_01");

        CardInstance instance = new CardInstance(definition);

        Assert.That(instance.Definition, Is.SameAs(definition));
        Assert.That(instance.Data.cardId, Is.EqualTo(definition.CardId));
        Assert.That(instance.Data.actionPointCost, Is.EqualTo(1));
        Assert.That(instance.Data.targetMode, Is.EqualTo(CardTargetMode.Unit));
        Assert.That(ContentCardEffectExecutor.CanExecuteOnPlay(definition), Is.True);
    }

    /// <summary>
    /// 验证发布的策划测试牌库可以展开为保留共享定义的运行时实例并进入现有手牌管线。
    /// </summary>
    [Test]
    public void PublishedPlannerDeckExpandsToRuntimeCardInstances()
    {
        string contentRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Content");
        ContentRegistry registry = new ContentRegistry(ContentPackageLoader.LoadFromDirectory(contentRoot));

        IReadOnlyList<CardInstance> instances = ContentDeckFactory.CreateInstances(registry, "planner_test");

        Assert.That(instances, Has.Count.EqualTo(66));
        Assert.That(instances.Any(instance => instance.Definition.CardId == "hit_01"), Is.True);
        Assert.That(instances.All(instance => instance.Definition != null), Is.True);
    }

    /// <summary>验证正式发布包全部 66 张卡都能通过运行时能力预检，且所有可打出卡都有可执行入口。</summary>
    [Test]
    public void PublishedFullCardCatalogPassesRuntimeSmokeValidation()
    {
        string contentRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Content");
        ContentPackage package = ContentPackageLoader.LoadFromDirectory(contentRoot);

        Assert.DoesNotThrow(() => ContentRuntimeCapabilityValidator.ValidateOrThrow(package));
        Assert.That(package.Cards, Has.Count.EqualTo(66));
        foreach (CardDefinition card in package.Cards.Where(card => card.Enabled && !card.Unplayable))
        {
            Assert.That(ContentCardEffectExecutor.CanExecuteOnPlay(card), Is.True,
                $"可打出卡 {card.CardId} 缺少可执行 on_play 行为。");
        }
    }

    /// <summary>验证抽牌与手牌回合结束触发器已经成为可执行内容能力，不再要求具体卡牌 ID 分支。</summary>
    [Test]
    public void PublishedCurseCardsExposeExecutableLifecycleTriggers()
    {
        string contentRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Content");
        ContentRegistry registry = new ContentRegistry(ContentPackageLoader.LoadFromDirectory(contentRoot));

        Assert.That(ContentCardEffectExecutor.CanExecuteTrigger(registry.GetCard("crystal_backlash"), "on_draw"), Is.True);
        Assert.That(ContentCardEffectExecutor.CanExecuteTrigger(registry.GetCard("crystal_shatter"), "on_turn_end_in_hand"), Is.True);
        Assert.That(ContentCardEffectExecutor.CanExecuteTrigger(registry.GetCard("cross_possessed"), "on_turn_end_in_hand"), Is.True);
        Assert.That(ContentCardEffectExecutor.CanExecuteTrigger(registry.GetCard("cloak_broken"), "on_turn_end_in_hand"), Is.True);
    }

    /// <summary>
    /// 验证发布包中的结构化伤害节点会通过通用执行器实际扣除目标生命，而不查询具体卡牌 ID。
    /// </summary>
    [Test]
    public void PublishedDamageCardExecutesWithoutCardIdBranch()
    {
        UnityEngine.GameObject sourceObject = new UnityEngine.GameObject("ContentDamageSource");
        UnityEngine.GameObject targetObject = new UnityEngine.GameObject("ContentDamageTarget");
        sourceObject.SetActive(false);
        targetObject.SetActive(false);
        try
        {
            Unit source = sourceObject.AddComponent<Unit>();
            Unit target = targetObject.AddComponent<Unit>();
            source.ConfigureCombatant("来源", 20, 1, 1);
            target.ConfigureCombatant("目标", 20, 1, 1);
            source.SetFaction(UnitFaction.Player);
            target.SetFaction(UnitFaction.Enemy);

            string contentRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "Content");
            CardDefinition definition = ContentPackageLoader.LoadFromDirectory(contentRoot).Cards
                .Single(card => card.CardId == "hit_01");
            CardPlayResult result = new CardPlayResult();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                new CardInstance(definition), source, target, null, null, null, null, result, false);

            bool succeeded = ContentCardEffectExecutor.TryExecuteOnPlay(context);

            Assert.That(succeeded, Is.True);
            Assert.That(target.CurrentHealth, Is.EqualTo(16));
            Assert.That(context.DamageRecords, Has.Count.EqualTo(1));
            Assert.That(context.DamageRecords[0].NodeId, Is.Not.Empty);
            Assert.That(context.DamageRecords[0].RequestedDamage, Is.EqualTo(4));
            Assert.That(context.DamageRecords[0].HealthDamage, Is.EqualTo(4));
            Assert.That(context.DamageRecords[0].KilledTarget, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    /// <summary>
    /// 验证治疗、护甲、抽牌、免费移动和结束回合均通过同一行为序列执行并写入共享结果。
    /// </summary>
    [Test]
    public void RemainingPhaseTwoEffectsExecuteThroughSharedContext()
    {
        UnityEngine.GameObject sourceObject = new UnityEngine.GameObject("ContentEffectSource");
        UnityEngine.GameObject targetObject = new UnityEngine.GameObject("ContentEffectTarget");
        sourceObject.SetActive(false);
        targetObject.SetActive(false);
        try
        {
            Unit source = sourceObject.AddComponent<Unit>();
            Unit target = targetObject.AddComponent<Unit>();
            source.ConfigureCombatant("来源", 20, 1, 1);
            target.ConfigureCombatant("目标", 20, 1, 1);
            target.TakeTypedDamage(5, DamageType.True, source, false);
            RecordingCardDrawService drawService = new RecordingCardDrawService();
            CardDefinition definition = CreatePhaseTwoEffectSequenceCard();
            CardPlayResult result = new CardPlayResult();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                new CardInstance(definition), source, target, null, null, drawService, null, result, false);

            bool succeeded = ContentCardEffectExecutor.TryExecuteOnPlay(context);

            Assert.That(succeeded, Is.True);
            Assert.That(target.CurrentHealth, Is.EqualTo(18));
            Assert.That(target.Armor, Is.EqualTo(4));
            Assert.That(drawService.DrawnCards, Is.EqualTo(2));
            Assert.That(result.FreeMoveSteps, Is.EqualTo(2));
            Assert.That(result.EndTurn, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    /// <summary>
    /// 验证结构化固定费用、行动点 X 费用、法力 X 费用和免费打出均返回确定的实际消耗。
    /// </summary>
    [Test]
    public void ContentCostsSupportFixedXAndFreePlayWithoutParsingDisplayText()
    {
        CardDefinition fixedCost = new CardDefinition
        {
            Cost = new CardCostDefinition { ActionCost = 2, ManaCost = 3 }
        };
        Assert.That(ContentCardPlayRules.TryCalculateCost(fixedCost, 4, 5, false, out int action, out int mana), Is.True);
        Assert.That(action, Is.EqualTo(2));
        Assert.That(mana, Is.EqualTo(3));
        Assert.That(ContentCardPlayRules.TryCalculateCost(fixedCost, 1, 5, false, out _, out _), Is.False);

        CardDefinition xCost = new CardDefinition
        {
            Cost = new CardCostDefinition { SpendAllAction = true, SpendAllMana = true }
        };
        Assert.That(ContentCardPlayRules.TryCalculateCost(xCost, 4, 5, false, out action, out mana), Is.True);
        Assert.That(action, Is.EqualTo(4));
        Assert.That(mana, Is.EqualTo(5));
        Assert.That(ContentCardPlayRules.TryCalculateCost(xCost, 0, 5, false, out _, out _), Is.False);
        Assert.That(ContentCardPlayRules.TryCalculateCost(xCost, 0, 0, true, out action, out mana), Is.True);
        Assert.That(action, Is.Zero);
        Assert.That(mana, Is.Zero);
    }

    /// <summary>
    /// 验证 Self、Unit、Cell 和正交 Direction 四类入口以及阵营、存活、距离和视线约束。
    /// </summary>
    [Test]
    public void ContentTargetsSupportAllPhaseTwoSelectionModesAndFilters()
    {
        CardDefinition card = new CardDefinition();
        Assert.That(ContentCardPlayRules.ValidateTarget(card, new ContentCardTargetSelection()), Is.True);

        card.Target = new CardTargetRule
        {
            SelectionMode = "unit",
            Range = 2,
            TeamFilter = "enemy",
            LifeStateFilter = "alive",
            RequiresLineOfSight = true,
            AllowSelf = false
        };
        ContentCardTargetSelection enemy = new ContentCardTargetSelection
        {
            HasUnit = true,
            Distance = 2,
            TargetIsAlive = true,
            TargetHasSameFaction = false,
            HasLineOfSight = true
        };
        Assert.That(ContentCardPlayRules.ValidateTarget(card, enemy), Is.True);
        enemy.HasLineOfSight = false;
        Assert.That(ContentCardPlayRules.ValidateTarget(card, enemy), Is.False);
        enemy.HasLineOfSight = true;
        enemy.TargetHasSameFaction = true;
        Assert.That(ContentCardPlayRules.ValidateTarget(card, enemy), Is.False);
        enemy.TargetHasSameFaction = false;
        enemy.Distance = 3;
        Assert.That(ContentCardPlayRules.ValidateTarget(card, enemy), Is.False);

        card.Target = new CardTargetRule { SelectionMode = "cell", Range = 3 };
        Assert.That(ContentCardPlayRules.ValidateTarget(card,
            new ContentCardTargetSelection { HasCell = true, Distance = 3 }), Is.True);

        card.Target = new CardTargetRule { SelectionMode = "direction" };
        Assert.That(ContentCardPlayRules.ValidateTarget(card,
            new ContentCardTargetSelection { HasDirection = true, DirectionX = 1 }), Is.True);
        Assert.That(ContentCardPlayRules.ValidateTarget(card,
            new ContentCardTargetSelection { HasDirection = true, DirectionX = 1, DirectionY = 1 }), Is.False);
    }

    /// <summary>
    /// 验证出牌上下文固定资源快照、伤害击杀归属和卡牌实例运行时整数状态。
    /// </summary>
    [Test]
    public void ExecutionContextTracksResourcesKillsAndCardRuntimeValues()
    {
        UnityEngine.GameObject sourceObject = new UnityEngine.GameObject("ExecutionContextSource");
        UnityEngine.GameObject targetObject = new UnityEngine.GameObject("ExecutionContextTarget");
        sourceObject.SetActive(false);
        targetObject.SetActive(false);
        try
        {
            Unit source = sourceObject.AddComponent<Unit>();
            Unit target = targetObject.AddComponent<Unit>();
            source.ConfigureCombatant("来源", 20, 1, 1);
            target.ConfigureCombatant("目标", 4, 1, 1);
            CardDefinition definition = CreateDamageCard("context_kill_test", 5);
            CardInstance instance = new CardInstance(definition);
            CardPlayResult result = new CardPlayResult();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                instance, source, target, null, null, null, null, result, false, 3, 7, 2, 4);

            bool succeeded = ContentCardEffectExecutor.TryExecuteOnPlay(context);
            instance.SetRuntimeValue("charge", 2);
            int changedValue = instance.ModifyRuntimeValue("charge", 3);

            Assert.That(succeeded, Is.True);
            Assert.That(context.Resources.ActionPointsBefore, Is.EqualTo(3));
            Assert.That(context.Resources.ActionPointsAfter, Is.EqualTo(1));
            Assert.That(context.Resources.ManaBefore, Is.EqualTo(7));
            Assert.That(context.Resources.ManaAfter, Is.EqualTo(3));
            Assert.That(context.WasKilledByThisCard(target), Is.True);
            Assert.That(context.DamageRecords.Single().HealthDamage, Is.EqualTo(4));
            Assert.That(changedValue, Is.EqualTo(5));
            Assert.That(instance.GetRuntimeValue("charge"), Is.EqualTo(5));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    /// <summary>
    /// 验证行为图解释器以稳定顺序执行 Sequence、Condition、Repeat、ForEach 并能在取消后立即停止。
    /// </summary>
    [Test]
    public void BehaviorGraphInterpreterHandlesAllStructuralNodesAndCancellation()
    {
        BehaviorDefinition behavior = CreateStructuralBehaviorGraph();
        ContentCardExecutionContext context = new ContentCardExecutionContext(
            new CardInstance(new CardDefinition { CardId = "graph_test" }),
            null,
            null,
            null,
            null,
            null,
            null,
            new CardPlayResult(),
            false);
        RecordingBehaviorRuntime runtime = new RecordingBehaviorRuntime();

        ContentBehaviorExecutionStatus status = ContentBehaviorGraphInterpreter.Execute(behavior, context, runtime);

        Assert.That(status, Is.EqualTo(ContentBehaviorExecutionStatus.Succeeded));
        Assert.That(runtime.ExecutedEffects, Is.EqualTo(6));
        Assert.That(runtime.IteratedTargets, Is.EqualTo(new[] { "first", "second" }));
        Assert.That(context.CurrentGraphTarget, Is.Null);

        CancellationTokenSource cancellation = new CancellationTokenSource();
        runtime = new RecordingBehaviorRuntime(cancellation);
        status = ContentBehaviorGraphInterpreter.Execute(behavior, context, runtime, cancellation.Token);
        Assert.That(status, Is.EqualTo(ContentBehaviorExecutionStatus.Cancelled));
        Assert.That(runtime.ExecutedEffects, Is.EqualTo(1));
    }

    /// <summary>
    /// 验证数值表达式注册表读取资源快照、单位状态和嵌套运算，并拒绝未知 key 与除零。
    /// </summary>
    [Test]
    public void ValueExpressionRegistryResolvesControlledTreesAndRejectsInvalidOperations()
    {
        UnityEngine.GameObject sourceObject = new UnityEngine.GameObject("ExpressionSource");
        UnityEngine.GameObject targetObject = new UnityEngine.GameObject("ExpressionTarget");
        sourceObject.SetActive(false);
        targetObject.SetActive(false);
        try
        {
            Unit source = sourceObject.AddComponent<Unit>();
            Unit target = targetObject.AddComponent<Unit>();
            source.ConfigureCombatant("来源", 20, 1, 1);
            target.ConfigureCombatant("目标", 10, 1, 1);
            target.State.Add(CombatStatus.Poison, 3);
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                new CardInstance(new CardDefinition { CardId = "expression_test" }),
                source,
                target,
                null,
                null,
                null,
                null,
                new CardPlayResult(),
                false,
                4,
                6,
                2,
                3,
                new FixedContentRandomSource());
            ContentValueExpressionResolver resolver = new ContentValueExpressionResolver();

            bool resolved = resolver.TryResolveJson(
                "{\"kind\":\"add\",\"left\":{\"kind\":\"spent_action_points\"},\"right\":{\"kind\":\"target_current_health\"}}",
                context,
                target,
                out int value);
            Assert.That(resolved, Is.True);
            Assert.That(value, Is.EqualTo(12));

            ContentValueExpression statusStacks = new ContentValueExpression
            {
                kind = "status_stacks",
                target = "selected_unit",
                statusId = "Poison"
            };
            Assert.That(resolver.TryResolve(statusStacks, context, target, out value), Is.True);
            Assert.That(value, Is.EqualTo(3));

            ContentValueExpression random = new ContentValueExpression
            {
                kind = "random_range",
                minimum = new ContentValueExpression { kind = "constant", value = 1 },
                maximum = new ContentValueExpression { kind = "constant", value = 3 }
            };
            Assert.That(resolver.TryResolve(random, context, target, out value), Is.True);
            Assert.That(value, Is.EqualTo(3));

            ContentValueExpression invalidDivide = new ContentValueExpression
            {
                kind = "divide",
                left = new ContentValueExpression { kind = "constant", value = 8 },
                right = new ContentValueExpression { kind = "constant", value = 0 }
            };
            Assert.That(resolver.TryResolve(invalidDivide, context, target, out _), Is.False);
            Assert.That(resolver.TryResolve(new ContentValueExpression { kind = "unknown" }, context, target, out _), Is.False);
            Assert.That(resolver.Contains("add"), Is.True);
            Assert.That(resolver.Contains("unknown"), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    /// <summary>
    /// 验证正式 OnPlay 管线通过条件和 foreach 注册表对所有敌人执行效果，并在扣费前拒绝未注册条件。
    /// </summary>
    [Test]
    public void RegisteredConditionsAndTargetSelectorsExecuteThroughProductionGraphRuntime()
    {
        UnityEngine.GameObject sourceObject = new UnityEngine.GameObject("RegistrySource");
        UnityEngine.GameObject firstTargetObject = new UnityEngine.GameObject("RegistryTargetA");
        UnityEngine.GameObject secondTargetObject = new UnityEngine.GameObject("RegistryTargetB");
        sourceObject.SetActive(false);
        firstTargetObject.SetActive(false);
        secondTargetObject.SetActive(false);
        try
        {
            Unit source = sourceObject.AddComponent<Unit>();
            Unit firstTarget = firstTargetObject.AddComponent<Unit>();
            Unit secondTarget = secondTargetObject.AddComponent<Unit>();
            source.ConfigureCombatant("来源", 20, 1, 1);
            firstTarget.ConfigureCombatant("目标甲", 10, 1, 1);
            secondTarget.ConfigureCombatant("目标乙", 10, 1, 1);
            source.SetFaction(UnitFaction.Player);
            firstTarget.SetFaction(UnitFaction.Enemy);
            secondTarget.SetFaction(UnitFaction.Enemy);
            CardDefinition definition = CreateConditionalAreaDamageCard();
            CardInstance instance = new CardInstance(definition);
            RecordingTargetQueryService query = new RecordingTargetQueryService(source, firstTarget, secondTarget);
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                instance,
                source,
                firstTarget,
                null,
                null,
                null,
                null,
                new CardPlayResult(),
                false,
                3,
                0,
                2,
                0,
                new FixedContentRandomSource(),
                query);

            Assert.That(ContentCardEffectExecutor.CanExecuteOnPlay(definition), Is.True);
            Assert.That(ContentCardEffectExecutor.TryExecuteOnPlay(context), Is.True);
            Assert.That(firstTarget.CurrentHealth, Is.EqualTo(8));
            Assert.That(secondTarget.CurrentHealth, Is.EqualTo(8));
            Assert.That(context.DamageRecords, Has.Count.EqualTo(2));

            definition.Behaviors.Single().Nodes.Single(node => node.NodeKind == "condition").OperationKey = "target_has_tag";
            Assert.That(ContentCardEffectExecutor.CanExecuteOnPlay(definition), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(firstTargetObject);
            UnityEngine.Object.DestroyImmediate(secondTargetObject);
        }
    }

    /// <summary>
    /// 验证内容包启动预检接受已注册行为图，并在进入战斗前拒绝未知 Trigger 或未实现条件。
    /// </summary>
    [Test]
    public void RuntimeCapabilityValidationRejectsUnsupportedGraphKeysBeforeBattle()
    {
        CardDefinition validCard = CreateConditionalAreaDamageCard();
        ContentPackage package = new ContentPackage();
        package.Cards.Add(validCard);

        Assert.DoesNotThrow(() => ContentRuntimeCapabilityValidator.ValidateOrThrow(package));

        BehaviorDefinition behavior = validCard.Behaviors.Single();
        behavior.Nodes.Single(node => node.NodeKind == "condition").OperationKey = "target_has_tag";
        Assert.Throws<InvalidDataException>(() => ContentRuntimeCapabilityValidator.ValidateOrThrow(package));

        behavior.Nodes.Single(node => node.NodeKind == "condition").OperationKey = "spent_action_compare";
        behavior.TriggerKey = "on_discard";
        Assert.Throws<InvalidDataException>(() => ContentRuntimeCapabilityValidator.ValidateOrThrow(package));
    }

    /// <summary>验证伤害前后事件、护甲吸收和生命伤害以同一结构化结果对外暴露。</summary>
    [Test]
    public void DamagePipelinePublishesOrderedStructuredResolution()
    {
        UnityEngine.GameObject targetObject = new UnityEngine.GameObject("DamagePipelineTarget");
        targetObject.SetActive(false);
        try
        {
            Unit target = targetObject.AddComponent<Unit>();
            target.ConfigureCombatant("目标", 10, 1, 1);
            target.AddArmor(3);
            List<string> order = new List<string>();
            target.BeforeDamage += request => { order.Add("before"); request.Amount += 2; };
            target.AfterDamage += resolution => order.Add("after");
            DamageResolution result = target.ResolveDamage(new DamageRequest(null, target, 5, DamageType.Normal, false));
            Assert.That(order, Is.EqualTo(new[] { "before", "after" }));
            Assert.That(result.FinalDamage, Is.EqualTo(7));
            Assert.That(result.AbsorbedByArmor, Is.EqualTo(3));
            Assert.That(result.HealthDamage, Is.EqualTo(4));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    /// <summary>验证字符串状态容器保存层数、持续时间、来源并兼容旧枚举访问。</summary>
    [Test]
    public void StringStatusContainerKeepsInstanceMetadataAndLegacyCompatibility()
    {
        UnityEngine.GameObject ownerObject = new UnityEngine.GameObject("StatusOwner");
        ownerObject.SetActive(false);
        try
        {
            CombatantState state = ownerObject.AddComponent<CombatantState>();
            state.Add("poison", 3, 2, "test.card");
            RuntimeStatusInstance instance = state.GetStatusSnapshot().Single();
            Assert.That(state.Get(CombatStatus.Poison), Is.EqualTo(3));
            Assert.That(instance.RemainingTurns, Is.EqualTo(2));
            Assert.That(instance.SourceId, Is.EqualTo("test.card"));
            state.Reduce("poison", 2);
            Assert.That(state.Get("poison"), Is.EqualTo(1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ownerObject);
        }
    }

    /// <summary>验证异步效果暂停上下文并由服务回调恢复，同时生成可定位节点日志。</summary>
    [Test]
    public void AsyncEffectPausesUntilCallbackAndWritesStructuredLog()
    {
        UnityEngine.GameObject ownerObject = new UnityEngine.GameObject("AsyncOwner");
        ownerObject.SetActive(false);
        try
        {
            Unit owner = ownerObject.AddComponent<Unit>();
            owner.ConfigureCombatant("来源", 10, 1, 1);
            CardDefinition card = CreateDamageCard("async_test", 1);
            BehaviorNodeDefinition effect = card.Behaviors.Single().Nodes.Single(node => node.NodeKind == "effect");
            effect.OperationKey = "reveal_top_cards_and_choose_discard";
            effect.ParametersJson = "{\"amount\":{\"kind\":\"constant\",\"value\":3}}";
            RecordingCardZoneService zones = new RecordingCardZoneService();
            ContentCardExecutionContext context = new ContentCardExecutionContext(
                new CardInstance(card), owner, owner, null, null, zones, null, new CardPlayResult(), false);
            Assert.That(ContentCardEffectExecutor.TryExecuteOnPlay(context), Is.True);
            Assert.That(context.IsAwaitingInteraction, Is.True);
            Assert.That(context.CombatLog.Single().NodeId, Is.EqualTo(effect.NodeId));
            zones.Complete();
            Assert.That(context.IsAwaitingInteraction, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ownerObject);
        }
    }

    /// <summary>
    /// 创建“消耗至少 2 行动力时，对所有敌人造成 2 点伤害”的行为图卡牌。
    /// </summary>
    /// <returns>同时覆盖 Condition、ForEach 和当前图目标的卡牌定义。</returns>
    private static CardDefinition CreateConditionalAreaDamageCard()
    {
        CardDefinition card = new CardDefinition
        {
            CardId = "conditional_area_damage_test",
            DisplayName = "条件范围伤害测试",
            Target = new CardTargetRule { SelectionMode = "unit" }
        };
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = card.CardId + ".on_play",
            OwnerKind = "card",
            OwnerId = card.CardId,
            TriggerKey = "on_play"
        };
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "area.root",
            NodeKind = "sequence",
            OperationKey = "sequence"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "area.condition",
            ParentNodeId = "area.root",
            BranchKey = "children",
            NodeKind = "condition",
            OperationKey = "spent_action_compare",
            ParametersJson = "{\"comparison\":\"gte\",\"value\":{\"kind\":\"constant\",\"value\":2}}"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "area.foreach",
            ParentNodeId = "area.condition",
            BranchKey = "then",
            NodeKind = "foreach",
            OperationKey = "all_enemies",
            ParametersJson = "{}"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "area.damage",
            ParentNodeId = "area.foreach",
            BranchKey = "children",
            NodeKind = "effect",
            OperationKey = "damage",
            ParametersJson = "{\"target\":\"current_graph_target\",\"amount\":{\"kind\":\"constant\",\"value\":2},\"damageType\":\"normal\"}"
        });
        card.Behaviors.Add(behavior);
        return card;
    }

    /// <summary>
    /// 创建同时包含条件、重复和目标遍历节点的确定性测试行为图。
    /// </summary>
    /// <returns>成功执行时共调用六次叶子效果的行为图。</returns>
    private static BehaviorDefinition CreateStructuralBehaviorGraph()
    {
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = "graph_test.on_play",
            OwnerKind = "card",
            OwnerId = "graph_test",
            TriggerKey = "on_play"
        };
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "root",
            NodeKind = "sequence",
            OperationKey = "sequence"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "plain",
            ParentNodeId = "root",
            BranchKey = "children",
            SortOrder = 1,
            NodeKind = "effect",
            OperationKey = "no_op"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "condition",
            ParentNodeId = "root",
            BranchKey = "children",
            SortOrder = 2,
            NodeKind = "condition",
            OperationKey = "always_for_test"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "condition_then",
            ParentNodeId = "condition",
            BranchKey = "then",
            NodeKind = "effect",
            OperationKey = "no_op"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "repeat",
            ParentNodeId = "root",
            BranchKey = "children",
            SortOrder = 3,
            NodeKind = "repeat",
            OperationKey = "repeat",
            ParametersJson = "{\"count\":{\"kind\":\"constant\",\"value\":2}}"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "repeat_child",
            ParentNodeId = "repeat",
            BranchKey = "children",
            NodeKind = "effect",
            OperationKey = "no_op"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "foreach",
            ParentNodeId = "root",
            BranchKey = "children",
            SortOrder = 4,
            NodeKind = "foreach",
            OperationKey = "test_targets"
        });
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = "foreach_child",
            ParentNodeId = "foreach",
            BranchKey = "children",
            NodeKind = "effect",
            OperationKey = "no_op"
        });
        return behavior;
    }

    /// <summary>
    /// 创建只有一个常量普通伤害节点的数据库卡牌定义。
    /// </summary>
    /// <param name="cardId">测试卡牌稳定 ID。</param>
    /// <param name="damage">常量伤害值。</param>
    /// <returns>可由阶段 2 通用执行器直接结算的卡牌。</returns>
    private static CardDefinition CreateDamageCard(string cardId, int damage)
    {
        CardDefinition card = new CardDefinition
        {
            CardId = cardId,
            DisplayName = cardId,
            Target = new CardTargetRule { SelectionMode = "unit" }
        };
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = cardId + ".on_play",
            OwnerKind = "card",
            OwnerId = cardId,
            TriggerKey = "on_play"
        };
        string rootId = behavior.BehaviorId + ".root";
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = rootId,
            NodeKind = "sequence",
            OperationKey = "sequence"
        });
        AddEffectNode(
            behavior,
            rootId,
            1,
            "damage",
            $"{{\"target\":\"selected_unit\",\"amount\":{{\"kind\":\"constant\",\"value\":{damage}}},\"damageType\":\"normal\"}}");
        card.Behaviors.Add(behavior);
        return card;
    }

    /// <summary>
    /// 创建覆盖阶段 2 非伤害效果的顺序行为卡，用于执行器隔离测试。
    /// </summary>
    /// <returns>包含治疗、护甲、抽牌、免费移动和结束回合节点的卡牌定义。</returns>
    private static CardDefinition CreatePhaseTwoEffectSequenceCard()
    {
        CardDefinition card = new CardDefinition
        {
            CardId = "phase_two_effect_test",
            DisplayName = "阶段二效果测试",
            Target = new CardTargetRule { SelectionMode = "unit" }
        };
        BehaviorDefinition behavior = new BehaviorDefinition
        {
            BehaviorId = "phase_two_effect_test.on_play",
            OwnerKind = "card",
            OwnerId = card.CardId,
            TriggerKey = "on_play"
        };
        string rootId = behavior.BehaviorId + ".root";
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = rootId,
            NodeKind = "sequence",
            OperationKey = "sequence"
        });
        AddEffectNode(behavior, rootId, 1, "heal", "{\"target\":\"selected_unit\",\"amount\":{\"kind\":\"constant\",\"value\":3}}");
        AddEffectNode(behavior, rootId, 2, "gain_armor", "{\"target\":\"selected_unit\",\"amount\":{\"kind\":\"constant\",\"value\":4}}");
        AddEffectNode(behavior, rootId, 3, "draw_cards", "{\"amount\":{\"kind\":\"constant\",\"value\":2}}");
        AddEffectNode(behavior, rootId, 4, "begin_free_move", "{\"amount\":{\"kind\":\"constant\",\"value\":2}}");
        AddEffectNode(behavior, rootId, 5, "end_turn", "{}");
        card.Behaviors.Add(behavior);
        return card;
    }

    /// <summary>
    /// 向测试行为追加一个具有确定顺序和稳定节点 ID 的基础效果。
    /// </summary>
    /// <param name="behavior">需要追加节点的行为。</param>
    /// <param name="rootId">顺序根节点 ID。</param>
    /// <param name="order">节点执行顺序。</param>
    /// <param name="operationKey">基础效果 key。</param>
    /// <param name="parametersJson">结构化参数 JSON。</param>
    private static void AddEffectNode(
        BehaviorDefinition behavior,
        string rootId,
        int order,
        string operationKey,
        string parametersJson)
    {
        behavior.Nodes.Add(new BehaviorNodeDefinition
        {
            NodeId = $"{behavior.BehaviorId}.{operationKey}.{order}",
            ParentNodeId = rootId,
            BranchKey = "children",
            SortOrder = order,
            NodeKind = "effect",
            OperationKey = operationKey,
            ParametersJson = parametersJson
        });
    }

    /// <summary>
    /// 在执行器测试中记录抽牌请求而不创建手牌界面或牌堆对象。
    /// </summary>
    private sealed class RecordingCardDrawService : ICardDrawService
    {
        public int DrawnCards { get; private set; }

        /// <summary>
        /// 累加执行器请求的抽牌张数，忽略仅属于真实手牌系统的强制弃牌表现。
        /// </summary>
        /// <param name="count">本次请求抽取的张数。</param>
        /// <param name="mandatory">是否要求手牌满时继续处理。</param>
        public void DrawCards(int count, bool mandatory)
        {
            DrawnCards += count;
        }
    }

    /// <summary>模拟会保留完成回调的牌区交互服务。</summary>
    private sealed class RecordingCardZoneService : IContentCardZoneService
    {
        private System.Action completion;

        /// <summary>触发测试保存的玩家交互完成回调。</summary>
        public void Complete() => completion?.Invoke();

        /// <summary>本测试不需要实际抽牌。</summary>
        public void DrawCards(int count, bool mandatory) { }

        /// <summary>本测试不需要实际生成牌。</summary>
        public bool GenerateCards(ContentCardQuery query, int count, string destinationZone) => true;

        /// <summary>本测试不需要实际移动牌。</summary>
        public int MoveCards(ContentCardQuery query, int count, string sourceZone, string destinationZone) => 0;

        /// <summary>本测试不需要实际移除牌。</summary>
        public int RemoveCards(ContentCardQuery query, string zone) => 0;

        /// <summary>保存看牌交互完成回调，模拟界面尚未确认。</summary>
        public void RevealTopCardsAndChooseDiscard(int count, System.Action onComplete) => completion = onComplete;

        /// <summary>保存免费打牌交互完成回调。</summary>
        public void PlayTopCardsForFree(int count, System.Action onComplete) => completion = onComplete;
    }

    /// <summary>
    /// 为结构调度测试提供固定条件、固定目标和可观察效果次数，并可在首个效果后触发取消。
    /// </summary>
    private sealed class RecordingBehaviorRuntime : IContentBehaviorRuntime
    {
        private readonly CancellationTokenSource cancellation;

        /// <summary>
        /// 创建行为图测试运行时；传入取消源时会在第一个效果完成后请求取消。
        /// </summary>
        /// <param name="cancellationSource">可选的测试取消源。</param>
        public RecordingBehaviorRuntime(CancellationTokenSource cancellationSource = null)
        {
            cancellation = cancellationSource;
        }

        public int ExecutedEffects { get; private set; }
        public List<string> IteratedTargets { get; } = new List<string>();

        /// <summary>
        /// 为测试中的唯一条件返回 true。
        /// </summary>
        /// <param name="node">当前条件节点。</param>
        /// <param name="context">本次卡牌上下文。</param>
        /// <param name="result">固定返回的 true。</param>
        /// <returns>始终返回 true 表示条件已成功求值。</returns>
        public bool TryEvaluateCondition(
            BehaviorNodeDefinition node,
            ContentCardExecutionContext context,
            out bool result)
        {
            result = true;
            return true;
        }

        /// <summary>
        /// 为测试 foreach 节点返回两个顺序稳定的字符串目标。
        /// </summary>
        /// <param name="node">当前 foreach 节点。</param>
        /// <param name="context">本次卡牌上下文。</param>
        /// <param name="targets">固定的两个目标。</param>
        /// <returns>始终返回 true 表示选择器已成功解析。</returns>
        public bool TryResolveTargets(
            BehaviorNodeDefinition node,
            ContentCardExecutionContext context,
            out IReadOnlyList<object> targets)
        {
            targets = new object[] { "first", "second" };
            return true;
        }

        /// <summary>
        /// 记录一次叶子效果，并保存 foreach 当前目标；测试取消模式下随后请求取消。
        /// </summary>
        /// <param name="node">当前效果节点。</param>
        /// <param name="context">本次卡牌上下文。</param>
        /// <returns>始终返回 true 表示叶子效果成功。</returns>
        public bool TryExecuteEffect(BehaviorNodeDefinition node, ContentCardExecutionContext context)
        {
            ExecutedEffects++;
            if (context.CurrentGraphTarget is string target)
            {
                IteratedTargets.Add(target);
            }
            cancellation?.Cancel();
            return true;
        }
    }

    /// <summary>
    /// 为表达式测试提供可预测的随机结果，整数范围总返回上界，小数固定返回 0.5。
    /// </summary>
    private sealed class FixedContentRandomSource : IContentRandomSource
    {
        /// <summary>返回调用方提供的闭区间上界。</summary>
        public int NextInclusive(int minimum, int maximum)
        {
            return maximum;
        }

        /// <summary>返回固定的 0.5 概率样本。</summary>
        public float NextUnit()
        {
            return 0.5f;
        }
    }

    /// <summary>
    /// 为目标选择器测试提供固定单位集合和空牌区集合。
    /// </summary>
    private sealed class RecordingTargetQueryService : IContentTargetQueryService
    {
        private readonly IReadOnlyList<Unit> units;

        /// <summary>保存测试场景中的确定性单位顺序。</summary>
        public RecordingTargetQueryService(params Unit[] values)
        {
            units = values;
        }

        /// <summary>返回测试单位快照。</summary>
        public IReadOnlyList<Unit> GetUnits()
        {
            return units;
        }

        /// <summary>测试不访问牌区，因此对任意 key 返回空集合。</summary>
        public IReadOnlyList<CardInstance> GetCards(string zoneKey)
        {
            return System.Array.Empty<CardInstance>();
        }
    }

}
#endif
