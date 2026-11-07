using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>고정설비 모델의 재질 순서 (서브메쉬 번호)</summary>
    public enum FixturePart
    {
        /// <summary>몸통·문짝 (Fixture 재질)</summary>
        Body,

        /// <summary>도기·상판 (흰색 광택)</summary>
        Porcelain,

        /// <summary>손잡이·수전·개수대 (금속)</summary>
        Metal,

        /// <summary>걸레받이 자리·쿡탑·냉장고 손잡이 (어두운 색)</summary>
        Dark,
    }

    /// <summary>
    /// 고정설비 type 별 모양 (JSON 형식 그대로, size 상자 안에 맞춤). 보이기만 하는 모양이고 판정은 지금처럼 size 상자(BoxCollider)로 한다.
    /// 좌표: 원점 = 바닥면 가운데, 정면 = +Z (가구와 같은 규칙), x = 폭 W, y = 높이 H, z = 깊이 D (m).
    /// 수전처럼 위로 솟는 작은 부분만 상자 높이를 조금 넘는다.
    /// 모르는 type 은 null (지금처럼 상자로 그린다).
    /// </summary>
    public static class FixtureModels
    {
        public const int PartCount = 4;

        const float Gap = 0.003f; // 문짝 사이 틈
        const float DoorT = 0.018f; // 문짝 두께

        public static ShapeMesh Build(string type, Vector3 size)
        {
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return null;
            var shape = new ShapeMesh(PartCount);
            switch (type)
            {
                case "counter":
                    Counter(shape, size);
                    return shape;
                case "cabinet":
                    Cabinet(shape, size, 0.45f, size.y > 1.6f);
                    return shape;
                case "wardrobe":
                    Cabinet(shape, size, 0.5f, false);
                    return shape;
                case "fridge":
                    Fridge(shape, size);
                    return shape;
                case "toilet":
                    Toilet(shape, size);
                    return shape;
                case "washbasin":
                    Washbasin(shape, size);
                    return shape;
                default:
                    return null;
            }
        }

        // ───────────── 싱크대 ─────────────

        /// <summary>
        /// 하부장 + 상판(개수대 구멍) + 개수대 + 수전 + 쿡탑. 문짝은 약 60cm 마다, 앞에 걸레받이 자리(안으로 들어감)
        /// </summary>
        static void Counter(ShapeMesh s, Vector3 size)
        {
            float w = size.x, h = size.y, d = size.z;
            float hw = w * 0.5f, hd = d * 0.5f;
            float toe = Mathf.Min(0.10f, h * 0.15f);
            float top = 0.04f;
            float zDoor = hd - 0.03f; // 문짝 앞면 (상판이 3cm 앞으로 나옴)
            float basinDepth = Mathf.Min(0.2f, (h - top - toe) * 0.5f);
            int body = (int)FixturePart.Body;

            // 걸레받이 자리(어두움, 안쪽으로), 양 옆판, 안쪽 몸통 (개수대가 들어갈 만큼 낮게, 앞은 문짝·위는 상판이 가림)
            s.Box((int)FixturePart.Dark, -hw + DoorT, hw - DoorT, 0f, toe, -hd, zDoor - 0.06f);
            s.Box(body, -hw, -hw + DoorT, 0f, h - top, -hd, zDoor);
            s.Box(body, hw - DoorT, hw, 0f, h - top, -hd, zDoor);
            s.Box(body, -hw + DoorT, hw - DoorT, toe, h - top - basinDepth - 0.02f, -hd, zDoor - DoorT);

            // 문짝 + 손잡이
            int doors = Mathf.Max(1, Mathf.RoundToInt((w - 2 * DoorT) / 0.6f));
            float doorW = (w - 2 * DoorT) / doors;
            for (int i = 0; i < doors; i++)
            {
                float x0 = -hw + DoorT + i * doorW + Gap * 0.5f, x1 = x0 + doorW - Gap;
                s.Box(body, x0, x1, toe + Gap, h - top - Gap, zDoor - DoorT, zDoor);
                float cx = (x0 + x1) * 0.5f;
                s.Box((int)FixturePart.Metal, cx - 0.07f, cx + 0.07f, h - top - 0.07f, h - top - 0.058f, zDoor, zDoor + 0.02f);
            }

            // 개수대 (왼쪽)와 쿡탑 (오른쪽). 폭이 좁아도 겹치지 않게 크기를 줄인다
            float sinkW = Mathf.Min(0.55f, w * 0.42f), sinkD = Mathf.Min(0.42f, d - 0.2f);
            float sinkX = -hw + Mathf.Max(0.06f + sinkW * 0.5f, w * 0.3f);
            float sx0 = sinkX - sinkW * 0.5f, sx1 = sinkX + sinkW * 0.5f;
            float sz0 = -0.01f - sinkD * 0.5f, sz1 = -0.01f + sinkD * 0.5f;

            // 상판: 개수대 구멍 둘레 네 조각
            int porcelain = (int)FixturePart.Porcelain;
            s.Box(porcelain, -hw, sx0, h - top, h, -hd, hd);
            s.Box(porcelain, sx1, hw, h - top, h, -hd, hd);
            s.Box(porcelain, sx0, sx1, h - top, h, -hd, sz0);
            s.Box(porcelain, sx0, sx1, h - top, h, sz1, hd);
            s.Basin((int)FixturePart.Metal, sx0, sx1, sz0, sz1, h, basinDepth);

            // 수전: 개수대 뒤 기둥 + 앞으로 나온 토수구
            float fz = sz0 - 0.035f;
            s.EllipticCylinder((int)FixturePart.Metal, sinkX, fz, 0.02f, 0.02f, h, h + 0.25f, true, false);
            s.Box((int)FixturePart.Metal, sinkX - 0.012f, sinkX + 0.012f, h + 0.22f, h + 0.25f, fz, fz + 0.2f);

            // 쿡탑: 검은 판 + 화구 두 개
            float cookW = Mathf.Min(0.58f, w * 0.38f), cookD = Mathf.Min(0.5f, d - 0.12f);
            float cookX = hw - Mathf.Max(0.06f + cookW * 0.5f, w * 0.25f);
            s.Box((int)FixturePart.Dark, cookX - cookW * 0.5f, cookX + cookW * 0.5f, h, h + 0.006f, -cookD * 0.5f, cookD * 0.5f);
            float r = Mathf.Min(0.09f, cookW * 0.2f);
            foreach (float bx in new[] { cookX - cookW * 0.25f, cookX + cookW * 0.25f })
                s.EllipticCylinder((int)FixturePart.Metal, bx, 0f, r, r, h + 0.006f, h + 0.008f, true, false);
        }

        // ───────────── 신발장·붙박이장 ─────────────

        /// <summary>바닥까지 닿는 장. 문짝은 doorWidth(m) 마다, twoRows 면 아래·위 두 단. 손잡이는 문짝 안쪽 가장자리 세로 막대</summary>
        static void Cabinet(ShapeMesh s, Vector3 size, float doorWidth, bool twoRows)
        {
            float w = size.x, h = size.y, d = size.z;
            float hw = w * 0.5f, hd = d * 0.5f;
            float toe = Mathf.Min(0.06f, h * 0.05f);
            int body = (int)FixturePart.Body;

            // 손잡이까지 상자 안에 들도록 문짝 앞면을 손잡이 두께만큼 안쪽에
            const float handleT = 0.022f;
            float zDoor = hd - handleT;
            s.Box((int)FixturePart.Dark, -hw, hw, 0f, toe, -hd, zDoor - 0.05f);
            s.Box(body, -hw, hw, toe, h, -hd, zDoor - DoorT);

            int columns = Mathf.Max(1, Mathf.RoundToInt(w / doorWidth));
            float colW = w / columns;
            float split = twoRows ? Mathf.Round(h * 0.45f * 100f) / 100f : h;
            var rows = twoRows ? new[] { (toe, split), (split, h) } : new[] { (toe, h) };
            for (int c = 0; c < columns; c++)
            {
                float x0 = -hw + c * colW + Gap * 0.5f, x1 = x0 + colW - Gap;
                // 손잡이는 짝을 이루는 문짝 사이 쪽 (짝수 칸은 오른쪽, 홀수 칸은 왼쪽 가장자리)
                float handleX = c % 2 == 0 ? x1 - 0.04f : x0 + 0.04f;
                if (columns == 1) handleX = x1 - 0.04f;
                foreach (var (y0, y1) in rows)
                {
                    s.Box(body, x0, x1, y0 + Gap * 0.5f, y1 - Gap * 0.5f, zDoor - DoorT, zDoor);
                    float hy = y1 - y0 > 1.2f ? y0 + 1.0f - toe : (y0 + y1) * 0.5f; // 긴 문은 허리 높이
                    float len = Mathf.Min(0.3f, (y1 - y0) * 0.4f);
                    s.Box((int)FixturePart.Metal, handleX - 0.006f, handleX + 0.006f, hy - len * 0.5f, hy + len * 0.5f, zDoor, hd);
                }
            }
        }

        // ───────────── 냉장고 ─────────────

        /// <summary>흰 몸통, 위 냉동실(약 1/3)·아래 냉장실 문, 세로 손잡이</summary>
        static void Fridge(ShapeMesh s, Vector3 size)
        {
            float w = size.x, h = size.y, d = size.z;
            float hw = w * 0.5f, hd = d * 0.5f;
            const float feet = 0.03f, doorT = 0.04f;
            int porcelain = (int)FixturePart.Porcelain, dark = (int)FixturePart.Dark;

            s.Box(dark, -hw + 0.02f, hw - 0.02f, 0f, feet, -hd, hd - doorT - 0.02f);
            s.Box(porcelain, -hw, hw, feet, h, -hd, hd - doorT);

            float split = feet + (h - feet) * 0.66f;
            foreach (var (y0, y1) in new[] { (feet, split), (split, h) })
            {
                s.Box(porcelain, -hw + Gap, hw - Gap, y0 + Gap, y1 - Gap, hd - doorT, hd - 0.012f);
                float len = Mathf.Min(0.35f, (y1 - y0) * 0.5f);
                float hy = y0 < split ? y1 - 0.05f - len * 0.5f : y0 + 0.05f + len * 0.5f; // 두 문 사이 쪽
                s.Box(dark, hw - 0.06f, hw - 0.035f, hy - len * 0.5f, hy + len * 0.5f, hd - 0.012f, hd);
            }
        }

        // ───────────── 변기 ─────────────

        /// <summary>뒤쪽 물탱크(뚜껑·버튼), 받침 기둥, 움푹한 변기. 상자 W·D·H 에 맞춰 비율로 줄인다 (기준 400 × 700 × 750mm)</summary>
        static void Toilet(ShapeMesh s, Vector3 size)
        {
            float sx = size.x / 0.40f, sz = size.z / 0.70f, sy = size.y / 0.75f;
            float hd = size.z * 0.5f;
            int porcelain = (int)FixturePart.Porcelain;

            // 물탱크 + 뚜껑 + 버튼
            float tankBack = -hd, tankFront = -hd + 0.18f * sz;
            s.Box(porcelain, -0.19f * sx, 0.19f * sx, 0.40f * sy, 0.73f * sy, tankBack, tankFront);
            s.Box(porcelain, -0.2f * sx, 0.2f * sx, 0.73f * sy, size.y, tankBack, tankFront + 0.005f);
            s.EllipticCylinder((int)FixturePart.Metal, 0f, (tankBack + tankFront) * 0.5f, 0.025f, 0.025f, size.y, size.y + 0.004f, true, false);

            // 받침 기둥과 변기 (안쪽 깊이는 기둥 윗면보다 얕게)
            float bowlZ = 0.05f * sz;
            s.EllipticCylinder(porcelain, 0f, -0.02f * sz, 0.11f * sx, 0.17f * sz, 0f, 0.30f * sy, false, false);
            // 물탱크 아래를 바닥까지 받치는 뒤쪽 몸통 (탱크가 떠 보이지 않게)
            s.Box(porcelain, -0.15f * sx, 0.15f * sx, 0f, 0.40f * sy, -hd + 0.02f * sz, -0.14f * sz);
            s.Cup(porcelain, 0f, bowlZ, 0.18f * sx, 0.27f * sz, 0.13f * sx, 0.20f * sz, 0.30f * sy, 0.42f * sy, 0.10f * sy);
        }

        // ───────────── 세면대 ─────────────

        /// <summary>받침 기둥 + 움푹한 세면기 + 수전 (기준 540 × 450 × 850mm)</summary>
        static void Washbasin(ShapeMesh s, Vector3 size)
        {
            float sx = size.x / 0.54f, sz = size.z / 0.45f;
            float h = size.y, rim = 0.15f * (size.y / 0.85f);
            int porcelain = (int)FixturePart.Porcelain;

            float rx = size.x * 0.5f, rz = size.z * 0.5f - 0.005f;
            s.EllipticCylinder(porcelain, 0f, -0.10f * sz, 0.09f * sx, 0.08f * sz, 0f, h - rim, false, false);
            s.Cup(porcelain, 0f, 0f, rx, rz, rx - 0.06f * sx, rz - 0.07f * sz, h - rim, h, rim * 0.8f);

            // 수전: 뒤 테두리 위 기둥 + 앞으로 나온 토수구
            float fz = -rz + 0.035f * sz;
            s.EllipticCylinder((int)FixturePart.Metal, 0f, fz, 0.018f, 0.018f, h, h + 0.16f, true, false);
            s.Box((int)FixturePart.Metal, -0.011f, 0.011f, h + 0.13f, h + 0.16f, fz, fz + 0.10f * sz);
        }
    }
}
