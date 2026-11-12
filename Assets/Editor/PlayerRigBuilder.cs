// Builds a DRAFT Unity 2D Animation bone rig on Assets/Character/Player.psb:
//   character skeleton (SpriteBones) -> per-sprite bone subsets -> grid mesh -> distance-based weights.
// Input: ArtSource/bone_layout.json (canvas pixels, y down) and ArtSource/layers/*.png + layers.json
// (layer pixels extracted from the GIMP XCF). Re-running OVERWRITES skeleton/mesh/weights on the PSB,
// so after you hand-tune them in the Skinning Editor, do not run this again (or back up Player.psb.meta first).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Animation;
using UnityEditor.U2D.PSD;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

public static class PlayerRigBuilder
{
    const string PsbPath = "Assets/Character/Player.psb";
    const string PrefabPath = "Assets/Character/Player.prefab";
    const float PixelsPerUnit = 400f;

    [Serializable] class BoneDef { public string name; public string parent; public float[] head; public float[] tail; }
    [Serializable] class LayerDef { public string name; public string file; public bool visible; }
    [Serializable] class LayersFile { public int[] canvas; public LayerDef[] layers; }

    class Bone
    {
        public string name; public int parent = -1;
        public Vector2 head, tail;           // character space, pixels, y up
        public float worldAngle;             // degrees
    }

    static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

    [MenuItem("Tools/Player Rig/Build Draft Rig (overwrites skinning)")]
    public static void BuildDraft() => BuildDraftAt(PsbPath, true);

    // Layers are matched by NAME on reimport (the PSB written by Tools/build_psb.py has no layer IDs),
    // so repainting a part and rebuilding the PSB keeps its bones/mesh/weights.
    public static void UseLayerNameMapping(string psbPath)
    {
        var importer = (PSDImporter)AssetImporter.GetAtPath(psbPath);
        var so = new SerializedObject(importer);
        var prop = so.FindProperty("m_LayerMappingOption");
        if (prop.intValue == 0) return; // 0 = UseLayerName
        prop.intValue = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
        importer.SaveAndReimport();
    }

    [Serializable] class GrownPart { public string name; public int growLeft; public int growBottom; public int width; public int height; }
    [Serializable] class GrownList { public GrownPart[] parts; }

    // Called by Tools/update_parts.py after a part's locked rect grew. The PSD Importer keeps the old sprite rect
    // size on reimport, so: enlarge the rect to the new layer size and shift that sprite's mesh vertices and
    // root-level sprite bones by the growth, so the existing (user-edited) skinning stays where it was.
    // The newly painted area has no mesh yet: extend it in the Skinning Editor.
    public static string RefitGrownParts(string psbPath, string grownJsonPath)
    {
        var grown = JsonUtility.FromJson<GrownList>(File.ReadAllText(grownJsonPath)).parts;
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        var rects = dp.GetSpriteRects();
        var log = new List<string>();
        foreach (var g in grown)
        {
            var rect = rects.FirstOrDefault(r => r.name == g.name);
            if (rect == null) { log.Add(g.name + ": sprite not found"); continue; }
            var r0 = rect.rect;
            rect.rect = new Rect(r0.x, r0.y, g.width, g.height);
            var shift = new Vector2(g.growLeft, g.growBottom);
            var verts = meshProvider.GetVertices(rect.spriteID);
            for (int i = 0; i < verts.Length; ++i) verts[i].position += shift;
            meshProvider.SetVertices(rect.spriteID, verts);
            var bones = boneProvider.GetBones(rect.spriteID);
            for (int i = 0; i < bones.Count; ++i)
                if (bones[i].parentId < 0) { var b = bones[i]; b.position += (Vector3)shift; bones[i] = b; }
            boneProvider.SetBones(rect.spriteID, bones);
            log.Add($"{g.name}: rect {r0.width}x{r0.height} -> {g.width}x{g.height}, skin shifted {shift}");
        }
        dp.SetSpriteRects(rects);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        // The newly painted area has no mesh yet: rebuild these sprites' meshes over their new pixels.
        foreach (var g in grown) log.Add(RemeshPart(psbPath, g.name));
        return string.Join("\n", log);
    }

