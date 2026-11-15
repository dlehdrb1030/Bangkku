using System;
using System.Collections.Generic;
using Bangkku.FloorPlan;
using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// 창밖 풍경 파노라마를 코드로 그린다 (다운로드·저작권 없음). 가로 = 원통 한 바퀴 (이음새 없이 이어짐), 세로 = 땅 ~ 원통 위 끝.
    /// 실제 거리에 있는 물체를 원통(ExteriorBuilder.RadiusM)에 투영한 크기로 그려서, 창으로 볼 때 원근이 맞는다:
    /// 원통 높이 = 눈높이 + (실제 높이 - 눈높이) × 반지름 / 거리
    /// - 하늘: 지평선 옅은 하늘색 → 위쪽 파랑, 구름
    /// - 먼 아파트 (450m): 안개에 묻힌 고층
    /// - 가까운 아파트 (220m): 층·창 무늬, 옆면 그늘
    /// - 나무 (110m), 땅
    /// UnityEditor 를 쓰지 않는 순수 계산이라 따로 시험할 수 있다. 파일 저장·재질 연결은 RoomTexturesMenu 가 한다.
    /// </summary>
    public static class ExteriorPanorama
    {
        public struct Result
        {
            public int Width, Height;
            public Color32[] Pixels;
        }

        static readonly Color SkyHorizon = new(0.86f, 0.90f, 0.94f);
        static readonly Color SkyZenith = new(0.42f, 0.62f, 0.88f);
        static readonly Color GroundNear = new(0.34f, 0.42f, 0.27f);
        static readonly Color TreeColor = new(0.22f, 0.33f, 0.20f);
        static readonly Color WindowColor = new(0.33f, 0.40f, 0.48f);

        static readonly Color[] FacadeColors =
        {
            new(0.90f, 0.88f, 0.84f), new(0.84f, 0.84f, 0.83f), new(0.92f, 0.91f, 0.88f),
            new(0.80f, 0.76f, 0.70f), new(0.86f, 0.83f, 0.78f),
        };

        public const float FloorHeightM = 2.8f;
        public const float BayWidthM = 3.2f;

        /// <summary>한 겹의 건물들 (같은 거리)</summary>
        public struct Layer
        {
            public float Distance;
            public float MinHeight, MaxHeight;
            public float MinWidth, MaxWidth;
            public float MinGap, MaxGap;
            public int Seed;
        }

        /// <summary>건물 하나. 원통 둘레 위치(m, 원통 위 길이)와 실제 크기(m)</summary>
        public struct Building
        {
            public float Start, ArcWidth;
            public float Width, Height;
            public int Index;
        }

        public static readonly Layer Far = new() { Distance = 450f, MinHeight = 40f, MaxHeight = 80f, MinWidth = 18f, MaxWidth = 34f, MinGap = 8f, MaxGap = 60f, Seed = 71 };
        public static readonly Layer Near = new() { Distance = 220f, MinHeight = 25f, MaxHeight = 55f, MinWidth = 22f, MaxWidth = 45f, MinGap = 20f, MaxGap = 90f, Seed = 83 };
        public const float TreeDistance = 110f;

        public static Result Generate(int width, int height)
        {
            float radius = ExteriorBuilder.RadiusM;
            float eye = ExteriorBuilder.EyeHeightM;
            float ground = ExteriorBuilder.GroundY;
            float circumference = 2f * Mathf.PI * radius;

            var farList = PlaceBuildings(Far, circumference, radius);
            var nearList = PlaceBuildings(Near, circumference, radius);
            var far = Columns(farList, width, circumference);
            var near = Columns(nearList, width, circumference);

            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                // 나무 줄 높이 (m, 땅에서): 완만한 굴곡 + 잔 굴곡
                float treeHeight = 5f + 5f * ProceduralTextures.Fbm(u, 0.5f, 48, 1, 4, 5) + 1.5f * ProceduralTextures.Fbm(u, 0.5f, 400, 1, 2, 9);

                for (int y = 0; y < height; y++)
                {
                    float v = (y + 0.5f) / height;
                    float yc = Mathf.Lerp(ground, ExteriorBuilder.TopY, v); // 원통 위 높이 (집 바닥 기준 m)
                    Color c;

                    if (yc >= eye) c = Sky(u, yc, eye, radius);
                    else c = Ground(u, v, yc, eye, ground, radius, y);

                    c = DrawBuildings(c, far, farList, Far, u, yc, eye, ground, radius, x);
                    c = DrawBuildings(c, near, nearList, Near, u, yc, eye, ground, radius, x);

                    // 나무
                    float treeY = WorldY(yc, eye, radius, TreeDistance) - ground;
                    if (treeY >= 0f && treeY < treeHeight)
                    {
                        float leaf = ProceduralTextures.Fbm(u, v, 900, 120, 3, 13);
                        var tree = TreeColor * (0.75f + 0.5f * leaf);
                        c = Haze(tree, TreeDistance);
                    }

                    pixels[y * width + x] = Opaque(c);
                }
            }

            return new Result { Width = width, Height = height, Pixels = pixels };
        }

        /// <summary>원통 높이 yc 에 보이는, 거리 d 에 있는 점의 실제 높이 (집 바닥 기준 m)</summary>
        public static float WorldY(float yc, float eye, float radius, float distance) => eye + (yc - eye) * distance / radius;

        /// <summary>실제 높이 y (집 바닥 기준 m) 가 거리 d 에서 원통에 보이는 높이</summary>
        public static float CylinderY(float y, float eye, float radius, float distance) => eye + (y - eye) * radius / distance;

        /// <summary>거리에 따라 하늘색으로 묻힘 (공기 원근)</summary>
        static Color Haze(Color c, float distance) => Color.Lerp(c, SkyHorizon, 1f - Mathf.Exp(-distance / 700f));

        static Color Sky(float u, float yc, float eye, float radius)
        {
            float elevation = Mathf.Atan2(yc - eye, radius) * Mathf.Rad2Deg; // 올려다본 각 (도)
            var c = Color.Lerp(SkyHorizon, SkyZenith, Mathf.Pow(Mathf.Clamp01(elevation / 40f), 0.7f));

            // 구름: 가로로 긴 무늬. 지평선 바로 위와 맨 위(뚜껑과 같은 색이어야 함)에서는 없앤다
            float fade = Mathf.Clamp01(elevation / 3f) * Mathf.Clamp01((28f - elevation) / 6f);
            if (fade > 0f)
            {
                float vCloud = elevation / 40f;
                float n = ProceduralTextures.Fbm(u, vCloud, 12, 3, 5, 31);
                float cloud = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n - 0.52f) / 0.2f)) * fade;
                c = Color.Lerp(c, new Color(0.97f, 0.97f, 0.98f), cloud * 0.85f);
            }

            return c;
        }

        static Color Ground(float u, float v, float yc, float eye, float ground, float radius, int row)
        {
            // 이 높이에 보이는 땅까지 거리: 원통(반지름)에서 땅이 보이면 바로 그 거리, 지평선에 가까울수록 멀다
            float distance = radius * (eye - ground) / Mathf.Max(0.01f, eye - yc);
            var c = GroundNear;
            // 맨 아래 줄은 무늬 없이 (땅 원판이 이 색을 그대로 씀). 위로 갈수록 무늬가 서서히 나타남 (경계선이 보이지 않게)
            float amount = Mathf.Clamp01((row - 2) / 30f);
            if (amount > 0f)
            {
                float n = ProceduralTextures.Fbm(u, v, 200, 20, 3, 17);
                c *= 1f + (n - 0.5f) * 0.3f * amount;
            }

            return Haze(c, distance);
        }

        static Color DrawBuildings(Color c, int[] columns, List<Building> buildings, Layer layer, float u, float yc,
            float eye, float ground, float radius, int x)
        {
            int index = columns[x];
            if (index < 0) return c;
            var b = buildings[index];

            float worldY = WorldY(yc, eye, radius, layer.Distance) - ground; // 땅에서 높이
            if (worldY < 0f || worldY > b.Height) return c;

            float circumference = 2f * Mathf.PI * radius;
            float arc = u * circumference;
            float local = Mathf.Repeat(arc - b.Start, circumference) / b.ArcWidth; // 건물 안 가로 위치 0~1
            float localX = local * b.Width;

            var facade = FacadeColors[b.Index % FacadeColors.Length];
            // 한쪽 끝 18% 는 옆면 (그늘)
            bool side = local > 0.82f;
            var wall = side ? facade * 0.78f : facade;

            // 옥상 난간 띠
            if (worldY > b.Height - 1.2f) wall *= 0.9f;
            else if (!side && worldY > FloorHeightM)
            {
                float floorFrac = Mathf.Repeat(worldY, FloorHeightM) / FloorHeightM;
                float bayFrac = Mathf.Repeat(localX, BayWidthM) / BayWidthM;
                if (floorFrac > 0.32f && floorFrac < 0.82f && bayFrac > 0.12f && bayFrac < 0.88f)
                {
                    int floor = (int)(worldY / FloorHeightM), bay = (int)(localX / BayWidthM);
                    float tone = ProceduralTextures.Hash01(floor + b.Index * 97, bay, layer.Seed) * 0.25f;
                    wall = WindowColor * (0.85f + tone);
                }
                else if (floorFrac < 0.06f)
                {
                    wall *= 0.92f; // 층 사이 띠
                }
            }

            return Haze(wall, layer.Distance);
        }

        /// <summary>
        /// 원통 둘레(m)를 따라 건물을 놓는다. 위치는 원통 위 길이, 크기는 실제 m. 마지막 건물은 한 바퀴를 넘어 처음과 이어질 수 있다
        /// </summary>
        public static List<Building> PlaceBuildings(Layer layer, float circumference, float radius)
        {
            var result = new List<Building>();
            float scale = radius / layer.Distance;
            float cursor = ProceduralTextures.Hash01(0, 0, layer.Seed) * layer.MaxGap * scale;
            for (int i = 0; ; i++)
            {
                float w = Mathf.Lerp(layer.MinWidth, layer.MaxWidth, ProceduralTextures.Hash01(i, 1, layer.Seed));
                float h = Mathf.Lerp(layer.MinHeight, layer.MaxHeight, ProceduralTextures.Hash01(i, 2, layer.Seed));
                float arcWidth = w * scale;
                // 첫 건물과 겹치지 않을 때만 놓는다
                if (result.Count > 0 && cursor + arcWidth > circumference + result[0].Start) break;
                result.Add(new Building { Start = cursor, ArcWidth = arcWidth, Width = w, Height = h, Index = i });
                float gap = Mathf.Lerp(layer.MinGap, layer.MaxGap, ProceduralTextures.Hash01(i, 3, layer.Seed));
                cursor += arcWidth + gap * scale;
                if (cursor >= circumference + result[0].Start) break;
            }

            return result;
        }

        /// <summary>열마다 덮는 건물 번호 (없으면 -1). 한 바퀴를 넘는 건물은 처음 쪽 열에도 표시</summary>
        public static int[] Columns(List<Building> buildings, int width, float circumference)
        {
            var columns = new int[width];
            Array.Fill(columns, -1);
            for (int x = 0; x < width; x++)
            {
                float arc = (x + 0.5f) / width * circumference;
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if (Mathf.Repeat(arc - b.Start, circumference) < b.ArcWidth)
                    {
                        columns[x] = i;
                        break;
                    }
                }
            }

            return columns;
        }

        static Color32 Opaque(Color c) => new(ToByte(c.r), ToByte(c.g), ToByte(c.b), 255);

        static byte ToByte(float f) => (byte)Mathf.RoundToInt(Mathf.Clamp01(f) * 255f);
    }
}
