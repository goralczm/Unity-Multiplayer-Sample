using Unity.Entities;
using UnityEngine;

namespace MultiplayerSample
{
    public struct CubeSpawner : IComponentData
    {
        public Entity Cube;
    }

    [DisallowMultipleComponent]
    public class CubeSpawnerAuthoring : MonoBehaviour
    {
        public GameObject Cube;

        class Baker : Baker<CubeSpawnerAuthoring>
        {
            public override void Bake(CubeSpawnerAuthoring authoring)
            {
                CubeSpawner component = default(CubeSpawner);
                component.Cube = GetEntity(authoring.Cube, TransformUsageFlags.Dynamic);
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, component);
            }
        }
    }
}
