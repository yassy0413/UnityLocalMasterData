#nullable enable
using UnityEditor;
using UnityEngine;

namespace LocalMasterDataWriter.Editor
{
    [CustomEditor(typeof(Excel))]
    public sealed class ExcelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (target is not Excel self)
            {
                return;
            }

            self.DrawSecurityKeys();

            EditorGUILayout.Space();

            base.OnInspectorGUI();

            EditorGUILayout.Space();

            if (!self.VerifyAssets())
            {
                return;
            }

            if (GUILayout.Button("Build"))
            {
                AssetDatabase.Refresh();
                self.Build();
            }
        }
    }
}