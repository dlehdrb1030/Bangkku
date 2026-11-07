using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// 방 재질용 텍스처를 코드로 그린다 (다운로드 없이, 저작권 걱정 없이). 모든 텍스처는 이음새 없이 반복된다 (가장자리가 반대쪽과 이어짐).
    /// 결과는 정사각형 size × size 의 색(Color32)과 높이(0~1). 높이로 노멀맵을 만든다.
    /// 텍스처 한 장이 실제로 몇 m 인지(Meters)를 재질 Tiling 에 넣는다 (UV 1 = 1m).
    /// 실제 사진 텍스처(ambientCG, Poly Haven 등 CC0)로 바꾸려면 재질의 Base Map·Normal Map 칸에 넣고 Tiling 만 맞추면 된다.
    /// UnityEditor 를 쓰지 않는 순수 계산이라 따로 시험할 수 있다. 파일 저장·재질 연결은 RoomTexturesMenu 가 한다.
    /// </summary>
    public static class ProceduralTextures
    {
        public struct Result
        {
            public int Size;
            public float Meters;
            public Color32[] Albedo;
            public float[] Height;
        }

        // ───────────── 마루 (강마루) ─────────────

        /// <summary>마루 한 장 = 1.2m. 폭 150mm 판 8줄이 X(U) 방향으로 놓이고, 줄마다 이음매 위치가 다르다</summary>
        public const float WoodMeters = 1.2f;
        public const int WoodRows = 8;

        public static Result Wood(int size, int seed = 11)
        {
            var albedo = new Color32[size * size];
            var height = new float[size * size];
            var light = new Color(0.78f, 0.62f, 0.45f);
            var dark = new Color(0.55f, 0.39f, 0.26f);
            float rowHeight = size / (float)WoodRows;
            float groove = Mathf.Max(1.5f, size / 600f); // 이음 홈 폭 (px)

            for (int y = 0; y < size; y++)
            {
                int row = Mathf.Min(WoodRows - 1, (int)(y / rowHeight));
                float inRowY = y - row * rowHeight;
                // 줄마다 이음매 위치와 판 색이 다르다
                float joint = Hash01(row, 0, seed) * size;
                float tone = (Hash01(row, 1, seed) - 0.5f) * 0.18f;

                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // 나뭇결: 판 길이(U) 방향으로 길게 늘어난 무늬 + 나이테처럼 휘는 줄
                    float grain = Fbm(u, v, 3, 2 * WoodRows * 8, 4, seed + 7);
                    float rings = 0.5f + 0.5f * Mathf.Sin((v * WoodRows * 14f + Fbm(u, v, 2, WoodRows * 2, 3, seed + 3) * 6f) * Mathf.PI * 2f);
                    float t = Mathf.Clamp01(0.45f + (grain - 0.5f) * 0.9f + (rings - 0.5f) * 0.25f + tone);
                    var c = Color.Lerp(dark, light, t);

                    // 판 사이 홈 (가로 이음 = 줄 경계, 세로 이음 = 줄마다 한 곳)
                    float edgeY = Mathf.Min(inRowY, rowHeight - inRowY);
                    float dx = Mathf.Abs(x - joint);
                    float edgeX = Mathf.Min(dx, size - dx);
                    float h = Mathf.Clamp01(Mathf.Min(edgeY, edgeX) / groove);
                    c *= Mathf.Lerp(0.55f, 1f, h);

                    int i = y * size + x;
                    albedo[i] = Opaque(c);
                    height[i] = h * 0.8f + grain * 0.2f;
                }
            }

            return new Result { Size = size, Meters = WoodMeters, Albedo = albedo, Height = height };
        }

        // ───────────── 타일 (욕실·현관 바닥) ─────────────

        /// <summary>타일 한 장 = 0.6m = 300mm 타일 2×2, 줄눈 3mm</summary>
        public const float TileMeters = 0.6f;
        public const int TileCount = 2;
        public const float GroutMeters = 0.003f;

        public static Result Tile(int size, int seed = 23)
        {
            var albedo = new Color32[size * size];
            var height = new float[size * size];
            var tileColor = new Color(0.86f, 0.86f, 0.84f);
            var groutColor = new Color(0.62f, 0.62f, 0.60f);
            float cell = size / (float)TileCount;
            float grout = Mathf.Max(1f, GroutMeters / TileMeters * size); // 줄눈 폭 (px), 경계 양쪽 합

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                int tx = (int)(x / cell), ty = (int)(y / cell);
                float fx = x - tx * cell, fy = y - ty * cell;
                float edge = Mathf.Min(Mathf.Min(fx, cell - fx), Mathf.Min(fy, cell - fy));
                float h = Mathf.Clamp01((edge - grout * 0.5f) / 1.5f + 0.5f); // 0 = 줄눈, 1 = 타일 면

                // 타일마다 아주 조금 다른 색 + 잔무늬
                float tone = (Hash01(tx, ty, seed) - 0.5f) * 0.05f;
                float speck = (Fbm(u, v, 16, 16, 3, seed + 5) - 0.5f) * 0.06f;
                var c = Color.Lerp(groutColor, tileColor * (1f + tone + speck), h);

                int i = y * size + x;
                albedo[i] = Opaque(c);
                height[i] = h;
            }

            return new Result { Size = size, Meters = TileMeters, Albedo = albedo, Height = height };
        }

        // ───────────── 벽지 (실크 벽지) ─────────────

        /// <summary>벽지 한 장 = 0.5m. 미색 바탕에 세로로 아주 약한 결</summary>
        public const float WallMeters = 0.5f;

        public static Result Wallpaper(int size, int seed = 37)
        {
            var albedo = new Color32[size * size];
            var height = new float[size * size];
            var baseColor = new Color(0.93f, 0.92f, 0.89f);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float fine = Fbm(u, v, 64, 64, 3, seed);
                float streak = Fbm(u, v, 96, 6, 2, seed + 1); // 세로(V)로 긴 결
                float n = fine * 0.6f + streak * 0.4f;
                var c = baseColor * (1f + (n - 0.5f) * 0.06f);

                int i = y * size + x;
                albedo[i] = Opaque(c);
                height[i] = n;
            }

            return new Result { Size = size, Meters = WallMeters, Albedo = albedo, Height = height };
        }

        // ───────────── 천장 (석고보드 도장) ─────────────

        /// <summary>천장 한 장 = 1m. 흰 바탕에 아주 옅은 얼룩</summary>
        public const float CeilingMeters = 1f;

        public static Result Ceiling(int size, int seed = 41)
        {
            var albedo = new Color32[size * size];
            var height = new float[size * size];
            var baseColor = new Color(0.96f, 0.96f, 0.95f);

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float n = Fbm(u, v, 24, 24, 4, seed);
                int i = y * size + x;
                albedo[i] = Opaque(baseColor * (1f + (n - 0.5f) * 0.03f));
                height[i] = n;
            }

            return new Result { Size = size, Meters = CeilingMeters, Albedo = albedo, Height = height };
        }

        // ───────────── 노멀맵 ─────────────

        /// <summary>
        /// 높이(0~1)로 접선 공간 노멀맵 색을 만든다. 가장자리는 반대쪽과 이어서 계산한다 (반복해도 이음새 없음).
        /// strength 가 클수록 요철이 깊다
        /// </summary>
        public static Color32[] NormalFromHeight(float[] height, int size, float strength)
        {
            var result = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = height[y * size + Wrap(x - 1, size)], r = height[y * size + Wrap(x + 1, size)];
                float d = height[Wrap(y - 1, size) * size + x], u = height[Wrap(y + 1, size) * size + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                result[y * size + x] = new Color32(ToByte(n.x * 0.5f + 0.5f), ToByte(n.y * 0.5f + 0.5f), ToByte(n.z * 0.5f + 0.5f), 255);
            }

            return result;
        }

        // ───────────── 이음새 없는 노이즈 ─────────────

        /// <summary>
        /// 반복되는 값 노이즈 여러 겹 (0~1). periodX·periodY 는 텍스처 한 장에 들어가는 격자 수 (정수라야 이음새가 없다).
        /// 겹마다 주기를 두 배로 한다
        /// </summary>
        public static float Fbm(float u, float v, int periodX, int periodY, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int px = periodX << o, py = periodY << o;
                sum += ValueNoise(u * px, v * py, px, py, seed + o * 101) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
            }

            return sum / total;
        }

        /// <summary>격자점 난수를 부드럽게 이은 노이즈 (0~1). 격자 좌표를 period 로 감아서 반복된다</summary>
        public static float ValueNoise(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);

            float a = Hash01(Wrap(x0, periodX), Wrap(y0, periodY), seed);
            float b = Hash01(Wrap(x0 + 1, periodX), Wrap(y0, periodY), seed);
            float c = Hash01(Wrap(x0, periodX), Wrap(y0 + 1, periodY), seed);
            float d = Hash01(Wrap(x0 + 1, periodX), Wrap(y0 + 1, periodY), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        /// <summary>정수 좌표의 결정적 난수 (0~1). 같은 입력이면 항상 같은 값</summary>
        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        public static int Wrap(int i, int period) => ((i % period) + period) % period;

        static Color32 Opaque(Color c) => new(ToByte(c.r), ToByte(c.g), ToByte(c.b), 255);

        static byte ToByte(float f) => (byte)Mathf.RoundToInt(Mathf.Clamp01(f) * 255f);
    }
}
