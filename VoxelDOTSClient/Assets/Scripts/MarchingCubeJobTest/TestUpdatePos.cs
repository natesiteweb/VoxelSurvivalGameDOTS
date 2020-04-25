using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Entities;
using Unity.Transforms;
using Unity.Rendering;

/*public class TestUpdatePos : JobComponentSystem
{
    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        float deltaTime = Time.DeltaTime;

        JobHandle jobHandle = Entities.ForEach((ref Translation translation, ref ChunkComponent chunkComponent) =>
        {
            float yValue = Mathf.PerlinNoise((translation.Value.x + chunkComponent.offset.x) * 0.1f, (translation.Value.z + chunkComponent.offset.y) * 0.1f);

            chunkComponent.offset.x += 2f * deltaTime;
            chunkComponent.offset.y += 2f * deltaTime;

            translation.Value.y = yValue * 10f;

        }).Schedule(inputDeps);

        return jobHandle;
    }
}*/