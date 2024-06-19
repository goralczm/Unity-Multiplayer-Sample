using Unity.Entities;
using UnityEngine;

namespace MultiplayerSample
{
    public struct Cube : IComponentData
    {

    }

    [DisallowMultipleComponent]
    public class CubeAuthoring : MonoBehaviour
    {
        class Baker : Baker<CubeAuthoring>
        {
            public override void Bake(CubeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<Cube>(entity);
            }
        }
    }
}
