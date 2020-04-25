using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Rendering;

public class EntitySpawnerSystem : ComponentSystem
{
    private float spawnTimer;
    private Random random;

    private float2 noiseOffset;

    protected override void OnCreate()
    {
        random = new Random(56);
        noiseOffset = float2.zero;
    }

    protected override void OnStartRunning()
    {
        /*NativeArray<Entity> entityArray = new NativeArray<Entity>(10, Allocator.Temp);
        //entityManager.CreateEntity(chunkArchetype, entityArray);
        EntityManager.Instantiate(ChunkToEntity.entity, entityArray);

        for (int i = 0; i < entityArray.Length; i++)
        {
            Entity entity = entityArray[i];
            EntityManager.SetComponentData(entity, new Translation { Value = new float3(random.NextFloat(-30f, 30f), random.NextFloat(-20f, 20f), 20f) });
            EntityManager.SetComponentData(entity, new Scale { Value = 1f });
            EntityManager.SetComponentData(entity, new ChunkComponent { tempMoveSpeed = 10f }); 

            if (i > 50)
            {
                EntityManager.SetSharedComponentData(entity, new RenderMesh
                {
                    mesh = mesh,
                    material = material
                });
            }
            else
            {
                EntityManager.SetSharedComponentData(entity, new RenderMesh
                {
                    mesh = mesh2,
                    material = material
                });
            }
        }

        entityArray.Dispose();*/
    }

    protected override void OnUpdate()
    {
        /*NativeArray<float3> positions = new NativeArray<float3>(10000, Allocator.TempJob);
        NativeArray<float> moveSpeeds = new NativeArray<float>(10000, Allocator.TempJob);

        int index = 0;

        Entities.ForEach((ref Translation translation, ref ChunkComponent chunkComponent) =>
        {
            positions[index] = translation.Value;
            moveSpeeds[index] = chunkComponent.tempMoveSpeed;
            index++;
        });

        noiseOffset.x += Time.DeltaTime * 2f;
        noiseOffset.y += Time.DeltaTime * 2f;

        MCTestJobMove testJob = new MCTestJobMove
        {
            //vertices = verts,
            //triangles = tris
            offset = noiseOffset,
            deltaTime = Time.DeltaTime,
            moveSpeeds = moveSpeeds,
            positions = positions
        };

        JobHandle jobHandle = testJob.Schedule(10000, 1000);
        jobHandle.Complete();

        index = 0;

        Entities.ForEach((ref Translation translation, ref ChunkComponent chunkComponent) =>
        {
            translation.Value = positions[index];
            chunkComponent.tempMoveSpeed = moveSpeeds[index];
            index++;
        });

        positions.Dispose();
        moveSpeeds.Dispose();*/

        /*Entities.ForEach((ref Translation translation, ref ChunkComponent chunkComponent) =>
        {
            if (translation.Value.x < -30f)
                chunkComponent.tempMoveSpeed = UnityEngine.Mathf.Abs(chunkComponent.tempMoveSpeed);
            else if (translation.Value.x > 30f)
                chunkComponent.tempMoveSpeed = -UnityEngine.Mathf.Abs(chunkComponent.tempMoveSpeed);

            //translation.Value.x += chunkComponent.tempMoveSpeed * deltaTime;
        });*/
    }
}
