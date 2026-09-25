#nullable enable
using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LocalMasterDataWriter.Editor
{
    [CustomEditor(typeof(SpreadSheet))]
    public sealed class SpreadSheetEditorEditor : UnityEditor.Editor
    {
        private const float Padding = 4f;
        private const float ButtonWidth = 80f;
        private const float SortButtonWidth = 26f;

        private static readonly GUIContent AscendingContent =
            new("\u25b2", "Sort by Name (ascending)");

        private static readonly GUIContent DescendingContent =
            new("\u25bc", "Sort by Name (descending)");

        private ReorderableList? m_SheetList;

        private int m_PendingSortOrder;

        private void OnEnable()
        {
            var sheets = serializedObject.FindProperty("m_Sheets");
            if (sheets == null)
            {
                return;
            }

            m_SheetList = new ReorderableList(
                serializedObject,
                sheets,
                draggable: true,
                displayHeader: true,
                displayAddButton: true,
                displayRemoveButton: true)
            {
                drawHeaderCallback = DrawHeader,
                elementHeightCallback = _ =>
                    (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 3f
                    + Padding * 2f,
                drawElementCallback = DrawElement,
            };
        }

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

            if (m_SheetList == null)
            {
                EditorGUILayout.HelpBox("m_Sheets was not found.", MessageType.Error);
            }
            else
            {
                m_SheetList.DoLayoutList();

                if (m_PendingSortOrder != 0)
                {
                    SortByName(m_SheetList.serializedProperty, m_PendingSortOrder > 0);
                    m_PendingSortOrder = 0;
                }
            }

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

        private void DrawHeader(Rect rect)
        {
            var labelRect = new Rect(
                rect.x,
                rect.y,
                rect.width - (SortButtonWidth * 2f) - 2f,
                rect.height);

            EditorGUI.LabelField(labelRect, m_SheetList?.serializedProperty.displayName ?? "Sheets");

            var ascendingRect = new Rect(
                rect.x + rect.width - (SortButtonWidth * 2f),
                rect.y,
                SortButtonWidth,
                rect.height);

            var descendingRect = new Rect(
                rect.x + rect.width - SortButtonWidth,
                rect.y,
                SortButtonWidth,
                rect.height);

            if (GUI.Button(ascendingRect, AscendingContent, EditorStyles.miniButtonLeft))
            {
                m_PendingSortOrder = 1;
            }

            if (GUI.Button(descendingRect, DescendingContent, EditorStyles.miniButtonRight))
            {
                m_PendingSortOrder = -1;
            }
        }

        private static void SortByName(SerializedProperty array, bool ascending)
        {
            // Selection sort: SerializedProperty only supports moving elements.
            for (var i = 0; i < array.arraySize - 1; i++)
            {
                var targetIndex = i;
                var targetName = GetName(array, i);

                for (var j = i + 1; j < array.arraySize; j++)
                {
                    var candidateName = GetName(array, j);
                    var comparison = string.Compare(
                        candidateName,
                        targetName,
                        StringComparison.OrdinalIgnoreCase);

                    if (ascending ? comparison < 0 : comparison > 0)
                    {
                        targetIndex = j;
                        targetName = candidateName;
                    }
                }

                if (targetIndex != i)
                {
                    array.MoveArrayElement(targetIndex, i);
                }
            }

            GUI.FocusControl(null);
        }

        private static string GetName(SerializedProperty array, int index)
        {
            return array.GetArrayElementAtIndex(index)
                .FindPropertyRelative("Name")
                .stringValue ?? string.Empty;
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (m_SheetList == null)
            {
                return;
            }

            var element = m_SheetList.serializedProperty.GetArrayElementAtIndex(index);
            var nameProperty = element.FindPropertyRelative("Name");
            var gidProperty = element.FindPropertyRelative("Gid");

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            var line = new Rect(rect.x, rect.y + Padding, rect.width, lineHeight);
            EditorGUI.PropertyField(line, nameProperty);

            line.y += lineHeight + spacing;
            EditorGUI.PropertyField(line, gidProperty);

            line.y += lineHeight + spacing;
            var buttonRect = new Rect(
                line.x + line.width - ButtonWidth,
                line.y,
                ButtonWidth,
                lineHeight);

            if (GUI.Button(buttonRect, "Build") && target is SpreadSheet self)
            {
                serializedObject.ApplyModifiedProperties();

                self.BuildAt(index);

                AssetDatabase.Refresh();
            }
        }
    }
}
