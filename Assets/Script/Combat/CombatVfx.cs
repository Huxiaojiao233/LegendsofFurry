using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 运行时生成的战斗特效：爪击斩击、火球弹道与爆炸、受击数字。
/// 不依赖 Prefab，切场景后自动清理。
/// </summary>
public sealed class CombatVfx : MonoBehaviour
{
    private static CombatVfx instance;

    public static CombatVfx Instance
    {
        get
        {
            if (instance != null)
            {
                return instance;
            }

            GameObject host = new GameObject("CombatVfx");
            instance = host.AddComponent<CombatVfx>();
            return instance;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static void PlayClaw(Unit attacker, Unit target)
    {
        if (target == null)
        {
            return;
        }

        Instance.StartCoroutine(Instance.ClawRoutine(attacker, target));
    }

    public static void PlayFireball(Unit caster, Unit target, Action onImpact)
    {
        if (caster == null || target == null)
        {
            onImpact?.Invoke();
            return;
        }

        Instance.StartCoroutine(Instance.FireballRoutine(caster, target, onImpact));
    }

    public static void PlayDamageNumber(Vector3 worldPosition, int amount, Color color)
    {
        if (amount <= 0)
        {
            return;
        }

        Instance.StartCoroutine(Instance.DamageNumberRoutine(worldPosition, amount, color));
    }

    private IEnumerator ClawRoutine(Unit attacker, Unit target)
    {
        Vector3 targetPosition = ImpactPoint(target);
        Vector3 from = attacker == null ? targetPosition + Vector3.left : attacker.transform.position;
        Vector3 direction = (targetPosition - from);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.right;
        }

        direction.Normalize();
        Color slashColor = new Color(1f, 0.92f, 0.85f, 1f);

        GameObject slashA = CreateSlash(targetPosition, direction, 35f, slashColor);
        GameObject slashB = CreateSlash(targetPosition, direction, -40f, new Color(1f, 0.45f, 0.35f, 1f));

        const float duration = 0.22f;
        float elapsed = 0f;
        while (elapsed < duration && target != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float fade = 1f - t;
            float travel = Mathf.Lerp(-0.35f, 0.4f, t);
            UpdateSlash(slashA, targetPosition + direction * travel, fade);
            UpdateSlash(slashB, targetPosition + direction * (travel * 0.85f), fade);
            yield return null;
        }

        DestroyVfx(slashA);
        DestroyVfx(slashB);
    }

    private IEnumerator FireballRoutine(Unit caster, Unit target, Action onImpact)
    {
        Vector3 start = ImpactPoint(caster) + Vector3.up * 0.15f;
        Color fire = new Color(1f, 0.45f, 0.12f, 1f);
        GameObject ball = CreateSphere("Fireball", start, 0.16f, fire);
        Light light = ball.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = fire;
        light.range = 2.4f;
        light.intensity = 2.2f;

        const float flyDuration = 0.28f;
        float elapsed = 0f;
        while (elapsed < flyDuration && target != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flyDuration);
            t = t * t * (3f - 2f * t);
            Vector3 end = ImpactPoint(target);
            Vector3 position = Vector3.Lerp(start, end, t);
            position.y += Mathf.Sin(t * Mathf.PI) * 0.35f;
            ball.transform.position = position;
            ball.transform.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.22f, t);
            yield return null;
        }

