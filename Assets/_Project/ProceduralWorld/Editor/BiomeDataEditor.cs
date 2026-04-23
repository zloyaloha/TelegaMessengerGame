#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Кастомный инспектор для BiomeData — отображает дерево булевых условий
/// как вложенные блоки с кнопками выбора типа.
/// </summary>
[CustomEditor(typeof(BiomeData))]
public class BiomeDataEditor : Editor
{
    // Названия типов условий (должны совпадать с CondTypes по индексу)
    internal static readonly string[] CondTypeNames =
    {
        "Высота (диапазон)",
        "Температура (диапазон)",
        "AND  (все должны выполняться)",
        "OR   (хотя бы одно)",
        "NOT  (инверсия)",
    };

    internal static readonly System.Type[] CondTypes =
    {
        typeof(HeightRangeCondition),
        typeof(TemperatureRangeCondition),
        typeof(AndCondition),
        typeof(OrCondition),
        typeof(NotCondition),
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "condition", "m_Script");

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Условие появления биома", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Задайте дерево условий: AND/OR/NOT + диапазоны высоты (м) и температуры (0–1).\n" +
            "BiomeManager выбирает биом с наименьшим Score() — ближайший к выполнению.",
            MessageType.None);

        var condProp = serializedObject.FindProperty("condition");
        DrawConditionNode(condProp, depth: 0);

        serializedObject.ApplyModifiedProperties();
    }

    // ── Рекурсивная отрисовка узла ────────────────────────────────────────────

    internal void DrawConditionNode(SerializedProperty prop, int depth)
    {
        var style = new GUIStyle(EditorStyles.helpBox);
        style.margin = new RectOffset(depth * 10, 2, 2, 2);

        EditorGUILayout.BeginVertical(style);

        // Заголовок: название типа + кнопка смены типа
        object currentRef = prop.managedReferenceValue;
        string typeName   = currentRef == null ? "— не задано —" : GetTypeName(currentRef.GetType());

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(typeName, EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
        if (DrawTypeDropdown(prop, currentRef))
        {
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return; // тип изменён — перерисуем в следующем кадре
        }
        EditorGUILayout.EndHorizontal();

        if (currentRef == null)
        {
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUI.indentLevel++;

        if (currentRef is HeightRangeCondition)
        {
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("min"),
                new GUIContent("Min высота (м)"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("max"),
                new GUIContent("Max высота (м)"));
        }
        else if (currentRef is TemperatureRangeCondition)
        {
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("min"),
                new GUIContent("Min температура"));
            EditorGUILayout.PropertyField(prop.FindPropertyRelative("max"),
                new GUIContent("Max температура"));
        }
        else if (currentRef is AndCondition || currentRef is OrCondition)
        {
            DrawChildrenArray(prop.FindPropertyRelative("children"), depth + 1);
        }
        else if (currentRef is NotCondition)
        {
            DrawConditionNode(prop.FindPropertyRelative("child"), depth + 1);
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.EndVertical();
    }

    // ── Массив дочерних узлов (AND/OR) ────────────────────────────────────────

    private void DrawChildrenArray(SerializedProperty arrayProp, int depth)
    {
        bool deleted = false;
        int  deleteIdx = -1;

        for (int i = 0; i < arrayProp.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 10);
            EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(28));
            if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(17)))
            {
                deleteIdx = i;
                deleted   = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!deleted)
                DrawConditionNode(arrayProp.GetArrayElementAtIndex(i), depth);
        }

        if (deleted)
        {
            arrayProp.DeleteArrayElementAtIndex(deleteIdx);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.Space(2);
        GUILayout.BeginHorizontal();
        GUILayout.Space(depth * 10);
        if (GUILayout.Button("＋ Добавить условие", GUILayout.Height(22)))
        {
            int newIdx = arrayProp.arraySize;
            arrayProp.arraySize = newIdx + 1;
            arrayProp.GetArrayElementAtIndex(newIdx).managedReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
        }
        GUILayout.EndHorizontal();
    }

    // ── Дропдаун выбора типа ──────────────────────────────────────────────────

    /// <summary>
    /// Рисует кнопку «Тип ▾». При выборе обновляет <paramref name="prop"/>.
    /// Возвращает true, если тип был изменён (нужно прервать отрисовку текущего кадра).
    /// </summary>
    private bool DrawTypeDropdown(SerializedProperty prop, object currentRef)
    {
        if (!EditorGUILayout.DropdownButton(
                new GUIContent("Тип ▾"),
                FocusType.Passive,
                GUILayout.Width(90)))
            return false;

        var so       = serializedObject;
        var propPath = prop.propertyPath;

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("(пусто)"), currentRef == null, () =>
        {
            so.Update();
            so.FindProperty(propPath).managedReferenceValue = null;
            so.ApplyModifiedProperties();
        });

        menu.AddSeparator("");

        for (int i = 0; i < CondTypeNames.Length; i++)
        {
            int   idx     = i;
            bool  isCurr  = currentRef?.GetType() == CondTypes[idx];
            menu.AddItem(new GUIContent(CondTypeNames[i]), isCurr, () =>
            {
                so.Update();
                so.FindProperty(propPath).managedReferenceValue =
                    System.Activator.CreateInstance(CondTypes[idx]);
                so.ApplyModifiedProperties();
            });
        }

        menu.ShowAsContext();
        return true;
    }

    // ── Утилиты ───────────────────────────────────────────────────────────────

    private string GetTypeName(System.Type t)
    {
        for (int i = 0; i < CondTypes.Length; i++)
            if (CondTypes[i] == t) return CondTypeNames[i];
        return t.Name;
    }
}
#endif
