#nullable enable
using UnityEditor;
using UnityEngine;

namespace LocalMasterDataWriter.Editor
{
    [CustomEditor(typeof(SpreadSheet))]
    public sealed class SpreadSheetEditorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (target is not SpreadSheet self)
            {
                return;
            }

            self.DrawSecurityKeys();

            EditorGUILayout.Space();

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Sheets");
            EditorGUILayout.Space();
            DrawSheets(self);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();

            if (!self.VerifyAssets())
            {
                return;
            }

            if (GUILayout.Button("Build All"))
            {
                self.Build();
            }
        }

        private void DrawSheets(SpreadSheet self)
        {
            var sheets = serializedObject.FindProperty("m_Sheets");

            EditorGUILayout.PropertyField(sheets, includeChildren: false);

            if (sheets.isExpanded)
            {
                EditorGUI.indentLevel++;

                sheets.arraySize = EditorGUILayout.IntField("Size", sheets.arraySize);

                for (var i = 0; i < sheets.arraySize; i++)
                {
                    var element = sheets.GetArrayElementAtIndex(i);
                    var nameProperty = element.FindPropertyRelative("Name");
                    var gidProperty = element.FindPropertyRelative("Gid");

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(EditorGUI.indentLevel * 15f);

                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.PropertyField(nameProperty);
                    EditorGUILayout.PropertyField(gidProperty);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Build", GUILayout.Width(80)))
                    {
                        serializedObject.ApplyModifiedProperties();

                        self.BuildAt(i);

                        AssetDatabase.Refresh();
                    }

                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }
        }
    }
}