using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LootTheDeep.Gameplay
{
    public sealed class TilemapPathfinder2D : MonoBehaviour
    {
        [SerializeField] private Tilemap ground;
        [SerializeField] private Collider2D[] obstacles;
        [SerializeField, Min(.1f)] private float cellSize = .5f;
        [SerializeField, Min(.01f)] private float agentRadius = .6f;
        [SerializeField, Min(0f)] private float wallClearance = .03f;

        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1),
            new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1)
        };

        private readonly HashSet<Collider2D> obstacleSet = new();
        private readonly List<RaycastHit2D> castHits = new();
        private readonly List<OpenNode> open = new();
        private readonly List<Vector2> reversePath = new();
        private Vector2 origin;
        private int width, height, search;
        private byte[] walkable, checkedEdges, clearEdges;
        private int[] visited, closed, parents;
        private float[] costs;
        private ContactFilter2D filter;

        public int Revision { get; private set; }
        public float AgentRadius => agentRadius;

        private struct OpenNode
        {
            public int index;
            public float cost, score;
        }

        private void Awake()
        {
            if (ground != null) Rebuild();
        }

        public void Configure(Tilemap map, Collider2D[] blockers)
        {
            ground = map;
            obstacles = blockers;
            Rebuild();
        }

        // Nodes and edges are sampled on demand, then shared by all agents until the map is rebuilt.
        [ContextMenu("重建导航网格")]
        public void Rebuild()
        {
            Physics2D.SyncTransforms();
            obstacleSet.Clear();
            if (obstacles != null)
                foreach (Collider2D obstacle in obstacles)
                    if (obstacle != null && !obstacle.isTrigger) obstacleSet.Add(obstacle);
            filter = new ContactFilter2D().NoFilter();
            filter.useTriggers = false;
            BoundsInt bounds = ground.cellBounds;
            Vector2 a = ground.CellToWorld(bounds.min);
            Vector2 b = ground.CellToWorld(bounds.max);
            Vector2 minimum = Vector2.Min(a, b);
            Vector2 size = Vector2.Max(a, b) - minimum;
            origin = minimum + Vector2.one * cellSize * .5f;
            width = Mathf.CeilToInt(size.x / cellSize);
            height = Mathf.CeilToInt(size.y / cellSize);
            int count = width * height;
            walkable = new byte[count];
            checkedEdges = new byte[count];
            clearEdges = new byte[count];
            visited = new int[count];
            closed = new int[count];
            parents = new int[count];
            costs = new float[count];
            search = 0;
            Revision++;
        }

        public bool TryFindPath(Vector2 from, Vector2 goal, List<Vector2> result)
        {
            result.Clear();
            if (walkable == null || !HasGround(from) || !HasGround(goal) ||
                !HasClearance(goal, agentRadius - .001f)) return false;
            if (CanTraverse(from, goal))
            {
                result.Add(goal);
                return true;
            }
            int start = FindEndpoint(from);
            int end = FindEndpoint(goal);
            if (start < 0 || end < 0) return false;

            search++;
            open.Clear();
            visited[start] = search;
            parents[start] = -1;
            costs[start] = 0f;
            Push(new OpenNode { index = start, cost = 0f, score = Heuristic(start, end) });
            while (open.Count > 0)
            {
                OpenNode current = Pop();
                int index = current.index;
                if (closed[index] == search || current.cost > costs[index]) continue;
                if (index == end)
                {
                    BuildPath(from, end, goal, result);
                    return true;
                }
                closed[index] = search;
                int x = index % width, y = index / width;
                for (int direction = 0; direction < Neighbours.Length; direction++)
                {
                    Vector2Int offset = Neighbours[direction];
                    int nx = x + offset.x, ny = y + offset.y;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int next = ny * width + nx;
                    if (closed[next] == search || !IsWalkable(next)) continue;
                    if (offset.x != 0 && offset.y != 0 &&
                        (!IsWalkable(y * width + nx) || !IsWalkable(ny * width + x))) continue;
                    if (!EdgeClear(index, next, direction)) continue;
                    float cost = costs[index] + cellSize * (offset.x != 0 && offset.y != 0 ? 1.41421356f : 1f);
                    if (visited[next] == search && cost >= costs[next]) continue;
                    visited[next] = search;
                    costs[next] = cost;
                    parents[next] = index;
                    Push(new OpenNode { index = next, cost = cost, score = cost + Heuristic(next, end) });
                }
            }
            return false;
        }

        public bool CanTraverse(Vector2 from, Vector2 to)
        {
            float radius = agentRadius + wallClearance;
            if (!HasGround(from) || !HasGround(to) || !HasClearance(to, radius)) return false;
            Vector2 delta = to - from;
            float distance = delta.magnitude;
            int samples = Mathf.CeilToInt(distance / (cellSize * .5f));
            for (int i = 1; i < samples; i++)
                if (!HasGround(Vector2.Lerp(from, to, (float)i / samples))) return false;
            if (distance < .001f) return true;
            Vector2 direction = delta / distance;
            Vector2 castStart = from;
            float escaped = 0f;
            if (!HasClearance(from, radius))
            {
                // Leave only the clearance margin, without skipping the rest of a composite wall.
                if (!HasClearance(from, agentRadius - .001f)) return false;
                while (escaped < distance)
                {
                    escaped = Mathf.Min(escaped + .01f, distance);
                    castStart = from + direction * escaped;
                    if (!HasClearance(castStart, agentRadius - .001f)) return false;
                    if (HasClearance(castStart, radius)) break;
                }
            }
            Physics2D.CircleCast(castStart, agentRadius, direction, filter, castHits, distance - escaped);
            foreach (RaycastHit2D hit in castHits)
            {
                if (!obstacleSet.Contains(hit.collider)) continue;
                return false;
            }
            return true;
        }

        private bool HasGround(Vector2 position) => ground.HasTile(ground.WorldToCell(position));

        private bool HasClearance(Vector2 position, float radius)
        {
            foreach (Collider2D obstacle in obstacleSet)
                if (obstacle != null && obstacle.enabled && obstacle.gameObject.activeInHierarchy &&
                    (obstacle.ClosestPoint(position) - position).sqrMagnitude < radius * radius) return false;
            return true;
        }

        private Vector2 Position(int index) => origin + new Vector2(index % width, index / width) * cellSize;

        private bool IsWalkable(int index)
        {
            if (walkable[index] == 0)
            {
                Vector2 position = Position(index);
                walkable[index] = (byte)(HasGround(position) && HasClearance(position, agentRadius + wallClearance) ? 2 : 1);
            }
            return walkable[index] == 2;
        }

        private int FindEndpoint(Vector2 position)
        {
            Vector2 relative = (position - origin) / cellSize;
            int cx = Mathf.RoundToInt(relative.x), cy = Mathf.RoundToInt(relative.y);
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int y = cy - 2; y <= cy + 2; y++)
                for (int x = cx - 2; x <= cx + 2; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;
                    int index = y * width + x;
                    float distance = (Position(index) - position).sqrMagnitude;
                    if (distance >= bestDistance || !IsWalkable(index) || !CanTraverse(position, Position(index))) continue;
                    best = index;
                    bestDistance = distance;
                }
            return best;
        }

        private bool EdgeClear(int from, int to, int direction)
        {
            int mask = 1 << direction;
            if ((checkedEdges[from] & mask) == 0)
            {
                bool clear = CanTraverse(Position(from), Position(to));
                checkedEdges[from] |= (byte)mask;
                int reverse = 1 << ((direction + 4) % 8);
                checkedEdges[to] |= (byte)reverse;
                if (clear)
                {
                    clearEdges[from] |= (byte)mask;
                    clearEdges[to] |= (byte)reverse;
                }
            }
            return (clearEdges[from] & mask) != 0;
        }

        private float Heuristic(int from, int to)
        {
            int dx = Mathf.Abs(from % width - to % width), dy = Mathf.Abs(from / width - to / width);
            return cellSize * (Mathf.Max(dx, dy) + .41421356f * Mathf.Min(dx, dy));
        }

        private void BuildPath(Vector2 from, int end, Vector2 goal, List<Vector2> result)
        {
            reversePath.Clear();
            for (int index = end; index >= 0; index = parents[index]) reversePath.Add(Position(index));
            result.Add(from);
            for (int i = reversePath.Count - 1; i >= 0; i--) result.Add(reversePath[i]);
            if (HasClearance(goal, agentRadius + wallClearance) && CanTraverse(Position(end), goal)) result.Add(goal);
            // Remove grid zigzags only where the entire body can pass between path points.
            for (int i = 0; i < result.Count - 2; i++)
            {
                int next = result.Count - 1;
                while (next > i + 1 && !CanTraverse(result[i], result[next])) next--;
                if (next > i + 1) result.RemoveRange(i + 1, next - i - 1);
            }
            result.RemoveAt(0);
        }

        private void Push(OpenNode node)
        {
            int index = open.Count;
            open.Add(node);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (open[parent].score <= node.score) break;
                open[index] = open[parent];
                index = parent;
            }
            open[index] = node;
        }

        private OpenNode Pop()
        {
            OpenNode first = open[0], last = open[open.Count - 1];
            open.RemoveAt(open.Count - 1);
            if (open.Count == 0) return first;
            int index = 0;
            while (index * 2 + 1 < open.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < open.Count && open[child + 1].score < open[child].score) child++;
                if (open[child].score >= last.score) break;
                open[index] = open[child];
                index = child;
            }
            open[index] = last;
            return first;
        }
    }
}
