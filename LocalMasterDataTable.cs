#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using JetBrains.Annotations;
using UnityEngine;

namespace LocalMasterData
{
    [PublicAPI]
    public abstract class LocalMasterDataTable<TKey, TRecord, TTable> : ILocalMasterDataTable
        where TRecord : class, new()
        where TTable : class
    {
        private static TTable? ms_Instance;

        public static TTable Instance
        {
            get
            {
                if (ms_Instance != null)
                {
                    return ms_Instance;
                }

                var reader = LocalMasterDataReader.Instance;
                if (!reader.IsLoaded)
                {
                    throw new InvalidOperationException("LocalMasterDataReader has not been loaded.");
                }

                if (!reader.Tables.TryGetValue(typeof(TTable), out var table) || table is not TTable instance)
                {
                    throw new InvalidOperationException(
                        $"Local master data table is not loaded: {typeof(TTable).FullName}");
                }

                ms_Instance = instance;
                return instance;
            }
        }

        public abstract TKey GetKey(TRecord record);

        public List<TRecord> Records { get; } = new();

        public Dictionary<TKey, TRecord> IdMap { get; } = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TRecord? GetRecord(TKey key) => IdMap.GetValueOrDefault(key);

        public void Dispose()
        {
            ms_Instance = null;
            Records.Clear();
            IdMap.Clear();
        }

        public byte[] CreateBinary(List<Dictionary<string, string>> records)
        {
            var type = typeof(TRecord);
            var props = type.GetProperties()
                .Select(static x =>
                {
                    var typecode = Type.GetTypeCode(x.PropertyType);
                    var codec = typecode switch
                    {
                        TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
                        TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 or
                        TypeCode.Single or TypeCode.Double or TypeCode.Boolean or
                        TypeCode.DateTime or TypeCode.String => null,
                        _ => LocalMasterDataResolver.TryResolve(x.PropertyType, out var registeredCodec)
                            ? registeredCodec
                            : null,
                    };
                    return (value: x, typecode, codec);
                })
                .ToArray();

            using var ms = new MemoryStream();
            using var bs = new BinaryWriter(ms, Encoding.UTF8, false);
            bs.Write(records.Count);
            Debug.Log($"[{type}] {props.Length} fields. {records.Count} records.");

            foreach (var record in records)
            {
                foreach (var prop in props)
                {
                    var value = record.GetValueOrDefault(prop.value.Name, "");

                    try
                    {
                        switch (prop.typecode)
                        {
                            case TypeCode.Int16:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : short.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Int32:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : int.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Int64:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : long.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.UInt16:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : ushort.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.UInt32:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : uint.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.UInt64:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : ulong.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Single:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : float.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Double:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0
                                    : double.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Boolean:
                                bs.Write(!string.IsNullOrEmpty(value) && bool.Parse(value));
                                break;

                            case TypeCode.DateTime:
                                bs.Write(string.IsNullOrEmpty(value)
                                    ? 0L
                                    : DateTime
                                        .Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                                        .ToBinary());
                                break;

                            case TypeCode.String:
                                bs.Write(value);
                                break;

                            default:
                                if (prop.codec != null)
                                {
                                    prop.codec.Write(bs, value);
                                }
                                else
                                {
                                    bs.Write(value);
                                }
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to parse [{prop.value.Name}] of [{type}] : {e}");
                    }
                }
            }

            return ms.ToArray();
        }

        public void ReadBinary(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var bs = new BinaryReader(ms, Encoding.UTF8, false);
            var numRecords = bs.ReadInt32();

            Records.Capacity = numRecords;
            IdMap.EnsureCapacity(numRecords);

            var propertyReaders = LocalMasterDataUtil.CreatePropertyReaders<TRecord>();

            for (var index = 0; index < numRecords; ++index)
            {
                var instance = new TRecord();

                foreach (var reader in propertyReaders)
                {
                    reader.Setter(instance, reader.ReadValue(bs));
                }

                Records.Add(instance);
                IdMap.Add(GetKey(instance), instance);
            }
        }
    }
}
