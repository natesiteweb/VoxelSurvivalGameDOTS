using Unity.Entities;
using UnityEngine;

[GenerateAuthoringComponent]
public struct PrefabEntityComponent : IComponentData
{
    public Entity prefabEntity; 
}