        Vector3 impact = target == null ? ball.transform.position : ImpactPoint(target);
        DestroyVfx(ball);
        onImpact?.Invoke();
        yield return BurstRoutine(impact, fire);
    }

    private IEnumerator BurstRoutine(Vector3 center, Color color)
    {
        GameObject shockwave = CreateSphere("FireBurst", center, 0.2f, new Color(color.r, color.g, color.b, 0.55f));
        GameObject[] sparks = new GameObject[10];
        Vector3[] velocities = new Vector3[sparks.Length];
        for (int i = 0; i < sparks.Length; i++)
        {
            Vector3 direction = UnityEngine.Random.onUnitSphere;
            direction.y = Mathf.Abs(direction.y);
            sparks[i] = CreateSphere($"Spark_{i}", center, 0.07f, color);
            velocities[i] = direction * UnityEngine.Random.Range(1.6f, 3.2f);
        }

        const float duration = 0.32f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            shockwave.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.4f, t);
            SetColor(shockwave, new Color(color.r, color.g, color.b, 0.55f * (1f - t)));

            for (int i = 0; i < sparks.Length; i++)
            {
                if (sparks[i] == null)
                {
                    continue;
                }

                sparks[i].transform.position += velocities[i] * Time.deltaTime;
                velocities[i] += Vector3.down * 6f * Time.deltaTime;
                sparks[i].transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.02f, t);
                SetColor(sparks[i], new Color(color.r, color.g, color.b, 1f - t));
            }

            yield return null;
        }

        DestroyVfx(shockwave);
        for (int i = 0; i < sparks.Length; i++)
        {
            DestroyVfx(sparks[i]);
        }
    }

    private IEnumerator DamageNumberRoutine(Vector3 worldPosition, int amount, Color color)
    {
        GameObject numberObject = new GameObject("DamageNumber");
        numberObject.transform.position = worldPosition + Vector3.up * 0.45f;

        TextMeshPro text = numberObject.AddComponent<TextMeshPro>();
        text.text = $"-{amount}";
        text.fontSize = 5f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.fontStyle = FontStyles.Bold;
        text.sortingOrder = 32;
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(2f, 1f);

        const float duration = 0.7f;
        Vector3 start = numberObject.transform.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            numberObject.transform.position = start + Vector3.up * (0.7f * t);
            text.color = new Color(color.r, color.g, color.b, 1f - t);
            if (Camera.main != null)
            {
                numberObject.transform.rotation =
                    Quaternion.LookRotation(numberObject.transform.position - Camera.main.transform.position);
            }

            yield return null;
        }

        Destroy(numberObject);
    }

    private static void DestroyVfx(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Destroy(renderer.material);
        }

        Destroy(target);
    }

    private static Vector3 ImpactPoint(Unit unit)
    {
        return unit.transform.position + Vector3.up * 0.35f;
    }

    private static GameObject CreateSlash(
        Vector3 position,
        Vector3 forward,
        float tilt,
        Color color)
    {
        GameObject slash = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slash.name = "ClawSlash";
        Collider collider = slash.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        slash.transform.position = position;
        slash.transform.rotation = Quaternion.LookRotation(forward, Vector3.up) *
            Quaternion.Euler(0f, 0f, tilt);
        slash.transform.localScale = new Vector3(0.9f, 0.045f, 0.08f);
        ApplyUnlit(slash, color);
        return slash;
    }

    private static void UpdateSlash(GameObject slash, Vector3 position, float fade)
    {
        if (slash == null)
        {
            return;
        }

        slash.transform.position = position;
        slash.transform.localScale = new Vector3(0.55f + 0.5f * fade, 0.045f, 0.08f);
        Renderer renderer = slash.GetComponent<Renderer>();
        if (renderer != null)
        {
            Color color = renderer.material.color;
            color.a = fade;
            renderer.material.color = color;
            if (renderer.material.HasProperty("_BaseColor"))
            {
                renderer.material.SetColor("_BaseColor", color);
            }
        }
    }

    private static GameObject CreateSphere(string name, Vector3 position, float scale, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        Collider collider = sphere.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * scale;
        ApplyUnlit(sphere, color);
        return sphere;
    }

    private static void ApplyUnlit(GameObject target, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Renderer renderer = target.GetComponent<Renderer>();
        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        material.color = color;
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void SetColor(GameObject target, Color color)
    {
        if (target == null)
        {
            return;
        }

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        renderer.material.color = color;
        if (renderer.material.HasProperty("_BaseColor"))
        {
            renderer.material.SetColor("_BaseColor", color);
        }
    }
}
