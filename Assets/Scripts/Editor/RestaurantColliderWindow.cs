using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RestaurantColliderWindow : EditorWindow
{
    private class Candidate
    {
        public string label, id;
        public Bounds bounds;
        public List<Renderer> parts;
        public bool selected, expanded;
        public int priority;
    }

    private Transform root;
    private CapsuleCollider character;
    private readonly List<Candidate> candidates = new();
    private Vector2 scroll;
    private int omitted;
    private bool analyzed;

    [MenuItem("Tools/TrickShot Creator/Restaurant Colliders")]
    private static void Open() => GetWindow<RestaurantColliderWindow>("Restaurant Colliders");

    private void OnEnable()
    {
        var objs = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(o => o.GetComponentsInChildren<Transform>(true)).ToArray();
        root = objs.FirstOrDefault(t => t.name == "TSCRestaurant");
        character = objs.FirstOrDefault(t => t.name == "Charactor Collider")?.GetComponent<CapsuleCollider>();
        SceneView.duringSceneGui += DrawPreview;
        EditorApplication.hierarchyChanged += Invalidate;
        Undo.undoRedoPerformed += Invalidate;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DrawPreview;
        EditorApplication.hierarchyChanged -= Invalidate;
        Undo.undoRedoPerformed -= Invalidate;
        SceneView.RepaintAll();
    }

    private void Invalidate() { analyzed = false; Repaint(); SceneView.RepaintAll(); }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        root = (Transform)EditorGUILayout.ObjectField("Restaurant root",root,typeof(Transform),true);
        character = (CapsuleCollider)EditorGUILayout.ObjectField("Character capsule",character,typeof(CapsuleCollider),true);
        if (EditorGUI.EndChangeCheck()) Invalidate();
        bool valid = root && root.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(root) &&
            character && character.enabled && character.gameObject.activeInHierarchy && character.gameObject.scene == root.gameObject.scene;
        using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button("Analyze / Preview")) Analyze();
        if (!valid || !analyzed) return;
        EditorGUILayout.HelpBox($"{candidates.Count} candidates, {omitted} details excluded. Green: walking height. Amber: review. Existing boxes keep their Inspector edits.",MessageType.Info);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var c in candidates)
        {
            EditorGUILayout.BeginHorizontal();
            bool old = c.selected;
            c.selected = EditorGUILayout.Toggle(c.selected,GUILayout.Width(20));
            if (old != c.selected) SceneView.RepaintAll();
            c.expanded = EditorGUILayout.Foldout(c.expanded,$"{c.label}  ({c.parts.Count})",true);
            EditorGUILayout.EndHorizontal();
            if (c.expanded)
                foreach (var part in c.parts) EditorGUILayout.ObjectField(part,typeof(Renderer),true);
        }
        EditorGUILayout.EndScrollView();
        using (new EditorGUI.DisabledScope(!candidates.Any(c => c.selected)))
            if (GUILayout.Button("Generate Selected Boxes")) Generate();
    }

    // The imported counter has separate Front and Return runs. Their union would close the aisle.
    private static string Family(string name)
    {
        var m = Regex.Match(name, @"^Counter (Front|Return) (Body|Fluting|Plinth|Stone)\b",RegexOptions.IgnoreCase);
        if (m.Success) return "Counter " + m.Groups[1].Value;
        if (Regex.IsMatch(name,@"^(Wall |Floor Slab$|Entrance Door$|Door Jamb\b)",RegexOptions.IgnoreCase))
            return name;
        return null;
    }

    private void Analyze()
    {
        candidates.Clear(); omitted = 0;
        var groups = new Dictionary<string,List<Renderer>>();
        var generated = root.Find("Colliders");
        foreach (Renderer part in root.GetComponentsInChildren<Renderer>(false))
        {
            if ((!(part is MeshRenderer) && !(part is SkinnedMeshRenderer)) || !part.enabled ||
                (generated && part.transform.IsChildOf(generated))) continue;
            string n = part.name;
            if (Regex.IsMatch(n,@"\b(LED|Menu|Ceiling|Spotlight|Track|Lens|Wash|Backsplash|Boards|Cap|Transom|Lintel|Crossbar|Pull|Floor Tile Field|Light|Decor)\b", RegexOptions.IgnoreCase))
            { omitted++; continue; }
            string family = Family(n);
            if (family == null) { Add(n,new List<Renderer>{part},false); continue; }
            if (!groups.TryGetValue(family,out var list)) groups.Add(family,list=new List<Renderer>());
            list.Add(part);
        }
        foreach (var pair in groups)
        {
            var remaining = pair.Value;
            while (remaining.Count > 0)
            {
                var connected = new List<Renderer>{remaining[0]};
                remaining.RemoveAt(0);
                for (int i=0;i<connected.Count;i++)
                {
                    Bounds near = connected[i].bounds;
                    near.Expand(.04f);
                    for (int j=remaining.Count-1;j>=0;j--)
                        if (near.Intersects(remaining[j].bounds))
                        { connected.Add(remaining[j]); remaining.RemoveAt(j); }
                }
                Add(pair.Key,connected,true);
            }
        }
        candidates.Sort((a,b)=> a.priority != b.priority ? a.priority.CompareTo(b.priority) : string.CompareOrdinal(a.id,b.id));
        analyzed = true; SceneView.RepaintAll();
    }

    private void Add(string label,List<Renderer> parts,bool known)
    {
        Bounds world = parts[0].bounds;
        foreach (var part in parts) world.Encapsulate(part.bounds);
        Bounds capsule = character.bounds;
        bool reachable = world.min.y <= capsule.max.y && world.max.y >= capsule.min.y - .15f;
        var anchor = parts.OrderBy(p=>p.name.Contains("Body") ? 0 : 1).ThenBy(p=>p.name,System.StringComparer.Ordinal).First();
        string path = GlobalObjectId.GetGlobalObjectIdSlow(anchor).ToString();
        candidates.Add(new Candidate {label=label,id=label+" ["+Hash128.Compute(path).ToString().Substring(0,12)+"]",
            parts=parts,bounds=LocalBounds(parts),priority=known && reachable ? 0 : 1,selected=known && reachable});
    }

    private Bounds LocalBounds(List<Renderer> parts)
    {
        Bounds result = default; bool first = true;
        foreach (var part in parts)
        {
            // Transform source bounds directly to restaurant space to avoid enlarging rotated world AABBs.
            Bounds b = part.localBounds;
            Matrix4x4 matrix = root.worldToLocalMatrix * part.localToWorldMatrix;
            for (int i=0;i<8;i++)
            {
                Vector3 corner = new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z);
                Vector3 p=matrix.MultiplyPoint3x4(corner);
                if (first){result=new Bounds(p,Vector3.zero);first=false;} else result.Encapsulate(p);
            }
        }
        return result;
    }

    private void DrawPreview(SceneView view)
    {
        if (!analyzed || !root) return;
        foreach (var c in candidates)
            if (c.selected)
                using (new Handles.DrawingScope(c.priority==0?Color.green:Color.yellow,root.localToWorldMatrix))
                    Handles.DrawWireCube(c.bounds.center,c.bounds.size);
    }

    private void Generate()
    {
        Transform holder=root.Find("Colliders");
        if (holder && (holder.localPosition != Vector3.zero || Quaternion.Angle(holder.localRotation,Quaternion.identity) > .001f || holder.localScale != Vector3.one))
        {
            EditorUtility.DisplayDialog("Restaurant Colliders","The Colliders parent must have an identity local transform. Review its transform before generating additional boxes.","OK");
            return;
        }
        Undo.IncrementCurrentGroup();
        int group=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Generate restaurant colliders");
        if (!holder) holder=Create("Colliders",root);
        foreach (var c in candidates)
        {
            if (!c.selected || holder.Find(c.id)) continue;
            Transform t=Create(c.id,holder);
                t.localPosition=c.bounds.center;
            var box=Undo.AddComponent<BoxCollider>(t.gameObject);
            box.size=c.bounds.size;
            box.isTrigger=false;
        }
        Undo.CollapseUndoOperations(group);
        Invalidate();
    }

    private static Transform Create(string name,Transform parent)
    {
        var obj=new GameObject(name);
        obj.layer=parent.gameObject.layer;
        obj.transform.SetParent(parent,false);
        Undo.RegisterCreatedObjectUndo(obj,"Create restaurant collider");
        return obj.transform;
    }
}