    // Rebuilds one sprite's grid mesh from its current pixels (ArtSource/layers). Each new vertex copies the
    // weights of the nearest old vertex, so hand-painted weights carry over approximately; bones are untouched.
    // Hand-edited mesh topology (vertices/edges you placed yourself) is replaced.
    public static string RemeshPart(string psbPath, string spriteName)
    {
        var layersFile = JsonUtility.FromJson<LayersFile>(File.ReadAllText(Path.Combine(ProjectRoot, "ArtSource/layers/layers.json")));
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var rect = dp.GetSpriteRects().FirstOrDefault(r => r.name == spriteName);
        if (rect == null) return "remesh: sprite not found: " + spriteName;
        var cd = dp.GetDataProvider<ICharacterDataProvider>().GetCharacterData();
        Vector2 partPos = cd.parts.First(x => x.spriteId == rect.spriteID.ToString()).spritePosition.position;
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        var old = meshProvider.GetVertices(rect.spriteID);

        var layer = layersFile.layers.First(l => l.name == spriteName + ".png");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(Path.Combine(ProjectRoot, "ArtSource/layers", layer.file)));
        BuildGridMesh(tex, layersFile.canvas[1], partPos, (int)rect.rect.width, (int)rect.rect.height, out var verts, out var indices, out var edges);
        UnityEngine.Object.DestroyImmediate(tex);

