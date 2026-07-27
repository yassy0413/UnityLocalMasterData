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

        public abstract string GetSheetName();

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
                .Select(x => (value: x, typecode: Type.GetTypeCode(x.PropertyType)))
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
                                bs.Write(short.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Int32:
                                bs.Write(int.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Int64:
                                bs.Write(long.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.UInt16:
                                bs.Write(ushort.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.UInt32:
                                bs.Write(uint.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.UInt64:
                                bs.Write(ulong.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Single:
                                bs.Write(float.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Double:
                                bs.Write(double.Parse(value, CultureInfo.InvariantCulture));
                                break;

                            case TypeCode.Boolean:
                                bs.Write(bool.Parse(value));
                                break;

                            case TypeCode.DateTime:
                                bs.Write(DateTime
                                    .Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                                    .ToBinary());
                                break;

                            default:
                                bs.Write(value);
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