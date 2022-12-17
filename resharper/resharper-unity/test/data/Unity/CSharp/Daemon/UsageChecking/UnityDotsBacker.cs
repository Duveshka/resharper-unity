using Unity.Entities;
using UnityEngine;

namespace UnityEngine
{
    public class MonoBehaviour
    {}
}

namespace Unity.Entities
{
    // ReSharper disable once InconsistentNaming
    public abstract class IBaker
    {
    }

    public class Baker<T> : IBaker
    {
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once UnusedParameter.Global
        public virtual void Bake(T authoring)
        {
        }
    }
}

internal class CannonBallAuthoring : MonoBehaviour
{
}

internal class CannonBallBaker : Baker<CannonBallAuthoring>
{
    public override void Bake(CannonBallAuthoring authoring)
    {
    }
}