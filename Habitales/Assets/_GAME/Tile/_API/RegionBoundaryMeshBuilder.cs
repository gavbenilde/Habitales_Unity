using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a flat mesh tracing the outer boundary seam of a region.
/// Quads sit exactly on the tile surface — no vertical offset.
/// </summary>
public static class RegionBoundaryMeshBuilder
{
    private static readonly (Vector2Int dir, Vector2 seamA, Vector2 seamB, Vector2 straddleDir)[] Edges =
    {
        (Vector2Int.up,    new Vector2(-0.5f,  0.5f), new Vector2( 0.5f,  0.5f), new Vector2( 0f, -1f)),
        (Vector2Int.down,  new Vector2( 0.5f, -0.5f), new Vector2(-0.5f, -0.5f), new Vector2( 0f,  1f)),
        (Vector2Int.right, new Vector2( 0.5f,  0.5f), new Vector2( 0.5f, -0.5f), new Vector2(-1f,  0f)),
        (Vector2Int.left,  new Vector2(-0.5f, -0.5f), new Vector2(-0.5f,  0.5f), new Vector2( 1f,  0f)),
    };

    /// <summary>
    /// Builds a mesh of quads sitting exactly on the boundary seam of the region.
    /// The Y of each vertex is taken directly from the tile's world position —
    /// no lift, no offset.
    /// </summary>
    public static Mesh Build(List<Tile> regionTiles, TileManager tileManager, float outlineWidth, float yOffset)
    {
        HashSet<Vector2Int> regionSet = new HashSet<Vector2Int>();
        foreach (Tile tile in regionTiles)
            regionSet.Add(tile.gridPosition);

        List<Vector3> verts = new List<Vector3>();
        List<int>     tris  = new List<int>();

        float halfWidth = outlineWidth * 0.5f;

        foreach (Tile tile in regionTiles)
        {
            Vector3 worldCenter = tileManager.GridToWorldPosition(tile.gridPosition);

            foreach (var edge in Edges)
            {
                if (regionSet.Contains(tile.gridPosition + edge.dir))
                    continue;

                // Seam endpoints, Y taken directly from the tile's world position
                Vector3 seamA = new Vector3(
                    worldCenter.x + edge.seamA.x,
                    worldCenter.y + yOffset,
                    worldCenter.z + edge.seamA.y);

                Vector3 seamB = new Vector3(
                    worldCenter.x + edge.seamB.x,
                    worldCenter.y + yOffset,
                    worldCenter.z + edge.seamB.y);

                Vector3 stride = new Vector3(edge.straddleDir.x, 0f, edge.straddleDir.y) * halfWidth;

                Vector3 innerA = seamA + stride;
                Vector3 outerA = seamA - stride;
                Vector3 innerB = seamB + stride;
                Vector3 outerB = seamB - stride;

                int baseIndex = verts.Count;
                verts.Add(outerA);
                verts.Add(outerB);
                verts.Add(innerA);
                verts.Add(innerB);

                tris.Add(baseIndex + 0);
                tris.Add(baseIndex + 1);
                tris.Add(baseIndex + 2);

                tris.Add(baseIndex + 1);
                tris.Add(baseIndex + 3);
                tris.Add(baseIndex + 2);
            }
        }

        if (verts.Count == 0)
            return new Mesh();

        Mesh mesh = new Mesh();
        mesh.name = "RegionOutline";
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        return mesh;
    }
}
