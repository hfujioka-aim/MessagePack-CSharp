﻿// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using MessagePack;
using Xunit;
using Xunit.Abstractions;

public class MessagePackSecurityTests
{
    public MessagePackSecurityTests(ITestOutputHelper logger)
    {
        Logger = logger;
    }

    public ITestOutputHelper Logger { get; }

    [Fact]
    public void Untrusted()
    {
        Assert.True(MessagePackSecurity.UntrustedData.HashCollisionResistant);
    }

    [Fact]
    public void Trusted()
    {
        Assert.False(MessagePackSecurity.TrustedData.HashCollisionResistant);
    }

    [Fact]
    public void WithHashCollisionResistant()
    {
        Assert.Same(MessagePackSecurity.TrustedData, MessagePackSecurity.TrustedData.WithHashCollisionResistant(false));
        Assert.True(MessagePackSecurity.TrustedData.WithHashCollisionResistant(true).HashCollisionResistant);
    }

    [Fact]
    public void EqualityComparer_CollisionResistance_Int64()
    {
        const long value1 = 0x100000001;
        const long value2 = 0x200000002;
        var eq = MessagePackSecurity.UntrustedData.GetEqualityComparer<long>();
        Assert.Equal(EqualityComparer<long>.Default.GetHashCode(value1), EqualityComparer<long>.Default.GetHashCode(value2)); // demonstrate insecurity
        Assert.NotEqual(eq.GetHashCode(value1), eq.GetHashCode(value2));
    }

    [Fact]
    public void EqualityComparer_CollisionResistance_Guid()
    {
        Guid value1 = new Guid(new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 });
        Guid value2 = new Guid(new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0, 2 });
        var eq = MessagePackSecurity.UntrustedData.GetEqualityComparer<Guid>();
#if NETCOREAPP // We contrived the two GUIDs to force a collision on .NET Core. NETFx's algorithm is slightly different.
        Assert.Equal(EqualityComparer<Guid>.Default.GetHashCode(value1), EqualityComparer<Guid>.Default.GetHashCode(value2)); // demonstrate insecurity
#endif
        Assert.NotEqual(eq.GetHashCode(value1), eq.GetHashCode(value2));
    }

    [Fact]
    public unsafe void EqualityComparer_Single()
    {
        var eq = MessagePackSecurity.UntrustedData.GetEqualityComparer<float>();
        Assert.Equal(eq.GetHashCode(0.0f), eq.GetHashCode(-0.0f));

        // Try multiple forms of NaN
        float nan1, nan2;
        *(uint*)&nan1 = 0xFFC00001;
        *(uint*)&nan2 = 0xFF800001;

        Assert.True(float.IsNaN(nan1));
        Assert.True(float.IsNaN(nan2));
#if NETCOREAPP // .NET Framework had a bug where these would not be equal
        Assert.Equal(nan1.GetHashCode(), nan2.GetHashCode());
#endif
        Assert.Equal(eq.GetHashCode(nan1), eq.GetHashCode(nan2));
        Assert.Equal(eq.GetHashCode(float.NaN), eq.GetHashCode(-float.NaN));

        // Try various other clearly different numbers
        Assert.NotEqual(eq.GetHashCode(1.0f), eq.GetHashCode(-1.0f));
        Assert.NotEqual(eq.GetHashCode(1.0f), eq.GetHashCode(2.0f));
    }

    [Fact]
    public unsafe void EqualityComparer_Double()
    {
        var eq = MessagePackSecurity.UntrustedData.GetEqualityComparer<double>();
        Assert.Equal(eq.GetHashCode(0.0), eq.GetHashCode(-0.0));

        // Try multiple forms of NaN
        double nan1, nan2;
        *(ulong*)&nan1 = 0xFFF8000000000001;
        *(ulong*)&nan2 = 0xFFF8000000000002;

        Assert.True(double.IsNaN(nan1));
        Assert.True(double.IsNaN(nan2));
#if NETCOREAPP // .NET Framework had a bug where these would not be equal
        Assert.Equal(nan1.GetHashCode(), nan2.GetHashCode());
#endif
        Assert.Equal(eq.GetHashCode(nan1), eq.GetHashCode(nan2));
        Assert.Equal(eq.GetHashCode(double.NaN), eq.GetHashCode(-double.NaN));

        // Try various other clearly different numbers
        Assert.NotEqual(eq.GetHashCode(1.0), eq.GetHashCode(-1.0));
        Assert.NotEqual(eq.GetHashCode(1.0), eq.GetHashCode(2.0));
    }

    [Fact]
    public void EqualityComparer_ObjectFallback()
    {
        var eq = MessagePackSecurity.UntrustedData.GetEqualityComparer<object>();

        Assert.Equal(eq.GetHashCode(null), eq.GetHashCode(null));
        Assert.NotEqual(eq.GetHashCode(null), eq.GetHashCode(new object()));

        Assert.Equal(eq.GetHashCode("hi"), eq.GetHashCode("hi"));
        Assert.NotEqual(eq.GetHashCode("hi"), eq.GetHashCode("bye"));

        Assert.Equal(eq.GetHashCode(true), eq.GetHashCode(true));
        Assert.NotEqual(eq.GetHashCode(true), eq.GetHashCode(false));

        var o = new object();
        Assert.Equal(eq.GetHashCode(o), eq.GetHashCode(o));
        Assert.NotEqual(eq.GetHashCode(o), eq.GetHashCode(new object()));
    }

    /// <summary>
    /// Verifies that arbitrary other types not known to be hash safe will be rejected.
    /// </summary>
    [Fact]
    public void EqualityComparer_ObjectFallback_UnsupportedType()
    {
        var eq = MessagePackSecurity.UntrustedData.GetEqualityComparer<object>();
        var ex = Assert.Throws<TypeAccessException>(() => eq.GetHashCode(new AggregateException()));
        this.Logger.WriteLine(ex.ToString());
    }

    [Fact]
    public void TypelessFormatterWithUntrustedData_SafeKeys()
    {
        var data = new
        {
            A = (byte)3,
            B = new Dictionary<object, byte>
            {
                { "C", 15 },
            },
            D = new string[] { "E", "F" },
        };
        byte[] msgpack = MessagePackSerializer.Serialize(data);
        MessagePackSecurity.Active = MessagePackSecurity.UntrustedData;
        try
        {
            var deserialized = (IDictionary<object, object>)MessagePackSerializer.Deserialize<object>(msgpack);
            Assert.Equal(data.A, deserialized["A"]);
            Assert.Equal(data.B["C"], ((Dictionary<object, object>)deserialized["B"])["C"]);
            Assert.Equal(data.D, deserialized["D"]);
        }
        finally
        {
            MessagePackSecurity.Active = MessagePackSecurity.TrustedData;
        }
    }

    [Fact]
    public void TypelessFormatterWithUntrustedData_UnsafeKeys()
    {
        var data = new
        {
            B = new Dictionary<object, byte>
            {
                { new ArbitraryType(), 15 },
            },
        };
        byte[] msgpack = MessagePackSerializer.Serialize(data);
        MessagePackSecurity.Active = MessagePackSecurity.UntrustedData;
        try
        {
            var ex = Assert.Throws<TypeAccessException>(() => MessagePackSerializer.Deserialize<object>(msgpack));
            this.Logger.WriteLine(ex.ToString());
        }
        finally
        {
            MessagePackSecurity.Active = MessagePackSecurity.TrustedData;
        }
    }

    [DataContract]
    public class ArbitraryType
    {
    }
}