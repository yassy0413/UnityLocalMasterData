#nullable enable
using System;
using System.Linq;
using LocalMasterData;
using UnityEditor;
using UnityEngine;

namespace LocalMasterDataWriter.Editor
{
    [CustomEditor(typeof(Excel))]
    public sealed class ExcelEditor : UnityEditor.Editor
    {
        private string m_SelectedTable = string.Empty;

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

            if (GUILayout.Button("Build All"))
            {
                AssetDatabase.Refresh();
                self.Build();
            }

            EditorGUILayout.Space();
            var names = LocalMasterDataReader.CreateTables().Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (names.Length == 0)
            {
                return;
            }

            var selectedIndex = Math.Max(0, Array.IndexOf(names, m_SelectedTable));
            selectedIndex = EditorGUILayout.Popup("Table", selectedIndex, names);
            m_SelectedTable = names[selectedIndex];
            if (GUILayout.Button("Build Selected Table"))
            {
                self.BuildTable(m_SelectedTable);
            }
        }
    }
}