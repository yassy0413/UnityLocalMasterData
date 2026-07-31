#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Csv;
using LocalMasterData;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LocalMasterDataWriter.Editor
{
    /// <summary>
    /// SpreadSheetからバイナリを作成 (開発中)
    /// </summary>
    [CreateAssetMenu(
        fileName = "LocalMasterDataWriter_SpreadSheet",
        menuName = "LocalMasterData/SpreadSheet Writer")]
    public sealed class SpreadSheet : Builder
    {
        [Serializable]
        private sealed class Sheet
        {
            public string Name = string.Empty;
            public string Gid = string.Empty;
        }

        [Header("")]
        [SerializeField]
        private string m_ApiUrl = string.Empty;

        [SerializeField]
        private string m_ApiToken = string.Empty;

        [SerializeField]
        private Sheet[] m_Sheets = Array.Empty<Sheet>();

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        public void BuildAt(int index)
        {
            BuildFor(new[] { m_Sheets[index] });
        }

        public void Build()
        {
            BuildFor(m_Sheets);
        }

        private void BuildFor(Sheet[] sheets)
        {
            AssetDatabase.Refresh();

            var tables = LocalMasterDataReader.CreateTables();
            var outputFolderPath = AssetDatabase.GetAssetPath(OutputFolder);

            var aesId = Convert.FromBase64String(AesId);
            var aesKey = Convert.FromBase64String(AesKey);
            var hmacSecretKey = Convert.FromBase64String(HmacSecretKey);

            Parallel.ForEach(
                sheets,
                sheet =>
                {
                    if (!tables.TryGetValue(sheet.Name, out var table))
                    {
                        Debug.LogWarning($"Class [{sheet.Name}] had not found.");
                        return;
                    }

                    var url = $"{m_ApiUrl}?token={m_ApiToken}&gid={sheet.Gid}";

                    byte[] csvData;
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        using var response = GetResult(HttpClient.SendAsync(request));
                        response.EnsureSuccessStatusCode();
                        csvData = GetResult(response.Content.ReadAsByteArrayAsync());
                    }

                    var csvRows = ParseCsv(csvData);

                    var keys = Enumerable.Range(0, csvRows[0].Length)
                        .Select(x => csvRows[0][x].ToString())
                        .Where(static x => !string.IsNullOrEmpty(x))
                        .ToArray();

                    var columnCount = keys.Length;
                    var rowCount = csvRows.Length;

                    var records = Enumerable.Range(1, rowCount - 1)
                        .Select(r =>
                        {
                            // Create a record
                            return Enumerable.Range(0, columnCount)
                                .ToDictionary(c => keys[c], c => csvRows[r][c]);
                        })
                        .ToList();

                    var bytes = table.instance.CreateBinary(records);
                    bytes = LocalMasterDataCompressor.CompressAndEncrypt(bytes, aesId, aesKey, hmacSecretKey);
                    File.WriteAllBytes(Path.Combine(outputFolderPath, $"{sheet.Name}.bin"), bytes);
                }
            );

            WriteManifestFile(outputFolderPath, tables.Select(static x => x.Key));
            WriteScriptFile();

            AssetDatabase.Refresh();
            Debug.Log("LocalMasterDataWriter.SpreadSheet build finished.");
        }

        private static T GetResult<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        private static string[][] ParseCsv(byte[] csvData)
        {
            var document = CsvSerializer.ConvertToDocument(
                csvData,
                new CsvOptions { HasHeader = false, });

            var result = new string[document.Rows.Length][];

            for (var y = 0; y < document.Rows.Length; y++)
            {
                var row = document.Rows[y];
                var values = new string[row.Length];

                for (var x = 0; x < row.Length; x++)
                {
                    values[x] = row[x].GetValue<string>() ?? string.Empty;
                }

                result[y] = values;
            }

            return result;
        }
    }
}