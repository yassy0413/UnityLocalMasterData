#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public const string ManifestFilename = "lmd-enum";

        private static readonly IReadOnlyDictionary<Type, ILocalMasterDataTable> EmptyTables =
            new ReadOnlyDictionary<Type, ILocalMasterDataTable>(new Dictionary<Type, ILocalMasterDataTable>());

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

        public async Task BuildAsync(byte[] aesIv, byte[] aesKey, byte[] hmacSecretKey)
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
                var builtTables = await BuildTablesAsync(aesIv, aesKey, hmacSecretKey);

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
            byte[] aesIv, byte[] aesKey, byte[] hmacSecretKey)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var sw = Stopwatch.StartNew();
#endif
            var tables = CreateTables();

            var manifestAsset = Resources.Load<TextAsset>(ManifestFilename);
            if (manifestAsset == null)
            {
                throw new FileNotFoundException();
            }

            var manifest = manifestAsset.text.Split(',');
            Resources.UnloadAsset(manifestAsset);

            var rootPath = $"{Application.streamingAssetsPath}/{manifest[0]}";

            ValidateBinaryFiles(tables, manifest[1..]);

            await Task.WhenAll(manifest[1..].Select(async x =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var sw2 = Stopwatch.StartNew();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
                var url = $"{rootPath}/{x}.bin";
#else
                var url = $"file://{rootPath}/{x}.bin";
#endif

                byte[] bytes;
                using (var request = UnityWebRequest.Get(url))
                {
                    await request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        throw new Exception($"LocalMasterDataReader load failed. url[{url}] error[{request.error}]");
                    }

                    bytes = request.downloadHandler.data;
                }

                await Task.Run(() =>
                {
                    bytes = LocalMasterDataCompressor.DecompressAndDecrypt(bytes, aesIv, aesKey, hmacSecretKey);
                    tables[x].instance.ReadBinary(bytes);
                });

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"Read [{x}] {sw2.Elapsed.TotalSeconds}sec");
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

        [Conditional("UNITY_EDITOR")]
        private static void ValidateBinaryFiles(
            IReadOnlyDictionary<string, (Type type, ILocalMasterDataTable instance)> tables,
            IReadOnlyCollection<string> keys)
        {
            var fileNames = keys
                .Where(static x => !string.IsNullOrEmpty(x))
                .Select(static x => x!)
                .ToHashSet();

            var missingFiles = tables.Keys
                .Where(x => !fileNames.Contains(x))
                .OrderBy(static x => x)
                .ToArray();

            var unknownFiles = fileNames
                .Where(x => !tables.ContainsKey(x))
                .OrderBy(static x => x)
                .ToArray();

            if (missingFiles.Length == 0 && unknownFiles.Length == 0)
            {
                return;
            }

            throw new InvalidDataException(
                "Local master data files do not match table definitions."
                + FormatValidationDetails("Missing files", missingFiles)
                + FormatValidationDetails("Unknown files", unknownFiles));
        }

        private static string FormatValidationDetails(string label, IReadOnlyCollection<string> values)
        {
            return values.Count == 0
                ? string.Empty
                : $" {label}: [{string.Join(", ", values)}].";
        }

        private static readonly Type LocalMasterDataTableType = typeof(ILocalMasterDataTable);

        public static IReadOnlyDictionary<string, (Type type, ILocalMasterDataTable instance)> CreateTables()
        {
            return Assembly.Load("Assembly-CSharp").GetTypes()
                .AsParallel()
                .Where(static x => !x.IsAbstract)
                .Where(static x => LocalMasterDataTableType.IsAssignableFrom(x))
                .Select(static x => (x, instance: CreateInstance(x)))
                .ToDictionary(static x => x.instance.GetSheetName());
        }

        private static ILocalMasterDataTable CreateInstance(Type type)
        {
            if (Activator.CreateInstance(type) is ILocalMasterDataTable instance)
            {
                return instance;
            }

            throw new Exception($"Can not create instance of {type.FullName}.");
        }
    }
}