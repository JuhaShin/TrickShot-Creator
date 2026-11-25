using UnityEngine;

// 에디터에서 만든 설치 판정용 작은 콜라이더들의 위치와 크기.
public class PlacementShape : ScriptableObject
{
    public GameObject sourcePrefab;
    public Mesh sourceMesh;
    public Bounds[] parts;
}
