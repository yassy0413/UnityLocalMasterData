#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LocalMasterData;
using UnityEditor;
using UnityEngine;

namespace LocalMasterDataWriter.Editor
{
    public class Builder : ScriptableObject
    {
        [HideInInspector]
        [SerializeField]
        public string AesId = "";

        [HideInInspector]
        [SerializeField]
        public string AesKey = "";

        [HideInInspector]
        [SerializeField]
        public string HmacSecretKey = "";

        [Header("StreamingAssets Folder Only")]
        [SerializeField]
        public DefaultAsset? OutputFolder;

        [Header("Scripts Folder Only")]
        [SerializeField]
        public DefaultAsset? OutputScriptFolder;

        [Header("Resources Folder Only")]
        [SerializeField]
        public DefaultAsset? OutputManifestFolder;

        private void OnEnable()
        {
            RegenerateSecurityKeys(false);
        }

        protected void RegenerateSecurityKeys(bool force)
        {
            if (!force && !string.IsNullOrEmpty(AesId))
            {
                return;
            }

            AesId = Convert.ToBase64String(LocalMasterDataCompressor.CreateAesManaged().IV);
            AesKey = Convert.ToBase64String(LocalMasterDataCompressor.CreateAesManaged().Key);
            HmacSecretKey = CreateRandomString();
            return;

            static string CreateRandomString(int size = 64)
            {
                Span<byte> secretKey = stackalloc byte[size];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(secretKey);
                }

                return Convert.ToBase64String(secretKey);
            }
        }

        /// <summary>
        /// create manifest file containing the all generated LocalMasterData table names.
        /// </summary>
        protected void WriteManifestFile(
            string streamingFolderPath,
            IEnumerable<string> tableNames)
        {
            const string prefix = "Assets/StreamingAssets/";
            var manifestData = $"{streamingFolderPath[prefix.Length..]},{string.Join(',', tableNames)}";

            var manifestPath = Path.Join(
                AssetDatabase.GetAssetPath(OutputManifestFolder),
                $"{LocalMasterDataReader.ManifestFilename}.txt");
            File.WriteAllText(manifestPath, manifestData);
        }

        protected void WriteScriptFile()
        {
            var sb = new StringBuilder();
            sb.AppendLine("public static class LocalMasterDataConst");
            sb.AppendLine("{");
            ToByteArrayDefinition(sb, AesId, "AesId");
            ToByteArrayDefinition(sb, AesKey, "AesKey");
            ToByteArrayDefinition(sb, HmacSecretKey, "HmacSecretKey");
            sb.AppendLine("}");

            var path = Path.Join(AssetDatabase.GetAssetPath(OutputScriptFolder), "LocalMasterDataConst.cs");
            File.WriteAllText(path, sb.ToString());
        }

        public void DrawSecurityKeys()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("AesId", AesId);
                EditorGUILayout.TextField("AesKey", AesKey);
                EditorGUILayout.TextField("HmacSecretKey", HmacSecretKey);
            }

            if (GUILayout.Button("Regenerate Security Keys"))
            {
                RegenerateSecurityKeys(true);
            }
        }

        public virtual bool VerifyAssets()
        {
            var result = VerifyFolder(OutputFolder, "OutputFolder", "StreamingAssets");
            result &= VerifyFolder(OutputManifestFolder, "OutputManifestFolder", "Resources");
            result &= VerifyFolder(OutputScriptFolder, "OutputScriptFolder", "Scripts");
            return result;
        }

        protected static bool VerifyFolder(DefaultAsset? asset, string folderName, string place)
        {
            if (asset == null)
            {
                EditorGUILayout.HelpBox($"{folderName} must be set.", MessageType.Error);
                return false;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (!path.Split('/').Contains(place))
            {
                EditorGUILayout.HelpBox($"{folderName} must be placed in the {place} folder.", MessageType.Error);
                return false;
            }

            return true;
        }

        private static string ToByteArrayDefinition(StringBuilder sb, string text, string variableName)
        {
            var bytes = Convert.FromBase64String(text);

            sb.Append("public static byte[] ");
            sb.Append(variableName);
            sb.AppendLine(" =");
            sb.AppendLine("{");

            for (var i = 0; i < bytes.Length; i++)
            {
                if (i % 16 == 0)
                {
                    sb.Append("    ");
                }

                sb.Append("0x");
                sb.Append(bytes[i].ToString("X2"));

                if (i != bytes.Length - 1)
                {
                    sb.Append(", ");
                }

                if (i % 16 == 15 || i == bytes.Length - 1)
                {
                    sb.AppendLine();
                }
            }

            sb.AppendLine("};");
            return sb.ToString();
        }
    }
}