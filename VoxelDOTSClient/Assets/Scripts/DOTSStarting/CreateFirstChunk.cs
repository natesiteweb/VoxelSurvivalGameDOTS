using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Physics;
using System;

public class CreateFirstChunk : MonoBehaviour
{
    public GameObject chunkPrefab;

    EntityManager entityManager;
    Entity chunkEntityPrefab;

    [SerializeField]
    private Mesh mesh;
    [SerializeField]
    private Mesh mesh2;
    [SerializeField]
    private UnityEngine.Material material;

    [SerializeField]
    private Unity.Physics.Material physicsMaterial;

    protected Entity stepper;
    public SimulationType StepType = SimulationType.UnityPhysics;

    void Start()
    {
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(56);

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        physicsMaterial = new Unity.Physics.Material();
        physicsMaterial.Restitution = .3f;
        physicsMaterial.Friction = .5f;

        ComponentType[] componentTypes = {
            typeof(PhysicsStep)
        };
        stepper = entityManager.CreateEntity(componentTypes);
        entityManager.SetComponentData(stepper, new PhysicsStep
        {
            SimulationType = StepType,
            Gravity = new float3(0f, -9.81f, 0f),
            SolverIterationCount = PhysicsStep.Default.SolverIterationCount,
            ThreadCountHint = PhysicsStep.Default.ThreadCountHint
        });

        EntityArchetype chunkArchetype = entityManager.CreateArchetype(
            typeof(Translation),
            typeof(Rotation),
            typeof(Scale),
            typeof(RenderMesh),
            typeof(WorldRenderBounds),
            typeof(RenderBounds),
            typeof(LocalToWorld),

            typeof(ChunkComponent),

            typeof(PhysicsVelocity),
            typeof(PhysicsCollider),
            typeof(PhysicsMass),
            typeof(PhysicsDamping)
        );

        NativeArray<Entity> entityArray = new NativeArray<Entity>(1000, Allocator.Temp);
        entityManager.CreateEntity(chunkArchetype, entityArray);
        //entityManager.Instantiate(chunkPrefab, entityArray);

        for(int i = 0; i < entityArray.Length; i++)
        {
            Entity entity = entityArray[i];

            entityManager.AddComponentData(entity, new LocalToWorld { });
            entityManager.AddComponentData(entity, new Rotation { Value = quaternion.identity });
            entityManager.SetComponentData(entity, new Translation { Value = new float3(random.NextFloat(-30f, 30f), random.NextFloat(-20f, 20f), random.NextFloat(-20f, 20f)) });
            entityManager.SetComponentData(entity, new Scale { Value = 1f });
            entityManager.SetComponentData(entity, new ChunkComponent { tempMoveSpeed = 10f });

            entityManager.AddComponentData(entity, new RenderBounds { Value = mesh.bounds.ToAABB() });

            /*BlobAssetReference<Unity.Physics.Collider> collider = Unity.Physics.SphereCollider.Create(new SphereGeometry
            {
                Center = float3.zero,
                Radius = 1f
            });*/

            NativeArray<float3> points = new NativeArray<float3>(mesh.vertices.Length, Allocator.TempJob);

            for (int j = 0; j < mesh.vertices.Length; j++)
            {
                points[j] = mesh.vertices[j];
            }

            BlobAssetReference<Unity.Physics.Collider> collider = ConvexCollider.Create(
                points, ConvexHullGenerationParameters.Default, CollisionFilter.Default, physicsMaterial);

            points.Dispose();

            var colliderComponent = new PhysicsCollider { Value = collider };
            var physicsMassComponent = PhysicsMass.CreateDynamic(colliderComponent.MassProperties, 1f);

            
            entityManager.AddComponentData(entity, colliderComponent);
            entityManager.AddComponentData(entity, physicsMassComponent);

            float3 angularVelocityLocal = math.mul(math.inverse(colliderComponent.MassProperties.MassDistribution.Transform.rot), 0f);
            entityManager.AddComponentData(entity, new PhysicsVelocity()
            {
                Linear = 0f,
                Angular = angularVelocityLocal
            });
            entityManager.AddComponentData(entity, new PhysicsDamping()
            {
                Linear = 0.01f,
                Angular = 0.05f
            });

            if (i > 50)
            {
                entityManager.SetSharedComponentData(entity, new RenderMesh
                {
                    mesh = mesh,
                    material = material
                });
            }
            else
            {
                entityManager.SetSharedComponentData(entity, new RenderMesh
                {
                    mesh = mesh,
                    material = material
                });
            }
        }

        entityArray.Dispose();
    }

    void Update()
    {
        
    }
}
