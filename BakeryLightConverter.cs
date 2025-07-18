#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity Light → Bakery Light 一括変換ツール（Undo 対応）
///   • Tools ▸ Bakery ▸ Convert Selected Lights  … 選択中ライトを変換
///   • Tools ▸ Bakery ▸ Convert All Lights       … シーン内ライトを全部変換
/// </summary>
public static class BakeryLightConverter
{
    private const string MENU_ROOT = "Tools/Bakery/";
    private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    
    static Texture2D spotCookie;   // デフォルト Spot Cookie 用

    // ──────────────────────────────────────────────────────────────────────────────
    // メニュー
    [MenuItem(MENU_ROOT + "Convert Selected Lights  %#&b")]           // Alt+Shift+B
    private static void ConvertSelected() =>
        ConvertLights(Selection.gameObjects);

    [MenuItem(MENU_ROOT + "Convert All Lights")]
    private static void ConvertAll() =>
        ConvertLights(Object.FindObjectsOfType<Light>()
                           .Select(l => l.gameObject).ToArray());

    // ──────────────────────────────────────────────────────────────────────────────
    // コア処理
    private static void ConvertLights(GameObject[] gos)
    {
        if (gos == null || gos.Length == 0)
        {
            EditorUtility.DisplayDialog("Bakery Light Converter",
                                        "変換対象の Light が見つかりません。",
                                        "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int converted = 0;

        foreach (var go in gos)
        {
            var uLight = go.GetComponent<Light>();
            if (uLight == null) continue;

            switch (uLight.type)
            {
                case LightType.Point:
                case LightType.Spot:
                    var bPoint = AddIfMissing<BakeryPointLight>(go, ref converted);
                    CopyCommon(uLight, bPoint);
                    InvokeIfExists(bPoint, "MatchLightmappedToRealtime");

                    // ---- ここから新しいコード ---------------------------------
                    if (uLight.type == LightType.Spot)
                    {
                        // ① Spot 用コーン角
                        bPoint.angle = uLight.spotAngle;

                        // ② Cookie と ProjMode
                        Texture2D cookie2D = uLight.cookie as Texture2D;
                        if (cookie2D == null)
                        {
                            // Unity 標準 gobo が無い場合は Bakery 同梱の既定テクスチャを使用
                            if (spotCookie == null)
                                spotCookie = AssetDatabase.LoadAssetAtPath<Texture2D>(
                                              $"{ftLightmaps.GetRuntimePath()}ftUnitySpotTexture.bmp");
                            cookie2D = spotCookie;
                        }
                        bPoint.cookie   = cookie2D;
                        bPoint.projMode = BakeryPointLight.ftLightProjectionMode.Cookie;
                    }
                    else // Point Light
                    {
                        bPoint.cookie   = uLight.cookie as Texture2D;
                        bPoint.projMode = bPoint.cookie
                            ? BakeryPointLight.ftLightProjectionMode.Cubemap
                            : BakeryPointLight.ftLightProjectionMode.Omni;
                    }
                    // ---- ここまで新しいコード ---------------------------------

                    break;

                case LightType.Directional:
                    var bDir = AddIfMissing<BakeryDirectLight>(go, ref converted);
                    CopyCommon(uLight, bDir);
                    InvokeIfExists(bDir, "MatchLightmappedToRealtime");
                    break;

                case LightType.Area:
                    var bMesh = AddIfMissing<BakeryLightMesh>(go, ref converted);
                    CopyCommon(uLight, bMesh);

                    // UI ボタン相当があるか確認して呼ぶ
                    bool matched = InvokeIfExists(bMesh, "MatchLightmappedToAreaLight");

                    if (!matched)
                    {
                        // ボタンが無い Bakery 版の場合は Width/Height → transform scale で再現
                        Vector2 sz = uLight.areaSize;           // (x = width, y = height)
                        bMesh.transform.localScale = new Vector3(sz.x, 1, sz.y);
                    }
                    break;
            }

            // 例: 元の Unity Light をベイク専用にしたい場合
            // uLight.lightmapBakeType = LightmapBakeType.Baked;
            // uLight.enabled = false;
        }

        Debug.Log($"[Bakery] Converted {converted} light(s).（Undo で戻せます）");
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Unity → Bakery intensity 変換 (directional / point / spot)
    private static float GetBakeryIntensity(Light uLight)
    {
        switch (uLight.type)
        {
            case LightType.Directional:
            case LightType.Point:
            case LightType.Spot:
                return uLight.intensity;
            default:
                return uLight.intensity;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // ヘルパー
    private static T AddIfMissing<T>(GameObject go, ref int counter) where T : Component
    {
        var comp = go.GetComponent<T>();
        if (comp == null)
        {
            comp = Undo.AddComponent<T>(go);
            counter++;
        }
        return comp;
    }

    // Unity Light → Bakery Light の主要プロパティコピー（最低限）
    private static void CopyCommon(Light src, Component dst)
    {
        var t = dst.GetType();

        // ① 色（色温度対応）
        Color c = src.useColorTemperature
            ? src.color * Mathf.CorrelatedColorTemperatureToRGB(src.colorTemperature)
            : src.color;
        SetFieldIfExists(t, dst, "color", c);

        // ② 強度（物理単位へ変換）
        SetFieldIfExists(t, dst, "intensity", GetBakeryIntensity(src));

        // ③ 汎用プロパティ
        SetFieldIfExists(t, dst, "cutoff", src.range);        // Range → Bakery の cutoff
        SetFieldIfExists(t, dst, "angle",  src.spotAngle);    // SpotAngle → angle
        SetFieldIfExists(t, dst, "range",        src.range);         // Point / Spot
        SetFieldIfExists(t, dst, "spotAngle",    src.spotAngle);     // Spot
        SetFieldIfExists(t, dst, "cookie",       src.cookie);        // Cookie/IES
        SetFieldIfExists(t, dst, "indirectIntensity", src.bounceIntensity);

        // ④ シャドウ関連（Unity→Bakery に近い既定値に）
        SetFieldIfExists(t, dst, "shadowSpread",  0f);   // blur 半径
        SetFieldIfExists(t, dst, "shadowSamples", src.shadowCustomResolution > 0 ? 64 : 16);

        // --- spot / directional 共通 ------------------------------
        SetFieldIfExists(t, dst, "cookieSize", src.cookieSize);

        // Spot 固有
        if (src.type == LightType.Spot)
        {
            SetFieldIfExists(t, dst, "innerAngle", src.innerSpotAngle);   // soft‑edge
            var shapeRadiusProp = typeof(Light).GetProperty(
                "shapeRadius", BindingFlags.Public | BindingFlags.Instance);
            if (shapeRadiusProp != null)
                SetFieldIfExists(t, dst, "sphereRadius",
                    (float)shapeRadiusProp.GetValue(src));
        }

        // Directional 固有（HDRP 2021+）
        if (src.type == LightType.Directional)
        {
            // sunAngularDiameter は HDRP 拡張。存在チェックしてからコピー
            var diameterProp = typeof(Light).GetProperty("sunAngularDiameter",
                          System.Reflection.BindingFlags.Public |
                          System.Reflection.BindingFlags.Instance);
            if (diameterProp != null)
            {
                float dia = (float)diameterProp.GetValue(src);
                SetFieldIfExists(t, dst, "angle", dia);
            }
        }
    }

    private static void SetFieldIfExists(System.Type t, object obj, string name, object val)
    {
        // フィールド
        var f = t.GetField(name, FLAGS);
        if (f != null)
        {
            if (val == null || f.FieldType.IsAssignableFrom(val.GetType()))
                f.SetValue(obj, val);
        }

        // プロパティ
        var p = t.GetProperty(name, FLAGS);
        if (p != null && p.CanWrite)
        {
            if (val == null || p.PropertyType.IsAssignableFrom(val.GetType()))
                p.SetValue(obj, val);
        }
    }

    private static bool InvokeIfExists(object target, string methodName)
    {
        var m = target.GetType().GetMethod(methodName, FLAGS);
        if (m != null) m.Invoke(target, null); // UI ボタン相当
        return m != null;
    }
}
#endif
