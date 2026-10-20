using System;

namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method
    | AttributeTargets.Interface | AttributeTargets.Delegate | AttributeTargets.Parameter
    | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
    AllowMultiple = false,
    Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;

    public NullableAttribute(byte flag)
    {
        NullableFlags = new[] { flag };
    }

    public NullableAttribute(byte[] flags)
    {
        NullableFlags = flags;
    }
}

[AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct
    | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Delegate,
    AllowMultiple = false,
    Inherited = false)]
internal sealed class NullableContextAttribute : Attribute
{
    public readonly byte Flag;

    public NullableContextAttribute(byte flag)
    {
        Flag = flag;
    }
}
