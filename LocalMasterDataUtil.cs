#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LocalMasterData
{
    internal static class LocalMasterDataUtil
    {
        public delegate object? ReadValueDelegate(BinaryReader bs);

        public sealed class PropertyReader
        {
            public Action<object, object?> Setter { get; }
            public ReadValueDelegate ReadValue { get; }

            public PropertyReader(Action<object, object?> setter, ReadValueDelegate readValue)
            {
                Setter = setter;
                ReadValue = readValue;
            }
        }

        public static PropertyReader[] CreatePropertyReaders<T>()
        {
            return typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(static property => new PropertyReader(CreateSetter(property), CreateReadValue(property)))
                .ToArray();
        }

        private static Action<object, object?> CreateSetter(PropertyInfo property)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");

            var body = Expression.Assign(
                Expression.Property(Expression.Convert(instance, property.DeclaringType!), property),
                Expression.Convert(value, property.PropertyType));

            return Expression.Lambda<Action<object, object?>>(body, instance, value).Compile();
        }

        private static ReadValueDelegate CreateReadValue(PropertyInfo property)
        {
            return Type.GetTypeCode(property.PropertyType) switch
            {
                TypeCode.Int16 => static bs => bs.ReadInt16(),
                TypeCode.Int32 => static bs => bs.ReadInt32(),
                TypeCode.Int64 => static bs => bs.ReadInt64(),
                TypeCode.UInt16 => static bs => bs.ReadUInt16(),
                TypeCode.UInt32 => static bs => bs.ReadUInt32(),
                TypeCode.UInt64 => static bs => bs.ReadUInt64(),
                TypeCode.Single => static bs => bs.ReadSingle(),
                TypeCode.Double => static bs => bs.ReadDouble(),
                TypeCode.Boolean => static bs => bs.ReadBoolean(),
                TypeCode.DateTime => static bs => DateTime.FromBinary(bs.ReadInt64()),
                TypeCode.String => static bs => bs.ReadString(),
                _ => LocalMasterDataResolver.TryResolve(property.PropertyType, out var codec)
                    ? reader => codec.Read(reader)
                    : static bs => bs.ReadString(),
            };
        }
    }
}
