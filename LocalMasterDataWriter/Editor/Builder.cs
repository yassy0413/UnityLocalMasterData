#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using LocalMasterData;
using UnityEditor;
using UnityEngine;

namespace LocalMasterDataWriter.Editor
{
    public class Builder : ScriptableObject
    {
        [Tooltip("RSA署名を付与します。無効でもAES暗号化とHMAC検証は行います。")]
        public bool UseRsaSignature = true;

        [HideInInspector]
        [SerializeField]
        public string AesKey = "";

        [HideInInspector]
        [SerializeField]
        public string HmacSecretKey = "";

        [HideInInspector]
        [SerializeField]
        public string SigningPrivateKeyParameters = "";

        [Header("StreamingAssets Folder Only")]
        [SerializeField]
        public DefaultAsset? OutputFolder;

        [Header("Scripts Folder Only")]
        [SerializeField]
        public DefaultAsset? OutputScriptFolder;

        private void OnEnable()
        {
            RegenerateSecurityKeys(false);
        }

        protected void RegenerateSecurityKeys(bool force)
        {
            if (!force &&
                !string.IsNullOrEmpty(AesKey) &&
                !string.IsNullOrEmpty(HmacSecretKey) &&
                !string.IsNullOrEmpty(SigningPrivateKeyParameters))
            {
                return;
            }

            AesKey = Convert.ToBase64String(LocalMasterDataCompressor.CreateAesKey());
            HmacSecretKey = Convert.ToBase64String(LocalMasterDataCompressor.CreateHmacKey());
            SigningPrivateKeyParameters = Convert.ToBase64String(
                LocalMasterDataCompressor.CreateSigningPrivateKey());
        }

        protected void WriteScriptFile(string streamingFolderPath)
        {
            const string streamingAssetsPrefix = "Assets/StreamingAssets/";
            if (!streamingFolderPath.StartsWith(streamingAssetsPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Output folder must be below {streamingAssetsPrefix}: {streamingFolderPath}");
            }

            var signingPrivateKey = Convert.FromBase64String(SigningPrivateKeyParameters);
            LocalMasterDataCompressor.GetSigningPublicKey(
                signingPrivateKey,
                out var signingPublicKeyModulus,
                out var signingPublicKeyExponent);

            var sb = new StringBuilder();
            sb.AppendLine("public static class LocalMasterDataConst");
            sb.AppendLine("{");
            sb.Append("    public const string StreamingAssetsDirectory = \"");
            sb.Append(streamingFolderPath[streamingAssetsPrefix.Length..].Replace("\\", "/"));
            sb.AppendLine("\";");
            ToByteArrayDefinition(sb, AesKey, "AesKey");
            ToByteArrayDefinition(sb, HmacSecretKey, "HmacSecretKey");
            ToByteArrayDefinition(
                sb,
                Convert.ToBase64String(signingPublicKeyModulus),
                "SigningPublicKeyModulus");
            ToByteArrayDefinition(
                sb,
                Convert.ToBase64String(signingPublicKeyExponent),
                "SigningPublicKeyExponent");
            sb.AppendLine("}");

            var path = Path.Join(AssetDatabase.GetAssetPath(OutputScriptFolder), "LocalMasterDataConst.cs");
            File.WriteAllText(path, sb.ToString());
        }

        public void DrawSecurityKeys()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("AesKey", AesKey);
                EditorGUILayout.TextField("HmacSecretKey", HmacSecretKey);
                EditorGUILayout.TextField("SigningPrivateKey", SigningPrivateKeyParameters);
            }

            if (GUILayout.Button("Regenerate Security Keys"))
            {
                RegenerateSecurityKeys(true);
            }
        }

        public virtual bool VerifyAssets()
        {
            var result = VerifyFolder(OutputFolder, "OutputFolder", "StreamingAssets");
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

            sb.Append("    public static readonly byte[] ");
            sb.Append(variableName);
            sb.AppendLine(" =");
            sb.AppendLine("    {");

            for (var i = 0; i < bytes.Length; i++)
            {
                if (i % 16 == 0)
                {
                    sb.Append("        ");
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

            sb.AppendLine("    };");
            return sb.ToString();
        }
    }
}
