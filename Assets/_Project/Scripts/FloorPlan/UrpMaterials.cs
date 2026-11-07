using UnityEngine;
using UnityEngine.Rendering;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// URP 단색 재질을 코드로 만든다. FloorPlanBuilder 가 재질 칸이 빈 곳을 메울 때와
    /// 에디터의 재질 생성 메뉴(Tools > 방꾸 > 재질 생성·연결)가 같은 설정을 쓰도록 한 곳에 둔다.
    /// </summary>
    public static class UrpMaterials
    {
        public const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        /// <summary>
        /// URP Lit 단색 재질. transparent 면 Inspector 에서 Surface Type 을 Transparent(Alpha) 로 바꾼 것과 같다.
        /// 셰이더를 찾지 못하면 경고하고 null.
        /// </summary>
        public static Material CreateLit(string materialName, Color color, float smoothness = 0.5f, bool transparent = false, float metallic = 0f)
        {
            var material = Create(LitShaderName, materialName, color);
            if (material == null) return null;

            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (transparent) MakeTransparent(material);
            return material;
        }

        /// <summary>
        /// URP Unlit 단색 재질. 조명을 받지 않아 윤곽선·상태 색 표시에 쓴다.
        /// transparent 면 색의 알파만큼 비치는 반투명 (겹침 상자용).
        /// </summary>
        public static Material CreateUnlit(string materialName, Color color, bool transparent = false)
        {
            var material = Create(UnlitShaderName, materialName, color);
            if (material != null && transparent) MakeTransparent(material);
            return material;
        }

        /// <summary>Inspector 에서 Surface Type 을 Transparent(Alpha) 로 바꾼 것과 같다. Lit·Unlit 공통</summary>
        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f); // Alpha
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        static Material Create(string shaderName, string materialName, Color color)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[방꾸] {shaderName} 셰이더를 찾지 못했습니다. {materialName} 재질을 Inspector 에서 지정하세요.");
                return null;
            }

            var material = new Material(shader) { name = materialName };
            material.SetColor("_BaseColor", color);
            return material;
        }
    }
}
