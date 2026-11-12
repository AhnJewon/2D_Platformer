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

    // Moves a joint (the HEAD of boneName) to canvasX/canvasY (pixels, y down) like Skinning Editor "Edit Joints":
    // the bone's tail and all children stay where they are in the bind pose (so e.g. moving a hip joint keeps
    // the knee in place); the bone is re-aimed at its old tail. Sprite-space bone data of every sprite that uses
    // the bone or its direct children is rebuilt from the skeleton. Meshes and weights are not changed.
    public static string MoveJoint(string psbPath, string boneName, float canvasX, float canvasY)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var charProvider = dp.GetDataProvider<ICharacterDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        CharacterData cd = charProvider.GetCharacterData();
        var cb = cd.bones;
        int canvasH = cd.dimension.y;

        Vector2[] wp; float[] wr;
        void World()
        {
            wp = new Vector2[cb.Length]; wr = new float[cb.Length];
            for (int i = 0; i < cb.Length; ++i)
            {
                float lr = cb[i].rotation.eulerAngles.z;
                if (cb[i].parentId < 0) { wp[i] = cb[i].position; wr[i] = lr; }
                else { int p = cb[i].parentId; wp[i] = wp[p] + (Vector2)(Quaternion.Euler(0, 0, wr[p]) * (Vector2)cb[i].position); wr[i] = wr[p] + lr; }
            }
        }
        World();
        int bi = Array.FindIndex(cb, b => b.name == boneName);
        if (bi < 0) return "bone not found: " + boneName;
        var oldHead = wp[bi];
        var tail = wp[bi] + (Vector2)(Quaternion.Euler(0, 0, wr[bi]) * new Vector2(cb[bi].length, 0));
        var newHead = new Vector2(canvasX, canvasH - canvasY);
        var children = Enumerable.Range(0, cb.Length).Where(i => cb[i].parentId == bi).ToArray();
        var childW = children.ToDictionary(c => c, c => (wp[c], wr[c]));

        float newWorldRot = Mathf.Atan2(tail.y - newHead.y, tail.x - newHead.x) * Mathf.Rad2Deg;
        var b0 = cb[bi];
        if (b0.parentId < 0) { b0.position = new Vector3(newHead.x, newHead.y, b0.position.z); b0.rotation = Quaternion.Euler(0, 0, newWorldRot); }
        else
        {
            int p = b0.parentId;
            Vector2 lp = Quaternion.Euler(0, 0, -wr[p]) * (newHead - wp[p]);
            b0.position = new Vector3(lp.x, lp.y, b0.position.z);
            b0.rotation = Quaternion.Euler(0, 0, newWorldRot - wr[p]);
        }
        b0.length = (tail - newHead).magnitude;
        cb[bi] = b0;
        foreach (int c in children) // keep children where they were
        {
            var (cpos, crot) = childW[c];
            var bc = cb[c];
            Vector2 lp = Quaternion.Euler(0, 0, -newWorldRot) * (cpos - newHead);
            bc.position = new Vector3(lp.x, lp.y, bc.position.z);
            bc.rotation = Quaternion.Euler(0, 0, crot - newWorldRot);
            cb[c] = bc;
        }
        cd.bones = cb;
        World();

        var touched = new HashSet<int>(children) { bi };
        var rects = dp.GetSpriteRects();
        var log = new List<string> { $"{boneName}: head ({oldHead.x:F0},{canvasH - oldHead.y:F0}) -> ({canvasX:F0},{canvasY:F0}), length {b0.length:F0}" };
        foreach (var part in cd.parts)
        {
            if (part.bones == null || !part.bones.Any(touched.Contains)) continue;
            var rect = rects.FirstOrDefault(r => r.spriteID.ToString() == part.spriteId);
            if (rect == null) continue;
            var old = boneProvider.GetBones(rect.spriteID);
            var sbs = new List<SpriteBone>();
            for (int k = 0; k < part.bones.Length; ++k)
            {
                int gi = part.bones[k];
                var b = cb[gi];
                int lp = b.parentId >= 0 ? Array.IndexOf(part.bones, b.parentId) : -1;
                if (lp < 0) { b.position = new Vector3(wp[gi].x - part.spritePosition.x, wp[gi].y - part.spritePosition.y, b.position.z); b.rotation = Quaternion.Euler(0, 0, wr[gi]); }
                b.parentId = lp;
                if (k < old.Count) b.guid = old[k].guid;
                sbs.Add(b);
            }
            boneProvider.SetBones(rect.spriteID, sbs);
            log.Add("  sprite bones updated: " + rect.name);
        }
        charProvider.SetCharacterData(cd);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return string.Join("\n", log);
    }

    // Adds a toe bone (child of parentBone) to a boot and re-skins that boot: everything stays 100% on parentBone
    // except the toe cap, i.e. vertices in front of the ball line (x > ballX) and below the instep (y > instepY),
    // which blend onto the toe bone over +-blend pixels. Canvas pixels, y down. The boot mesh is rebuilt finer
    // (8 px grid) so the bend is smooth. Other sprites and bones are untouched. If the toe bone already exists,
    // only the boot's weights/mesh are redone.
    public static string AddToeBone(string psbPath, string bootSprite, string parentBone, string toeName,
        float headX, float headY, float tailX, float tailY, float ballX, float instepY, float blend)
    {
        var layersFile = JsonUtility.FromJson<LayersFile>(File.ReadAllText(Path.Combine(ProjectRoot, "ArtSource/layers/layers.json")));
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var charProvider = dp.GetDataProvider<ICharacterDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        CharacterData cd = charProvider.GetCharacterData();
        int H = cd.dimension.y;
        var cb = cd.bones.ToList();

        Vector2[] wp = null; float[] wr = null;
        void World()
        {
            wp = new Vector2[cb.Count]; wr = new float[cb.Count];
            for (int i = 0; i < cb.Count; ++i)
            {
                float lr = cb[i].rotation.eulerAngles.z;
                if (cb[i].parentId < 0) { wp[i] = cb[i].position; wr[i] = lr; }
                else { int p = cb[i].parentId; wp[i] = wp[p] + (Vector2)(Quaternion.Euler(0, 0, wr[p]) * (Vector2)cb[i].position); wr[i] = wr[p] + lr; }
            }
        }
        World();
        int pi = cb.FindIndex(b => b.name == parentBone);
        if (pi < 0) return "parent bone not found: " + parentBone;
        int ti = cb.FindIndex(b => b.name == toeName);
        var head = new Vector2(headX, H - headY); var tail = new Vector2(tailX, H - tailY);
        float wrot = Mathf.Atan2(tail.y - head.y, tail.x - head.x) * Mathf.Rad2Deg;
        Vector2 lpos = Quaternion.Euler(0, 0, -wr[pi]) * (head - wp[pi]);
        var toe = new SpriteBone
        {
            name = toeName, guid = ti >= 0 ? cb[ti].guid : GUID.Generate().ToString(), parentId = pi,
            position = new Vector3(lpos.x, lpos.y, 0), rotation = Quaternion.Euler(0, 0, wrot - wr[pi]),
            length = (tail - head).magnitude, color = new Color(1f, 0.6f, 0.1f)
        };
        if (ti >= 0) cb[ti] = toe; else { cb.Add(toe); ti = cb.Count - 1; }
        cd.bones = cb.ToArray();
        World();

        var rect = dp.GetSpriteRects().First(r => r.name == bootSprite);
        int partIdx = Array.FindIndex(cd.parts, x => x.spriteId == rect.spriteID.ToString());
        var part = cd.parts[partIdx];
        part.bones = new[] { pi, ti };
        cd.parts[partIdx] = part;
        Vector2 partPos = part.spritePosition.position;
        var sb = new List<SpriteBone>();
        var b0 = cb[pi]; b0.position = new Vector3(wp[pi].x - partPos.x, wp[pi].y - partPos.y, b0.position.z); b0.rotation = Quaternion.Euler(0, 0, wr[pi]); b0.parentId = -1; sb.Add(b0);
        var b1 = cb[ti]; b1.parentId = 0; sb.Add(b1);
        boneProvider.SetBones(rect.spriteID, sb);

        var layer = layersFile.layers.First(l => l.name == bootSprite + ".png");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(Path.Combine(ProjectRoot, "ArtSource/layers", layer.file)));
        BuildGridMesh(tex, H, partPos, (int)rect.rect.width, (int)rect.rect.height, out var verts, out var indices, out var edges, 8);
        UnityEngine.Object.DestroyImmediate(tex);
        float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        var vmeta = new Vertex2DMetaData[verts.Count];
        int toeVerts = 0;
        for (int v = 0; v < verts.Count; ++v)
        {
            float cx = verts[v].x + partPos.x, cy = H - (verts[v].y + partPos.y);
            float w = Smooth(ballX - blend, ballX + blend, cx) * Smooth(instepY - blend, instepY + blend, cy);
            if (w > 0.5f) toeVerts++;
            vmeta[v].position = verts[v];
            vmeta[v].boneWeight = new BoneWeight { boneIndex0 = 0, weight0 = 1f - w, boneIndex1 = 1, weight1 = w };
        }
        meshProvider.SetVertices(rect.spriteID, vmeta);
        meshProvider.SetIndices(rect.spriteID, indices.ToArray());
        meshProvider.SetEdges(rect.spriteID, edges.ToArray());
        charProvider.SetCharacterData(cd);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return $"{bootSprite}: toe bone {toeName} ({headX},{headY})->({tailX},{tailY}) under {parentBone}; {verts.Count} verts, {toeVerts} mostly on the toe";
    }

    // Lets a skirt react to the legs: the skirt sprite gets the thigh bones added, and below startY its vertices
    // hand a growing share of their weight (0 at startY .. maxShare at endY, canvas y down) to the nearest thigh(s),
    // so the hem lifts and swings when a thigh rises. Existing weights (yours) keep the rest of the share.
    public static string SkirtFollowThighs(string psbPath, string skirtSprite, string[] thighBones, float startY, float endY, float maxShare)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var charProvider = dp.GetDataProvider<ICharacterDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        CharacterData cd = charProvider.GetCharacterData();
        var cb = cd.bones; int H = cd.dimension.y;
        var wp = new Vector2[cb.Length]; var wr = new float[cb.Length];
        for (int i = 0; i < cb.Length; ++i)
        {
            float lr = cb[i].rotation.eulerAngles.z;
            if (cb[i].parentId < 0) { wp[i] = cb[i].position; wr[i] = lr; }
            else { int p = cb[i].parentId; wp[i] = wp[p] + (Vector2)(Quaternion.Euler(0, 0, wr[p]) * (Vector2)cb[i].position); wr[i] = wr[p] + lr; }
        }
        var rect = dp.GetSpriteRects().First(r => r.name == skirtSprite);
        int partIdx = Array.FindIndex(cd.parts, x => x.spriteId == rect.spriteID.ToString());
        var part = cd.parts[partIdx];
        var oldSubset = part.bones.ToArray();
        var thighIdx = thighBones.Select(n => Array.FindIndex(cb, b => b.name == n)).ToArray();
        if (thighIdx.Any(i => i < 0)) return "thigh bone not found";
        var subset = oldSubset.Union(thighIdx).OrderBy(i => i).ToArray();
        int Local(int global) => Array.IndexOf(subset, global);
        part.bones = subset; cd.parts[partIdx] = part;
        Vector2 partPos = part.spritePosition.position;

        var sbs = new List<SpriteBone>();
        var oldBones = boneProvider.GetBones(rect.spriteID);
        foreach (int gi in subset)
        {
            var b = cb[gi];
            int lp = b.parentId >= 0 ? Array.IndexOf(subset, b.parentId) : -1;
            if (lp < 0) { b.position = new Vector3(wp[gi].x - partPos.x, wp[gi].y - partPos.y, b.position.z); b.rotation = Quaternion.Euler(0, 0, wr[gi]); }
            b.parentId = lp;
            int oi = Array.IndexOf(oldSubset, gi); if (oi >= 0 && oi < oldBones.Count) b.guid = oldBones[oi].guid;
            sbs.Add(b);
        }
        boneProvider.SetBones(rect.spriteID, sbs);

        var segs = thighIdx.Select(gi => (a: wp[gi], b: wp[gi] + (Vector2)(Quaternion.Euler(0, 0, wr[gi]) * new Vector2(cb[gi].length, 0)))).ToArray();
        var verts = meshProvider.GetVertices(rect.spriteID);
        int touched = 0;
        for (int v = 0; v < verts.Length; ++v)
        {
            var w = new Dictionary<int, float>();
            var bw = verts[v].boneWeight;
            void Add(int i, float x) { if (x <= 0f) return; w[i] = (w.TryGetValue(i, out var o) ? o : 0f) + x; }
            Add(Local(oldSubset[bw.boneIndex0]), bw.weight0); Add(Local(oldSubset[bw.boneIndex1]), bw.weight1);
            Add(Local(oldSubset[bw.boneIndex2]), bw.weight2); Add(Local(oldSubset[bw.boneIndex3]), bw.weight3);
            Vector2 p = verts[v].position + partPos;
            float cy = H - p.y;
            float t = Mathf.Clamp01((cy - startY) / (endY - startY)); t = t * t * (3f - 2f * t);
            float share = maxShare * t;
            if (share > 0f)
            {
                foreach (var k in w.Keys.ToList()) w[k] *= 1f - share;
                var inv = segs.Select(s => 1f / Mathf.Pow(Mathf.Max(DistToSegment(p, s.a, s.b), 4f), 2f)).ToArray();
                float sum = inv.Sum();
                for (int k = 0; k < thighIdx.Length; ++k) Add(Local(thighIdx[k]), share * inv[k] / sum);
                touched++;
            }
            var top = w.OrderByDescending(kv => kv.Value).Take(4).ToList();
            float tot = top.Sum(kv => kv.Value);
            var nb = new BoneWeight();
            for (int k = 0; k < top.Count; ++k)
            {
                float x = top[k].Value / tot;
                if (k == 0) { nb.boneIndex0 = top[k].Key; nb.weight0 = x; } else if (k == 1) { nb.boneIndex1 = top[k].Key; nb.weight1 = x; }
                else if (k == 2) { nb.boneIndex2 = top[k].Key; nb.weight2 = x; } else { nb.boneIndex3 = top[k].Key; nb.weight3 = x; }
            }
            verts[v].boneWeight = nb;
        }
        meshProvider.SetVertices(rect.spriteID, verts);
        charProvider.SetCharacterData(cd);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return $"{skirtSprite}: bones [{string.Join(",", subset.Select(i => cb[i].name))}], {touched}/{verts.Length} vertices share up to {maxShare:P0} with the thighs (y {startY}..{endY})";
    }

    // Adds an ankle: a foot bone (child of shinBone, head at the ankle, tail at the ball of the foot) and moves the
    // existing toe bone under it (toe keeps its place). The boot is re-weighted in three zones: shaft above the
    // ankle crease -> shin, heel/sole below it -> foot, toe cap (x > ballX, y > instepY) -> toe; each border blends
    // over +-blend px. Canvas pixels, y down. Other sprites are untouched. Safe to run again (updates in place).
    public static string AddAnkle(string psbPath, string bootSprite, string shinBone, string footName, string toeName,
        float ankleX, float ankleY, float creaseY, float ballX, float instepY, float blend)
    {
        var layersFile = JsonUtility.FromJson<LayersFile>(File.ReadAllText(Path.Combine(ProjectRoot, "ArtSource/layers/layers.json")));
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(AssetImporter.GetAtPath(psbPath));
        dp.InitSpriteEditorDataProvider();
        var charProvider = dp.GetDataProvider<ICharacterDataProvider>();
        var boneProvider = dp.GetDataProvider<ISpriteBoneDataProvider>();
        var meshProvider = dp.GetDataProvider<ISpriteMeshDataProvider>();
        CharacterData cd = charProvider.GetCharacterData();
        int H = cd.dimension.y;
        var cb = cd.bones.ToList();
        Vector2[] wp = null; float[] wr = null;
        void World()
        {
            wp = new Vector2[cb.Count]; wr = new float[cb.Count];
            for (int i = 0; i < cb.Count; ++i)
            {
                float lr = cb[i].rotation.eulerAngles.z;
                if (cb[i].parentId < 0) { wp[i] = cb[i].position; wr[i] = lr; }
                else { int p = cb[i].parentId; wp[i] = wp[p] + (Vector2)(Quaternion.Euler(0, 0, wr[p]) * (Vector2)cb[i].position); wr[i] = wr[p] + lr; }
            }
        }
        World();
        int si = cb.FindIndex(b => b.name == shinBone), ti = cb.FindIndex(b => b.name == toeName);
        if (si < 0 || ti < 0) return "shin or toe bone not found";
        var toeW = (pos: wp[ti], rot: wr[ti]);
        var ankle = new Vector2(ankleX, H - ankleY);
        var ball = toeW.pos;
        float frot = Mathf.Atan2(ball.y - ankle.y, ball.x - ankle.x) * Mathf.Rad2Deg;
        int fi = cb.FindIndex(b => b.name == footName);
        Vector2 flp = Quaternion.Euler(0, 0, -wr[si]) * (ankle - wp[si]);
        var foot = new SpriteBone
        {
            name = footName, guid = fi >= 0 ? cb[fi].guid : GUID.Generate().ToString(), parentId = si,
            position = new Vector3(flp.x, flp.y, 0), rotation = Quaternion.Euler(0, 0, frot - wr[si]),
            length = (ball - ankle).magnitude, color = new Color(0.9f, 0.4f, 0.1f)
        };
        if (fi >= 0) cb[fi] = foot;
        else if (fi < 0)
        {
            // insert before the toe so parents still come before children
            cb.Insert(ti, foot); fi = ti; ti++;
            for (int i = 0; i < cb.Count; ++i)
                if (i != fi && cb[i].parentId >= fi) { var b = cb[i]; b.parentId++; cb[i] = b; }
            foreach (var pidx in Enumerable.Range(0, cd.parts.Length))
            {
                var pt = cd.parts[pidx];
                if (pt.bones != null) { pt.bones = pt.bones.Select(x => x >= fi ? x + 1 : x).ToArray(); cd.parts[pidx] = pt; }
            }
        }
        // toe now hangs from the foot, keeping its world placement
        var toe = cb[ti];
        Vector2 tlp = Quaternion.Euler(0, 0, -frot) * (toeW.pos - ankle);
        toe.parentId = fi; toe.position = new Vector3(tlp.x, tlp.y, toe.position.z); toe.rotation = Quaternion.Euler(0, 0, toeW.rot - frot);
        cb[ti] = toe;
        cd.bones = cb.ToArray();
        World();

        var rect = dp.GetSpriteRects().First(r => r.name == bootSprite);
        int partIdx = Array.FindIndex(cd.parts, x => x.spriteId == rect.spriteID.ToString());
        var part = cd.parts[partIdx];
        part.bones = new[] { si, fi, ti };
        cd.parts[partIdx] = part;
        Vector2 partPos = part.spritePosition.position;
        var sbs = new List<SpriteBone>();
        var b0 = cb[si]; b0.position = new Vector3(wp[si].x - partPos.x, wp[si].y - partPos.y, b0.position.z); b0.rotation = Quaternion.Euler(0, 0, wr[si]); b0.parentId = -1; sbs.Add(b0);
        var b1 = cb[fi]; b1.parentId = 0; sbs.Add(b1);
        var b2 = cb[ti]; b2.parentId = 1; sbs.Add(b2);
        boneProvider.SetBones(rect.spriteID, sbs);

        // the bone data changed for every sprite whose bones were renumbered: rebuild their sprite bones too
        var rects = dp.GetSpriteRects();
        foreach (var pt in cd.parts)
        {
            if (pt.spriteId == part.spriteId || pt.bones == null || pt.bones.Length == 0) continue;
            var r = rects.FirstOrDefault(x => x.spriteID.ToString() == pt.spriteId);
            if (r == null) continue;
            var old = boneProvider.GetBones(r.spriteID);
            var list = new List<SpriteBone>();
            for (int k = 0; k < pt.bones.Length; ++k)
            {
                int gi = pt.bones[k]; var b = cb[gi];
                int lp = b.parentId >= 0 ? Array.IndexOf(pt.bones, b.parentId) : -1;
                if (lp < 0) { b.position = new Vector3(wp[gi].x - pt.spritePosition.x, wp[gi].y - pt.spritePosition.y, b.position.z); b.rotation = Quaternion.Euler(0, 0, wr[gi]); }
                b.parentId = lp;
                if (k < old.Count) b.guid = old[k].guid;
                list.Add(b);
            }
            boneProvider.SetBones(r.spriteID, list);
        }

        var layer = layersFile.layers.First(l => l.name == bootSprite + ".png");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(Path.Combine(ProjectRoot, "ArtSource/layers", layer.file)));
        BuildGridMesh(tex, H, partPos, (int)rect.rect.width, (int)rect.rect.height, out var verts, out var indices, out var edges, 8);
        UnityEngine.Object.DestroyImmediate(tex);
        float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        var vmeta = new Vertex2DMetaData[verts.Count];
        int nFoot = 0, nToe = 0;
        for (int v = 0; v < verts.Count; ++v)
        {
            float cx = verts[v].x + partPos.x, cy = H - (verts[v].y + partPos.y);
            float wToe = Smooth(ballX - blend, ballX + blend, cx) * Smooth(instepY - blend, instepY + blend, cy);
            float wFoot = (1f - wToe) * Smooth(creaseY - blend, creaseY + blend, cy);
            float wShin = 1f - wToe - wFoot;
            if (wFoot > 0.5f) nFoot++; if (wToe > 0.5f) nToe++;
            vmeta[v].position = verts[v];
            vmeta[v].boneWeight = new BoneWeight { boneIndex0 = 0, weight0 = wShin, boneIndex1 = 1, weight1 = wFoot, boneIndex2 = 2, weight2 = wToe };
        }
        meshProvider.SetVertices(rect.spriteID, vmeta);
        meshProvider.SetIndices(rect.spriteID, indices.ToArray());
        meshProvider.SetEdges(rect.spriteID, edges.ToArray());
        charProvider.SetCharacterData(cd);
        dp.Apply();
        AssetDatabase.ImportAsset(psbPath, ImportAssetOptions.ForceUpdate);
        return $"{bootSprite}: {footName} ankle ({ankleX},{ankleY}) -> ball; {toeName} reparented; {verts.Count} verts: {nFoot} foot, {nToe} toe";
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
        out List<Vector2> verts, out List<int> indices, out List<Vector2Int> edges, int cellOverride = 0)
    {
        int cell = cellOverride > 0 ? cellOverride : Mathf.Max(w, h) > 400 ? 20 : Mathf.Max(w, h) > 150 ? 14 : 8;
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