        var vmeta = new Vertex2DMetaData[verts.Count];
        for (int v = 0; v < verts.Count; ++v)
        {
            vmeta[v].position = verts[v];
            var nearest = old.Length > 0 ? old.OrderBy(o => (o.position - verts[v]).sqrMagnitude).First().boneWeight
                                         : new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            vmeta[v].boneWeight = nearest;
        }
        meshProvider.SetVertices(rect.spriteID, vmeta);
        meshProvider.SetIndices(rect.spriteID, indices.ToArray());
        meshProvider.SetEdges(rect.spriteID, edges.ToArray());
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return $"{spriteName}: mesh rebuilt over {rect.rect.width}x{rect.rect.height} ({old.Length} -> {verts.Count} vertices, weights from nearest old vertex)";
    }

    [Serializable] class NewPart { public string name; public string[] bones; }
    [Serializable] class NewPartList { public NewPart[] parts; }

    // Skins sprites that were just added to the PSB (Tools/update_parts.py writes ArtSource/pending_skin.json).
    // Uses the CURRENT character skeleton (your Skinning Editor joint edits), builds a grid mesh from the part's
    // pixels and weights it: one bone = rigid, several = distance-based. Other sprites are not touched.
    public static string SkinNewParts(string psbPath, string jsonPath)
    {
        var list = JsonUtility.FromJson<NewPartList>(File.ReadAllText(jsonPath)).parts;
        var layersFile = JsonUtility.FromJson<LayersFile>(File.ReadAllText(Path.Combine(ProjectRoot, "ArtSource/layers/layers.json")));
        int canvasH = layersFile.canvas[1];
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var charProvider = dp.GetDataProvider<ICharacterDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        var rects = dp.GetSpriteRects();
        CharacterData cd = charProvider.GetCharacterData();
        var cb = cd.bones;

        // character-space (pixels, y up) world transform of every bone
        var wPos = new Vector2[cb.Length]; var wRot = new float[cb.Length];
        for (int i = 0; i < cb.Length; ++i)
        {
            float local = cb[i].rotation.eulerAngles.z;
            if (cb[i].parentId < 0) { wPos[i] = cb[i].position; wRot[i] = local; }
            else
            {
                int p = cb[i].parentId; // parents always come before children
                wPos[i] = wPos[p] + (Vector2)(Quaternion.Euler(0, 0, wRot[p]) * (Vector2)cb[i].position);
                wRot[i] = wRot[p] + local;
            }
        }

        var log = new List<string>();
        foreach (var np in list)
        {
            var rect = rects.FirstOrDefault(r => r.name == np.name);
            if (rect == null) { log.Add(np.name + ": sprite not found in PSB"); continue; }
            int pi = Array.FindIndex(cd.parts, x => x.spriteId == rect.spriteID.ToString());
            var part = cd.parts[pi];
            int[] subset = np.bones.Select(n =>
            {
                int i = Array.FindIndex(cb, b => b.name == n);
                if (i < 0) throw new Exception("Unknown bone " + n + " for " + np.name);
                return i;
            }).OrderBy(i => i).ToArray();
            part.bones = subset;
            cd.parts[pi] = part;
            Vector2 partPos = part.spritePosition.position;

            var spriteBones = new List<SpriteBone>();
            foreach (int gi in subset)
            {
                var b = cb[gi];
                int lp = b.parentId >= 0 ? Array.IndexOf(subset, b.parentId) : -1;
                if (lp < 0) { b.position = new Vector3(wPos[gi].x - partPos.x, wPos[gi].y - partPos.y, b.position.z); b.rotation = Quaternion.Euler(0, 0, wRot[gi]); }
                b.parentId = lp;
                spriteBones.Add(b);
            }
            boneProvider.SetBones(rect.spriteID, spriteBones);

            var layer = layersFile.layers.First(l => l.name == np.name + ".png");
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(Path.Combine(ProjectRoot, "ArtSource/layers", layer.file)));
            BuildGridMesh(tex, canvasH, partPos, (int)rect.rect.width, (int)rect.rect.height, out var verts, out var indices, out var edges);
            UnityEngine.Object.DestroyImmediate(tex);

            var segs = subset.Select(gi => new Bone { head = wPos[gi], tail = wPos[gi] + (Vector2)(Quaternion.Euler(0, 0, wRot[gi]) * new Vector2(cb[gi].length, 0)) }).ToList();
            var all = Enumerable.Range(0, segs.Count).ToArray();
            var vmeta = new Vertex2DMetaData[verts.Count];
            for (int v = 0; v < verts.Count; ++v)
            {
                vmeta[v].position = verts[v];
                vmeta[v].boneWeight = subset.Length == 1 ? new BoneWeight { boneIndex0 = 0, weight0 = 1f } : Weigh(segs, all, verts[v] + partPos);
            }
            meshProvider.SetVertices(rect.spriteID, vmeta);
            meshProvider.SetIndices(rect.spriteID, indices.ToArray());
            meshProvider.SetEdges(rect.spriteID, edges.ToArray());
            log.Add($"{np.name}: bones=[{string.Join(",", np.bones)}] verts={verts.Count}");
        }
        charProvider.SetCharacterData(cd);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return string.Join("\n", log);
    }

    // Makes one sprite rigid: every vertex 100% on boneName. Only that sprite's weights change;
    // bone positions, other sprites and meshes are left as they are (safe after Skinning Editor edits).
    public static string SetRigid(string psbPath, string spriteName, string boneName)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var rect = dp.GetSpriteRects().FirstOrDefault(r => r.name == spriteName);
        if (rect == null) return "sprite not found: " + spriteName;
        var bones = dp.GetDataProvider<ISpriteBoneDataProvider>().GetBones(rect.spriteID);
        int bi = bones.FindIndex(b => b.name == boneName);
        if (bi < 0) return $"{spriteName} is not bound to bone {boneName} (bones: {string.Join(",", bones.Select(b => b.name))})";
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        var verts = meshProvider.GetVertices(rect.spriteID);
        for (int i = 0; i < verts.Length; ++i)
            verts[i].boneWeight = new BoneWeight { boneIndex0 = bi, weight0 = 1f };
        meshProvider.SetVertices(rect.spriteID, verts);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return $"{spriteName}: {verts.Length} vertices -> 100% {boneName}";
    }

    public static void BuildDraftAt(string psbPath, bool createPrefab)
    {
        string layoutJson = File.ReadAllText(Path.Combine(ProjectRoot, "ArtSource/bone_layout.json"));
        var layersFile = JsonUtility.FromJson<LayersFile>(File.ReadAllText(Path.Combine(ProjectRoot, "ArtSource/layers/layers.json")));
        int canvasH = layersFile.canvas[1];

        // --- parse bones (JsonUtility can't read the dictionary, so parts are parsed by hand below)
        var boneDefs = JsonUtility.FromJson<BoneList>(layoutJson).bones;
        var bones = new List<Bone>();
        foreach (var d in boneDefs)
        {
            var b = new Bone
            {
                name = d.name,
                head = new Vector2(d.head[0], canvasH - d.head[1]),
                tail = new Vector2(d.tail[0], canvasH - d.tail[1]),
                parent = string.IsNullOrEmpty(d.parent) ? -1 : bones.FindIndex(x => x.name == d.parent)
            };
            if (!string.IsNullOrEmpty(d.parent) && b.parent < 0) throw new Exception("Parent must come before child: " + d.name);
            Vector2 dir = b.tail - b.head;
            b.worldAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            bones.Add(b);
        }
        var partBones = ParseParts(layoutJson);

        // --- importer + data providers
        UseLayerNameMapping(psbPath);
        var importer = (PSDImporter)AssetImporter.GetAtPath(psbPath);
        importer.useMosaicMode = true;
        importer.useCharacterMode = true;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
        importer = (PSDImporter)AssetImporter.GetAtPath(psbPath); // reimport recreates the importer object

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
        dp.InitSpriteEditorDataProvider();
        var charProvider = dp.GetDataProvider<ICharacterDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        var rects = dp.GetSpriteRects();

        CharacterData cd = charProvider.GetCharacterData();
        cd.bones = bones.Select((b, i) => ToSpriteBone(bones, i, Vector2.zero, i2 => i2)).ToArray();
        // character bones: root-level bones use absolute position; others parent-local (ToSpriteBone handles both)

        var report = new List<string>();
        for (int p = 0; p < cd.parts.Length; ++p)
        {
            var part = cd.parts[p];
            var rect = rects.First(r => r.spriteID.ToString() == part.spriteId);
            if (!partBones.TryGetValue(rect.name, out var names))
                throw new Exception("No bone mapping in bone_layout.json for sprite " + rect.name);

            int[] subset = names.Select(n =>
            {
                int i = bones.FindIndex(b => b.name == n);
                if (i < 0) throw new Exception("Unknown bone " + n + " for " + rect.name);
                return i;
            }).OrderBy(i => i).ToArray();
            part.bones = subset;
            cd.parts[p] = part;

            Vector2 partPos = part.spritePosition.position;
            var map = new Dictionary<int, int>();
            for (int k = 0; k < subset.Length; ++k) map[subset[k]] = k;
            var spriteBones = subset.Select(i => ToSpriteBone(bones, i, partPos, gi => map.TryGetValue(gi, out var li) ? li : -1)).ToList();
            boneProvider.SetBones(rect.spriteID, spriteBones);

            // --- mesh from layer alpha
            var layer = layersFile.layers.First(l => l.name == rect.name + ".png");
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(Path.Combine(ProjectRoot, "ArtSource/layers", layer.file)));
            BuildGridMesh(tex, canvasH, partPos, (int)rect.rect.width, (int)rect.rect.height,
                out var verts, out var indices, out var edges);
            UnityEngine.Object.DestroyImmediate(tex);

            var vmeta = new Vertex2DMetaData[verts.Count];
            for (int v = 0; v < verts.Count; ++v)
            {
                vmeta[v].position = verts[v];
                vmeta[v].boneWeight = Weigh(bones, subset, verts[v] + partPos);
            }
            meshProvider.SetVertices(rect.spriteID, vmeta);
            meshProvider.SetIndices(rect.spriteID, indices.ToArray());
            meshProvider.SetEdges(rect.spriteID, edges.ToArray());
            report.Add($"{rect.name}: pos={part.spritePosition} bones=[{string.Join(",", names)}] verts={verts.Count} tris={indices.Count / 3}");
        }

        charProvider.SetCharacterData(cd);
        dp.Apply(); // writes importer state to Player.psb.meta
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[PlayerRigBuilder] Draft rig applied\n" + string.Join("\n", report));
        if (createPrefab) CreatePrefab();
    }

    [Serializable] class BoneList { public BoneDef[] bones; }

    static SpriteBone ToSpriteBone(List<Bone> bones, int i, Vector2 rootOffset, Func<int, int> remapParent)
    {
        var b = bones[i];
        int parent = b.parent >= 0 ? remapParent(b.parent) : -1;
        Vector2 pos; float rot;
        if (parent >= 0)
        {
            var pb = bones[b.parent];
            pos = Quaternion.Euler(0, 0, -pb.worldAngle) * (b.head - pb.head);
            rot = b.worldAngle - pb.worldAngle;
        }
        else
        {
            pos = b.head - rootOffset;
            rot = b.worldAngle;
        }
        return new SpriteBone
        {
            name = b.name,
            guid = GUID.Generate().ToString(),
            position = new Vector3(pos.x, pos.y, 0),
            rotation = Quaternion.Euler(0, 0, rot),
            length = (b.tail - b.head).magnitude,
            parentId = parent,
            color = Color.HSVToRGB((i * 0.137f) % 1f, 0.8f, 1f)
        };
    }

    // Grid mesh: cells that contain any visible pixel become two triangles. Positions are sprite-local pixels (y up).
    static void BuildGridMesh(Texture2D layerTex, int canvasH, Vector2 partPos, int w, int h,
        out List<Vector2> verts, out List<int> indices, out List<Vector2Int> edges)
    {
        int cell = Mathf.Max(w, h) > 400 ? 20 : Mathf.Max(w, h) > 150 ? 14 : 8;
        int cx = Mathf.CeilToInt(w / (float)cell), cy = Mathf.CeilToInt(h / (float)cell);
        var pixels = layerTex.GetPixels32(); int tw = layerTex.width; // texture rows are bottom-up already
        bool Filled(int gx, int gy)
        {
            for (int y = gy * cell; y < Mathf.Min(h, (gy + 1) * cell); ++y)
            for (int x = gx * cell; x < Mathf.Min(w, (gx + 1) * cell); ++x)
            {
                int px = (int)partPos.x + x, py = (int)partPos.y + y;
                if (px < 0 || py < 0 || px >= tw || py >= layerTex.height) continue;
                if (pixels[py * tw + px].a > 8) return true;
            }
            return false;
        }
        var vIndex = new Dictionary<Vector2Int, int>();
        verts = new List<Vector2>(); indices = new List<int>();
        var vlist = verts;
        int V(int gx, int gy)
        {
            var key = new Vector2Int(gx, gy);
            if (!vIndex.TryGetValue(key, out int id))
            {
                id = vlist.Count; vIndex[key] = id;
                vlist.Add(new Vector2(Mathf.Min(gx * cell, w), Mathf.Min(gy * cell, h)));
            }
            return id;
        }
        var edgeCount = new Dictionary<(int, int), int>();
        void E(int a, int b) { var k = a < b ? (a, b) : (b, a); edgeCount[k] = edgeCount.TryGetValue(k, out var c) ? c + 1 : 1; }
        for (int gy = 0; gy < cy; ++gy)
        for (int gx = 0; gx < cx; ++gx)
        {
            if (!Filled(gx, gy)) continue;
            int a = V(gx, gy), b = V(gx + 1, gy), c = V(gx + 1, gy + 1), d = V(gx, gy + 1);
            indices.AddRange(new[] { a, c, b, a, d, c });
            E(a, b); E(b, c); E(c, d); E(d, a); E(a, c);
        }
        edges = edgeCount.Where(kv => kv.Value == 1).Select(kv => new Vector2Int(kv.Key.Item1, kv.Key.Item2)).ToList();
    }

    // Inverse-distance-to-bone-segment weights, top 4, normalized. Indices are into the sprite's bone subset.
    static BoneWeight Weigh(List<Bone> bones, int[] subset, Vector2 p)
    {
        var ws = new List<(int idx, float w)>();
        for (int k = 0; k < subset.Length; ++k)
        {
            var b = bones[subset[k]];
            float d = DistToSegment(p, b.head, b.tail);
            ws.Add((k, 1f / Mathf.Pow(Mathf.Max(d, 2f), 3f)));
        }
        ws = ws.OrderByDescending(x => x.w).Take(4).ToList();
        float sum = ws.Sum(x => x.w);
        var bw = new BoneWeight();
        for (int i = 0; i < ws.Count; ++i)
        {
            float w = ws[i].w / sum;
            switch (i)
            {
                case 0: bw.boneIndex0 = ws[i].idx; bw.weight0 = w; break;
                case 1: bw.boneIndex1 = ws[i].idx; bw.weight1 = w; break;
                case 2: bw.boneIndex2 = ws[i].idx; bw.weight2 = w; break;
                case 3: bw.boneIndex3 = ws[i].idx; bw.weight3 = w; break;
            }
        }
        return bw;
    }

    static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
        return (p - (a + t * ab)).magnitude;
    }

    // Minimal parser for the "parts": { "name": ["bone", ...], ... } object.
    static Dictionary<string, string[]> ParseParts(string json)
    {
        int start = json.IndexOf("\"parts\"", StringComparison.Ordinal);
        int open = json.IndexOf('{', start), close = json.IndexOf('}', open);
        string body = json.Substring(open + 1, close - open - 1);
        var result = new Dictionary<string, string[]>();
        foreach (var entry in body.Split(']'))
        {
            int colon = entry.IndexOf(':');
            if (colon < 0) continue;
            string key = entry.Substring(0, colon).Trim().Trim(',').Trim().Trim('"');
            string list = entry.Substring(colon + 1).Trim().TrimStart('[');
            result[key] = list.Split(',').Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0).ToArray();
        }
        return result;
    }

    // Prefab that nests the PSB's generated rig (bones + Sprite Skins). Created once; never overwritten.
    static void CreatePrefab()
    {
        if (File.Exists(Path.Combine(ProjectRoot, PrefabPath))) { Debug.Log("[PlayerRigBuilder] Player.prefab exists, left unchanged"); return; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(PsbPath);
        var root = new GameObject("Player");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        var anim = model.AddComponent<Animator>();
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        Debug.Log("[PlayerRigBuilder] Created " + PrefabPath);
    }
}
