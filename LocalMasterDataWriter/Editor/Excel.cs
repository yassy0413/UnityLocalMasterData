#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExcelDataReader;
using LocalMasterData;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LocalMasterDataWriter.Editor
{
    /// <summary>
    /// Excelからバイナリを作成
    /// </summary>
    /// <see href="https://github.com/ExcelDataReader/ExcelDataReader"/>
    [CreateAssetMenu(
        fileName = "LocalMasterDataWriter_Excel",
        menuName = "LocalMasterData/Excel Writer")]
    public sealed class Excel : Builder
    {
        [Header("Editor Folder Only")]
        [SerializeField]
        public DefaultAsset? InputFolder;

        public override bool VerifyAssets()
        {
            var result = base.VerifyAssets();
            result &= VerifyFolder(InputFolder, "InputFolder", "Editor");
            return result;
        }

        public void Build()
        {
            BuildFor(null);
        }

        public void BuildTable(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("Table name must not be empty.", nameof(tableName));
            }

            BuildFor(tableName);
        }

        private void BuildFor(string? tableName)
        {
            AssetDatabase.Refresh();

            var tables = LocalMasterDataReader.CreateTables();
            var inputFolderPath = AssetDatabase.GetAssetPath(InputFolder);
            var outputFolderPath = AssetDatabase.GetAssetPath(OutputFolder);

            var aesKey = Convert.FromBase64String(AesKey);
            var hmacSecretKey = Convert.FromBase64String(HmacSecretKey);
            var signingPrivateKey = Convert.FromBase64String(SigningPrivateKeyParameters);

            var excelTables = Directory.GetFiles(inputFolderPath, "*.xlsx", SearchOption.AllDirectories)
                .SelectMany(path => GetTablesFromExcel(path, tableName))
                .ToDictionary(static x => x.Key, static x => x.Value);

            if (tableName != null && (!tables.ContainsKey(tableName) || !excelTables.ContainsKey(tableName)))
            {
                throw new InvalidOperationException($"Registered table or Excel sheet [{tableName}] was not found.");
            }

            Parallel.ForEach(
                excelTables,
                excelTable =>
                {
                    if (!tables.TryGetValue(excelTable.Key, out var table))
                    {
                        Debug.LogWarning($"Class [{excelTable.Key}] had not found.");
                        return;
                    }

                    var bytes = table.instance.CreateBinary(excelTable.Value);
                    bytes = LocalMasterDataCompressor.CompressEncryptAndSign(
                        bytes,
                        aesKey,
                        hmacSecretKey,
                        signingPrivateKey,
                        UseRsaSignature);
                    File.WriteAllBytes(Path.Combine(outputFolderPath, $"{excelTable.Key}.bin"), bytes);
                });

            if (tableName == null)
            {
                WriteScriptFile(outputFolderPath);
            }

            AssetDatabase.Refresh();
            Debug.Log("LocalMasterDataWriter.Excel build finished.");
        }

        private static Dictionary<string, List<Dictionary<string, string>>> GetTablesFromExcel(string path, string? tableName)
        {
            return GetSheetsFromExcel(path)
                .Where(sheet => tableName == null || sheet.TableName == tableName)
                .AsParallel()
                .Select(static sheet =>
                {
                    var keys = Enumerable.Range(0, sheet.Columns.Count)
                        .Select(x => sheet.Rows[0][x].ToString())
                        .Where(static x => !string.IsNullOrEmpty(x))
                        .ToArray();

                    var columnCount = keys.Length;
                    var rowCount = sheet.Rows.Count;

                    var records = Enumerable.Range(1, rowCount - 1)
                        .Select(r =>
                        {
                            // Create a record
                            return Enumerable.Range(0, columnCount)
                                .ToDictionary(c => keys[c], c => sheet.Rows[r][c].ToString());
                        })
                        .ToList();

                    return (sheet, records);
                })
                .ToDictionary(static x => x.sheet.TableName, static x => x.records);
        }

        private static List<DataTable> GetSheetsFromExcel(string path)
        {
            var sheets = new List<DataTable>();
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(fs);
            foreach (DataTable sheet in reader.AsDataSet().Tables)
            {
                sheets.Add(sheet);
            }

            return sheets;
        }
    }
}
