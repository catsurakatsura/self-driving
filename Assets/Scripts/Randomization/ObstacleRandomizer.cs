using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ObstacleRandomizer : MonoBehaviour
{
    [Header("Waypoint source")]
    [Tooltip("例: Stage/Waypoints を入れる。未設定なら waypointsRootPath から自動検索する")]
    [SerializeField] private Transform waypointsRoot;

    [Tooltip("Hierarchy 上のパスで Waypoints を探す（ドラッグできないとき用）")]
    [SerializeField] private string waypointsRootPath = "Stage/Waypoints";

    [Header("Obstacle source")]
    [Tooltip("Obstacle コンポーネントが付いたオブジェクトをランダム化対象にする。無ければ名前で拾う")]
    [SerializeField] private bool preferObstacleComponent = true;

    [Tooltip("Obstacle コンポーネントが無い場合、名前がこれで始まるものを拾う（例 Rock, Rock(Clone)）")]
    [SerializeField] private string obstacleNamePrefix = "Rock";

    [Header("Placement tuning")]
    [Tooltip("候補点（Point群）を間引く。大きいほど候補が減る")]
    [SerializeField] private int useEveryNthPoint = 5;

    [Tooltip("Point の right 方向に横へずらす距離（道路幅に合わせて調整）")]
    [SerializeField] private float lateralOffset = 2.5f;

    [Tooltip("横ずらしに揺らぎを入れる（0なら固定）")]
    [SerializeField] private float lateralJitter = 0.5f;

    [Tooltip("左右どちら側に置くか（trueなら左右ランダム）")]
    [SerializeField] private bool allowBothSides = true;

    [Header("Safety")]
    [Tooltip("同じ候補点に複数障害物が乗るのを避ける（候補が足りないと一部だけ配置される）")]
    [SerializeField] private bool noDuplicatePoints = true;

    // キャッシュ
    private readonly List<Transform> cachedPoints = new();
    private readonly List<Transform> cachedObstacles = new();
    private bool initialized = false;

    private void Awake()
    {
        InitializeIfNeeded();
    }

    public void InitializeIfNeeded()
    {
        if (initialized) return;

        if (waypointsRoot == null)
        {
            var go = GameObject.Find(waypointsRootPath);
            if (go != null) waypointsRoot = go.transform;
        }

        RebuildCaches();
        initialized = true;
    }

    /// <summary>
    /// Waypoints / 障害物の探索キャッシュを作り直す（コース生成が動的なら世代ごとに呼んでもOK）
    /// </summary>
    public void RebuildCaches()
    {
        cachedPoints.Clear();
        cachedObstacles.Clear();

        // --- collect points ---
        if (waypointsRoot != null)
        {
            var all = waypointsRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t != waypointsRoot && t.name.StartsWith("Point", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int step = Mathf.Max(1, useEveryNthPoint);
            for (int i = 0; i < all.Count; i += step)
                cachedPoints.Add(all[i]);
        }

        // --- collect obstacles ---
        if (preferObstacleComponent)
        {
            // “Obstacle” という名前のコンポーネントが存在する前提（君のコードに出てきてた）
            // ただし、型が存在しないプロジェクトでもコンパイルできるように反射で拾う
            var allBehaviours = FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mb in allBehaviours)
            {
                if (mb == null) continue;
                var t = mb.GetType();
                if (t.Name == "Obstacle")
                {
                    cachedObstacles.Add(mb.transform);
                }
            }
        }

        // Obstacle コンポーネントが見つからなければ名前で拾う
        if (cachedObstacles.Count == 0)
        {
            var all = FindObjectsOfType<Transform>(true)
                .Where(t => t.name.StartsWith(obstacleNamePrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            cachedObstacles.AddRange(all);
        }
    }

    /// <summary>
    /// 指定seedで障害物配置をランダム化する（世代ごとに1回呼ぶ想定）
    /// </summary>
    public void RandomizeObstacles(int seed)
    {
        InitializeIfNeeded();

        // 動的に Rock(Clone) が生成される構造なら、毎回取り直すのが安全
        RebuildCaches();

        if (cachedPoints.Count == 0)
        {
            Debug.LogError("[ObstacleRandomizer] Point が見つからない。WaypointsRoot が正しいか、Pointの名前が Point で始まるか確認。");
            return;
        }
        if (cachedObstacles.Count == 0)
        {
            Debug.LogWarning("[ObstacleRandomizer] 障害物が見つからない（Obstacleコンポーネント/名前prefix）。");
            return;
        }

        var rng = new System.Random(seed);

        // 候補点をシャッフル
        List<Transform> pointsPool = cachedPoints.OrderBy(_ => rng.Next()).ToList();

        int placeCount = Mathf.Min(cachedObstacles.Count, pointsPool.Count);
        if (!noDuplicatePoints) placeCount = cachedObstacles.Count; // 重複OKなら障害物数だけ回す

        for (int i = 0; i < cachedObstacles.Count; i++)
        {
            Transform obs = cachedObstacles[i];
            if (obs == null) continue;

            Transform p = noDuplicatePoints ? pointsPool[i % pointsPool.Count]
                                            : pointsPool[rng.Next(pointsPool.Count)];

            float side = 1f;
            if (allowBothSides) side = (rng.NextDouble() < 0.5) ? -1f : 1f;

            float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * lateralJitter;

            // Point の向きが信用できる前提：right 方向にずらす
            Vector3 pos = p.position + p.right * (side * (lateralOffset + jitter));

            obs.position = pos;
        }
    }
}
