#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace LocalMasterData
{
    [PublicAPI]
    public sealed class LocalMasterDataReader : IDisposable
    {
        private static readonly IReadOnlyDictionary<Type, ILocalMasterDataTable> EmptyTables =
            new ReadOnlyDictionary<Type, ILocalMasterDataTable>(new Dictionary<Type, ILocalMasterDataTable>());

        private static readonly Dictionary<string, Func<IReadOnlyList<LocalMasterDataTableRegistration>>>
            TableFactories = new();

        private static readonly object TableFactorySyncRoot = new();
        private static LocalMasterDataReader? ms_Instance;
        private readonly object m_SyncRoot = new();

        public static LocalMasterDataReader Instance =>
            ms_Instance ??= new LocalMasterDataReader();

        public static bool Exists => ms_Instance != null;

        public IReadOnlyDictionary<Type, ILocalMasterDataTable> Tables { get; private set; } = EmptyTables;

        public bool IsLoaded { get; private set; }

        public bool IsBuilding { get; private set; }

        public void Dispose()
        {
            ILocalMasterDataTable[] tables;
            lock (m_SyncRoot)
            {
                tables = Tables.Values.ToArray();
                Tables = EmptyTables;
                IsLoaded = false;
                IsBuilding = false;
                ms_Instance = null;
            }

            foreach (var table in tables)
            {
                table.Dispose();
            }

            Debug.Log("LocalMasterDataReader disposed.");
        }

        public async Task BuildAsync(
            byte[] aesKey,
            byte[] hmacSecretKey,
            byte[] signingPublicKeyModulus,
            byte[] signingPublicKeyExponent,
            string streamingAssetsDirectory)
        {
            lock (m_SyncRoot)
            {
                if (IsBuilding)
                {
                    throw new InvalidOperationException("LocalMasterDataReader is already building.");
                }

                IsBuilding = true;
            }

            try
            {
                var builtTables = await BuildTablesAsync(
                    aesKey,
                    hmacSecretKey,
                    signingPublicKeyModulus,
                    signingPublicKeyExponent,
                    streamingAssetsDirectory);

                foreach (var table in Tables.Values)
                {
                    table.Dispose();
                }

                lock (m_SyncRoot)
                {
                    Tables = new ReadOnlyDictionary<Type, ILocalMasterDataTable>(builtTables);
                    IsLoaded = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }
            finally
            {
                lock (m_SyncRoot)
                {
                    IsBuilding = false;
                }
            }
        }

        private static async Task<Dictionary<Type, ILocalMasterDataTable>> BuildTablesAsync(
            byte[] aesKey,
            byte[] hmacSecretKey,
            byte[] signingPublicKeyModulus,
            byte[] signingPublicKeyExponent,
            string streamingAssetsDirectory)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var sw = Stopwatch.StartNew();
#endif
            var tables = CreateTables();
            if (string.IsNullOrWhiteSpace(streamingAssetsDirectory))
            {
                throw new ArgumentException(
                    "StreamingAssets directory must not be empty.",
                    nameof(streamingAssetsDirectory));
            }

            var rootPath = $"{Application.streamingAssetsPath}/{streamingAssetsDirectory.Trim('/')}";

            await Task.WhenAll(tables.Select(async pair =>
            {
                var sheetName = pair.Key;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var sw2 = Stopwatch.StartNew();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
                var url = $"{rootPath}/{sheetName}.bin";
#else
                var url = $"file://{rootPath}/{sheetName}.bin";
#endif

                byte[] bytes;
                using (var request = UnityWebRequest.Get(url))
                {
                    await request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"LocalMasterDataReader load failed. url[{url}] error[{request.error}]");
                        return;
                    }

                    bytes = request.downloadHandler.data;
                }

                await Task.Run(() =>
                {
                    bytes = LocalMasterDataCompressor.DecryptVerifyAndDecompress(
                        bytes,
                        aesKey,
                        hmacSecretKey,
                        signingPublicKeyModulus,
                        signingPublicKeyExponent);
                    pair.Value.instance.ReadBinary(bytes);
                });

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"Read [{sheetName}] {sw2.Elapsed.TotalSeconds}sec");
#endif
            }));

            var result = tables.ToDictionary(
                static x => x.Value.type,
                static x => x.Value.instance);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"LocalMasterDataReader build finished. {sw.Elapsed.TotalSeconds}sec");
#endif

            return result;
        }

        public static void RegisterTableFactory(
            string assemblyName,
            Func<IReadOnlyList<LocalMasterDataTableRegistration>> factory)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new ArgumentException("Assembly name must not be empty.", nameof(assemblyName));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (TableFactorySyncRoot)
            {
                TableFactories[assemblyName] = factory;
            }
        }

        public static IReadOnlyDictionary<string, (Type type, ILocalMasterDataTable instance)> CreateTables()
        {
            Func<IReadOnlyList<LocalMasterDataTableRegistration>>[] factories;
            lock (TableFactorySyncRoot)
            {
                factories = TableFactories.Values.ToArray();
            }

            if (factories.Length == 0)
            {
                throw new InvalidOperationException(
                    "No local master data table was registered. " +
                    "Make sure the LocalMasterData Source Generator is installed and tables have " +
                    "LocalMasterDataAttribute.");
            }

            var result = new Dictionary<string, (Type type, ILocalMasterDataTable instance)>();
            foreach (var registration in factories.SelectMany(static x => x()))
            {
                if (!result.TryAdd(registration.SheetName, (registration.Type, registration.Instance)))
                {
                    throw new InvalidOperationException(
                        $"Duplicate local master data sheet name: {registration.SheetName}");
                }
            }

            return new ReadOnlyDictionary<string, (Type type, ILocalMasterDataTable instance)>(result);
        }
    }
}