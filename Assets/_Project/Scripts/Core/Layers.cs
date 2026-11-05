using UnityEngine;

namespace Bangkku.Core
{
    /// <summary>
    /// 물리 Layer 번호와 마스크 (03 문서 5.5, 전체개발계획 5장).
    /// 코드에서 Layer 는 이 상수로만 참조한다. LayerMask.NameToLayer("Wall") 같은 문자열을 직접 쓰지 않는다.
    /// Layer 이름은 상수 이름과 같다 (nameof(Layers.Wall) == "Wall").
    /// </summary>
    public static class Layers
    {
        public const int Wall = 6;
        public const int Floor = 7;
        public const int Furniture = 8;
        public const int Fixture = 9;

        public const int WallMask = 1 << Wall;
        public const int FloorMask = 1 << Floor;
        public const int FurnitureMask = 1 << Furniture;
        public const int FixtureMask = 1 << Fixture;

        /// <summary>가구 겹침 판정 대상: 벽, 가구, 고정설비</summary>
        public const int PlacementObstacleMask = WallMask | FurnitureMask | FixtureMask;

#if UNITY_EDITOR
        /// <summary>
        /// 에디터를 열 때 Tags and Layers 등록 상태가 이 상수와 같은지 확인한다.
        /// 다르면 Tools > 방꾸 > 프로젝트 설정 적용 을 실행하라고 알린다.
        /// </summary>
        [UnityEditor.InitializeOnLoadMethod]
        static void ValidateProjectLayers()
        {
            Check(Wall, nameof(Wall));
            Check(Floor, nameof(Floor));
            Check(Furniture, nameof(Furniture));
            Check(Fixture, nameof(Fixture));
        }

        static void Check(int index, string expected)
        {
            string actual = LayerMask.LayerToName(index);
            if (actual != expected)
            {
                Debug.LogWarning($"[방꾸] Layer {index} 가 '{expected}' 가 아니라 '{actual}' 입니다. " +
                                 "Tools > 방꾸 > 프로젝트 설정 적용 을 실행하세요.");
            }
        }
#endif
    }
}
