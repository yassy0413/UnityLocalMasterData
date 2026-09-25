#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;

namespace LocalMasterData
{
    [PublicAPI]
    public static class LocalMasterDataResolver
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<Type, LocalMasterDataTypeCodec> Codecs = new();

        internal sealed class LocalMasterDataTypeCodec
        {
            public Action<BinaryWriter, string> Write { get; }
            public Func<BinaryReader, object?> Read { get; }

            public LocalMasterDataTypeCodec(Action<BinaryWriter, string> write, Func<BinaryReader, object?> read)
            {
                Write = write;
                Read = read;
            }
        }

        public static void Register<T>(
            Func<string, T> parse,
            Action<BinaryWriter, T> write,
            Func<BinaryReader, T> read)
        {
            if (parse == null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            var codec = new LocalMasterDataTypeCodec(
                (writer, value) => write(writer, parse(value)),
                reader => read(reader));

            lock (SyncRoot)
            {
                Codecs[typeof(T)] = codec;
            }
        }

        internal static bool TryResolve(Type type, out LocalMasterDataTypeCodec codec)
        {
            lock (SyncRoot)
            {
                if (Codecs.TryGetValue(type, out var registeredCodec))
                {
                    codec = registeredCodec;
                    return true;
                }
            }

            codec = null!;
            return false;
        }
    }
}