#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BakeryLightConverter
{
    private const string MENU_ROOT = "Tools/BakeryLightConverter/";
    private const BindingFlags FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static Texture2D spotCookie;
    static Material  defaultAreaMat;

    [MenuItem(MENU_ROOT + "Convert Selected Lights  %#&b")]
    private static void ConvertSelected() =>
        ConvertLights(Selection.gameObjects);

    [MenuItem(MENU_ROOT + "Convert All Lights")]
    private static void ConvertAll() =>
        ConvertLights(Object.FindObjectsOfType<Light>().Select(l => l.gameObject).ToArray());

    private static void ConvertLights(GameObject[] gos)
    {
        if (gos == null || gos.Length == 0)
        {
            EditorUtility.DisplayDialog("Bakery Light Converter", "変換対象の Light が見つかりません。", "OK");
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
                {
                    var bPoint = AddIfMissing<BakeryPointLight>(go, ref converted);
                    CopyCommon(uLight, bPoint);
                    InvokeIfExists(bPoint, "MatchLightmappedToRealtime");

                    if (uLight.type == LightType.Spot)
                    {
                        bPoint.angle = uLight.spotAngle;

                        Texture2D cookie2D = uLight.cookie as Texture2D;
                        if (cookie2D == null)
                        {
                            if (spotCookie == null)
                                spotCookie = AssetDatabase.LoadAssetAtPath<Texture2D>(
                                    $"{ftLightmaps.GetRuntimePath()}ftUnitySpotTexture.bmp");
                            cookie2D = spotCookie;
                        }
                        bPoint.cookie   = cookie2D;
                        bPoint.projMode = BakeryPointLight.ftLightProjectionMode.Cookie;
                    }
                    else
                    {
                        bPoint.cookie   = uLight.cookie as Texture2D;
                        bPoint.projMode = bPoint.cookie
                            ? BakeryPointLight.ftLightProjectionMode.Cubemap
                            : BakeryPointLight.ftLightProjectionMode.Omni;
                    }
                    break;
                }

                case LightType.Directional:
                {
                    var bDir = AddIfMissing<BakeryDirectLight>(go, ref converted);
                    CopyCommon(uLight, bDir);
                    InvokeIfExists(bDir, "MatchLightmappedToRealtime");
                    break;
                }

                case LightType.Area:
                {
                    // 同一GameObjectにBakeryLightMeshを付与しUnity Area Lightを参照して同期
                    var bMesh = AddIfMissing<BakeryLightMesh>(go, ref converted);

                    // 共通プロパティ（色温度対応など）
                    CopyCommon(uLight, bMesh);

                    // Unity Area Light -> Bakery Light Mesh へ諸元コピー（存在すれば）
                    InvokeIfExists(bMesh, "MatchLightmappedToAreaLight");

                    // Quad形状とデフォルトマテリアルを保証（XY平面・+Z法線）
                    EnsureQuadAndMat(go);

                    // サイズをXYに反映（XZではなくXY）
                    var sz = uLight.areaSize; // x=width, y=height
                    go.transform.localScale = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), 1f);
					
					// Bakery Light Mesh 固有プロパティ
					SetFieldIfExists(bMesh.GetType(), bMesh, "lmid", -1); // 未使用
					SetFieldIfExists(bMesh.GetType(), bMesh, "bitmask", 0);
					SetFieldIfExists(bMesh.GetType(), bMesh, "bakeToIndirect", true);
					SetFieldIfExists(bMesh.GetType(), bMesh, "shadowmask", false);
					SetFieldIfExists(bMesh.GetType(), bMesh, "shadowmaskFalloff", false);
					SetFieldIfExists(bMesh.GetType(), bMesh, "maskChannel", 0);
					SetFieldIfExists(bMesh.GetType(), bMesh, "indirectIntensity", uLight.bounceIntensity);
					SetFieldIfExists(bMesh.GetType(), bMesh, "selfShadow", false);

                    // 既定値（Manual準拠）
						var t2 = bMesh.GetType();
                    SetFieldIfExists(t2, bMesh, "cutoff", 100f);
                    SetFieldIfExists(t2, bMesh, "samplesNear", 16);
                    SetFieldIfExists(t2, bMesh, "samplesFar", 256);
                    SetFieldIfExists(t2, bMesh, "texture", null);
                    break;
                }
            }

            // 必要なら元ライトをベイク専用に
            // uLight.lightmapBakeType = LightmapBakeType.Baked;
            // uLight.enabled = false;
        }

        Debug.Log($"[Bakery] Converted {converted} light(s).（Undo で戻せます）");
    }

    // ──────────────────────────────────────────────────────────────
    // Quad形状とデフォルトAreaLightマテリアルを同一GameObjectに保証
    private static void EnsureQuadAndMat(GameObject go)
    {
        var mf = go.GetComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();
        if (mf == null) mf = Undo.AddComponent<MeshFilter>(go);
        if (mr == null) mr = Undo.AddComponent<MeshRenderer>(go);

        if (mf.sharedMesh == null || mf.sharedMesh.name != "Quad")
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            mf.sharedMesh = mesh; // XY平面 (+Z法線)
        }

        if (defaultAreaMat == null)
            defaultAreaMat = AssetDatabase.LoadAssetAtPath<Material>(
                $"{ftLightmaps.GetRuntimePath()}ftDefaultAreaLightMat.mat");
        if (mr.sharedMaterial == null && defaultAreaMat != null)
            mr.sharedMaterial = defaultAreaMat;
    }

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

    private static void CopyCommon(Light src, Component dst)
    {
        var t = dst.GetType();

        Color c = src.useColorTemperature
            ? src.color * Mathf.CorrelatedColorTemperatureToRGB(src.colorTemperature)
            : src.color;
        SetFieldIfExists(t, dst, "color", c);
        SetFieldIfExists(t, dst, "intensity", GetBakeryIntensity(src));

        // Point/Spot/Dir向けのみ（Areaには当てない）
        if (!(dst is BakeryLightMesh))
        {
            SetFieldIfExists(t, dst, "cutoff", src.range);
            SetFieldIfExists(t, dst, "angle", src.spotAngle);
            SetFieldIfExists(t, dst, "range", src.range);
            SetFieldIfExists(t, dst, "spotAngle", src.spotAngle);
            SetFieldIfExists(t, dst, "cookie", src.cookie);
            SetFieldIfExists(t, dst, "indirectIntensity", src.bounceIntensity);
            SetFieldIfExists(t, dst, "shadowSpread", 0f);
            SetFieldIfExists(t, dst, "shadowSamples", src.shadowCustomResolution > 0 ? 64 : 16);
            SetFieldIfExists(t, dst, "cookieSize", src.cookieSize);

            if (src.type == LightType.Spot)
            {
                SetFieldIfExists(t, dst, "innerAngle", src.innerSpotAngle);
                var shapeRadiusProp = typeof(Light).GetProperty("shapeRadius", BindingFlags.Public | BindingFlags.Instance);
                if (shapeRadiusProp != null)
                    SetFieldIfExists(t, dst, "sphereRadius", (float)shapeRadiusProp.GetValue(src));
            }

            if (src.type == LightType.Directional)
            {
                var diameterProp = typeof(Light).GetProperty("sunAngularDiameter",
                    BindingFlags.Public | BindingFlags.Instance);
                if (diameterProp != null)
                    SetFieldIfExists(t, dst, "angle", (float)diameterProp.GetValue(src));
            }
        }
    }

    private static void SetFieldIfExists(System.Type t, object obj, string name, object val)
    {
        var f = t.GetField(name, FLAGS);
        if (f != null)
        {
            if (val == null || f.FieldType.IsAssignableFrom(val.GetType()))
                f.SetValue(obj, val);
        }
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
        if (m != null) m.Invoke(target, null);
        return m != null;
    }
}
#endif
