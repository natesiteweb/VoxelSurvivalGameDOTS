using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;

public class ChunkToEntity : MonoBehaviour, IDeclareReferencedPrefabs, IConvertGameObjectToEntity
{
    public GameObject prefab;
    public static Entity entity;


    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {

        Entity prefabEntity = conversionSystem.GetPrimaryEntity(prefab);
        ChunkToEntity.entity = prefabEntity;
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(prefab);
    }
}
