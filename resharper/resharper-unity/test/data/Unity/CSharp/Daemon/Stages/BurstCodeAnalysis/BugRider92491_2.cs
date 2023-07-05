using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Unity
{

    namespace Entities
    {
        public abstract partial class ComponentSystemBase
        {
        }

        public struct ForEachLambdaJobDescription
        {
        }

        public abstract partial class SystemBase : ComponentSystemBase
        {
            protected internal ForEachLambdaJobDescription Entities;
            protected abstract void OnUpdate();
        }
        
        public delegate void RI<T0, T1>(ref T0 t0, in T1 t1);

        public static  class LambdaForEachDescriptionConstructionMethods
        {
            public static TDescription ForEach<TDescription, T0, T1>(this TDescription description,
                RI<T0, T1> codeToRun) where TDescription : struct
            {
                return default;
            }

        }
    }

    namespace Burst
    {
        public class BurstCompileAttribute : Attribute
        {
        }

        public class BurstDiscardAttribute : Attribute
        {
        }
    }
}

public partial class ECSSystem : SystemBase
{
    protected override void OnUpdate()
    {
        Entities
            .ForEach(
                codeToRun: (ref int position, in float velocity) =>
                    GeneratePosition(ref position, velocity)
            );
    }

    private static void GeneratePosition(ref int position, in float velocity)
    {
        var l = new List<int>();
    }
}
