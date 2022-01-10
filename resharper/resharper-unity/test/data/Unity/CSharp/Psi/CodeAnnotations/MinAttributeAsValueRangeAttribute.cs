using System;
using System.Diagnostics;
using UnityEngine;

public class MyMonoBehaviour : MonoBehaviour
{
    [Min(100)] public int intValue;
    [Min(100)] public uint uintValue;
    [Min(100)] public long longValue;
    [Min(100)] public ulong ulongValue;
    [Min(100)] public byte byteValue;
    [Min(100)] public sbyte sbyteValue;
    [Min(100)] public short shortValue;
    [Min(100)] public ushort ushortValue;

    [Min(1.3f)] public int intWithFloatRange;

    [Min(100)] private int nonSerialisedField;

    public void Update()
    {
        // Only the types that are implicitly converted into int will have
        // integer value analysis warnings
        if (intValue < 100) { }
        if (uintValue < 100) { }
        if (longValue < 100) { }
        if (ulongValue < 100) { }
        if (byteValue < 100) { }
        if (sbyteValue < 100) { }
        if (shortValue < 100) { }
        if (ushortValue < 100) { }

        if (nonSerialisedField < 100) { }

        if (intWithFloatRange < 1) { }
        if (intWithFloatRange == 0) { }
        if (intWithFloatRange == 1) { }
        if (intWithFloatRange == 2) { }
    }

    public void LateUpdate()
    {
        if (intValue > 500) { }
        if (uintValue > 500) { }
        if (longValue > 500) { }
        if (ulongValue > 500) { }
        if (byteValue > 500) { }
        if (sbyteValue > 500) { }
        if (shortValue > 500) { }
        if (ushortValue > 500) { }
    }
}

