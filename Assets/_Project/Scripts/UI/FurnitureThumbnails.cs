using System.Collections.Generic;
using UnityEngine;

namespace Bangkku.UI
{
    /// <summary>
    /// 가구 사진 (손목 메뉴 선택 탭). Resources/FurnitureThumbnails/&lt;가구 id&gt;.png 를 읽는다.
    /// 사진은 에디터 메뉴 Tools > 방꾸 > 가구 사진 만들기 가 카탈로그 3DGS 가구를 찍어 만든다.
    /// 3DGS 가 찍히지 않으면 Game 뷰 캡처를 같은 이름(정사각, 256px 권장)으로 넣어도 된다.
    /// 사진이 없으면 null (메뉴가 가구 색 상자로 대신 보여 준다). 한 번 찾은 결과는 기억한다.
    /// </summary>
    public static class FurnitureThumbnails
    {
        /// <summary>Resources 아래 폴더 이름 (에디터 메뉴도 같은 값을 쓴다)</summary>
        public const string ResourceFolder = "FurnitureThumbnails";

        /// <summary>사진 한 변 픽셀</summary>
        public const int Size = 256;

        static readonly Dictionary<string, Texture2D> Cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Cache.Clear();

        /// <summary>가구 id 의 사진. 없으면 null</summary>
        public static Texture2D Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (!Cache.TryGetValue(itemId, out var texture))
            {
                texture = Resources.Load<Texture2D>($"{ResourceFolder}/{itemId}");
                Cache[itemId] = texture;
            }

            return texture;
        }
    }
}
