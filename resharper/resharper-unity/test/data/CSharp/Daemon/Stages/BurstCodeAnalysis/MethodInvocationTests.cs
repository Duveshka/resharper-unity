using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.UnityEngine;

namespace Unity
{
    namespace Jobs
    {
        [JobProducerType]
        public interface IJob
        {
            void Execute();
        }

        namespace LowLevel
        {
            namespace Unsafe
            {
                public class JobProducerTypeAttribute : Attribute
                {
                }
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

    namespace UnityEngine
    {
        public class Debug
        {
            public static void Log(object message)
            {
            }
        }
    }

    namespace Collections
    {
        public struct NativeArray<T> : IDisposable, IEnumerable<T>, IEnumerable, IEquatable<NativeArray<T>>
            where T : struct
        {
            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public IEnumerator<T> GetEnumerator()
            {
                throw new NotImplementedException();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public bool Equals(NativeArray<T> other)
            {
                throw new NotImplementedException();
            }
        }
    }
}


namespace MethodInvocationTests
{
    public class MethodInvocationTests
    {
        public class SimpleClass
        {
            public static void StaticMethod()
            {
            }

            public void PlainMethod()
            {
            }
        }

        public static SimpleClass myClasss = new SimpleClass();

        [BurstCompile]
        struct MethodInvocationTest1 : IJob
        {
            public void Execute()
            {
                SimpleClass.StaticMethod();
                GetType();
            }
        }

        [BurstCompile]
        struct MethodInvocationTest2 : IJob
        {
            public void Execute()
            {
                Equals(null, null);
            }
        }

        [BurstCompile]
        struct MethodInvocationTest3 : IJob
        {
            public void Execute()
            {
                Equals(null);
            }
        }

        [BurstCompile]
        struct MethodInvocationTest4 : IJob
        {
            public void Execute()
            {
                ToString();
            }
        }

        [BurstCompile]
        struct MethodInvocationTest5 : IJob
        {
            public void Execute()
            {
                var kek = myClasss;
            }
        }

        [BurstCompile]
        struct MethodInvocationTest6 : IJob
        {
            public void Execute()
            {
                myClasss.PlainMethod();
            }
        }

        [BurstCompile]
        struct MethodInvocationTest7 : IJob
        {
            public void Execute()
            {
                GetHashCode();
            }
        }

        [BurstCompile]
        struct MethodInvocationTest8 : IJob
        {
            public void Execute()
            {
                GetHashCode();
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }
        }
    }
}