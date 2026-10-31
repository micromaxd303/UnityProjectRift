using UnityEngine;
using UnityEditor;

namespace NavMesh3DSystem.Editor
{
    /// <summary>
    /// Кастомный инспектор для NavMesh3D.
    /// 
    /// Добавляет:
    /// - Кнопки Bake/Clear
    /// - Информацию о состоянии навмеша
    /// - Красивое форматирование
    /// </summary>
    [CustomEditor(typeof(NavMesh3D))]
    public class NavMesh3DEditor : UnityEditor.Editor
    {
        // Кэшируем ссылку на компонент
        private NavMesh3D navMesh;
        
        // Стили (создаются при первом использовании)
        private GUIStyle headerStyle;
        private GUIStyle infoBoxStyle;
        
        private void OnEnable()
        {
            navMesh = (NavMesh3D)target;
        }
        
        public override void OnInspectorGUI()
        {
            // Инициализируем стили
            InitStyles();
            
            // Рисуем заголовок
            DrawStatusHeader();
            
            EditorGUILayout.Space(5);
            
            // Рисуем стандартные поля
            DrawDefaultInspector();
            
            EditorGUILayout.Space(10);
            
            // Рисуем кнопки управления
            DrawBakingControls();
            
            EditorGUILayout.Space(10);
            
            // Рисуем информацию о навмеше
            if (navMesh.IsBaked)
            {
                DrawNavMeshInfo();
            }
            
            // Применяем изменения
            if (GUI.changed)
            {
                EditorUtility.SetDirty(navMesh);
            }
        }
        
        private void InitStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            
            if (infoBoxStyle == null)
            {
                infoBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 10, 10)
                };
            }
        }
        
        private void DrawStatusHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Иконка статуса
            string status = navMesh.IsBaked ? "✓ Baked" : "○ Not Baked";
            Color statusColor = navMesh.IsBaked ? new Color(0.2f, 0.8f, 0.2f) : Color.gray;
            
            GUIStyle statusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = statusColor }
            };
            
            EditorGUILayout.LabelField(status, statusStyle);
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawBakingControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Baking", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            // Кнопка Bake
            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            if (GUILayout.Button("Bake", GUILayout.Height(30)))
            {
                navMesh.Bake();
                SceneView.RepaintAll();
            }
            
            // Кнопка Clear
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Clear", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(
                    "Clear NavMesh3D",
                    "Are you sure you want to clear the navigation mesh?",
                    "Yes", "Cancel"))
                {
                    navMesh.Clear();
                    SceneView.RepaintAll();
                }
            }
            
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        
        private void DrawNavMeshInfo()
        {
            EditorGUILayout.BeginVertical(infoBoxStyle);
            EditorGUILayout.LabelField("NavMesh Info", EditorStyles.boldLabel);
            
            EditorGUILayout.Space(5);
            
            // Размер ячейки и покрытие чанка
            float chunkWorldSize = navMesh.cellSize * 16; // ChunkSize = 16
            DrawInfoRow("Cell Size", $"{navMesh.cellSize}m");
            DrawInfoRow("Chunk Size", $"{chunkWorldSize}m × {chunkWorldSize}m × {chunkWorldSize}m");
            
            EditorGUILayout.Space(3);
            
            // Информация о чанках
            DrawInfoRow("Chunks", 
                $"{navMesh.ChunkCount.x} × {navMesh.ChunkCount.y} × {navMesh.ChunkCount.z} = {navMesh.TotalChunkCount:N0}");
            
            // Информация об узлах
            DrawInfoRow("Total Nodes", $"{navMesh.TotalNodeCount:N0}");
            DrawInfoRow("Nodes/Chunk", "4,096 (16³)");
            
            EditorGUILayout.Space(3);
            
            // Информация о регионах
            DrawInfoRow("Regions", navMesh.RegionCount.ToString());
            
            // Оценка памяти
            // Каждый чанк: 512 байт (битовая маска) + ~50 байт overhead
            long memoryBytes = navMesh.TotalChunkCount * 600L;
            DrawInfoRow("Est. Memory", FormatBytes(memoryBytes));
            
            EditorGUILayout.Space(5);
            
            // Подсказка
            EditorGUILayout.HelpBox(
                "• Green chunks = fully walkable\n" +
                "• Yellow chunks = partially blocked\n" +
                "• Red chunks = fully blocked\n" +
                "• Select object to see debug options", 
                MessageType.Info);
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawInfoRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }
        
        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:F1} {suffixes[order]}";
        }
        
        // ===== Scene View =====
        
        private void OnSceneGUI()
        {
            // Рисуем хэндлы для изменения размера volume
            if (!navMesh.IsBaked)
            {
                DrawSizeHandles();
            }
        }
        
        private void DrawSizeHandles()
        {
            // Позволяем менять размер volume перетаскиванием в Scene View
            EditorGUI.BeginChangeCheck();
            
            Vector3 center = navMesh.transform.position;
            Quaternion rotation = navMesh.transform.rotation;
            Vector3 size = navMesh.size;
            
            // Рисуем хэндлы на гранях
            Handles.color = new Color(0.5f, 0.9f, 0.9f, 0.8f);
            
            // +X
            Vector3 handlePos = center + rotation * new Vector3(size.x / 2, 0, 0);
            Vector3 newPos = Handles.Slider(handlePos, rotation * Vector3.right);
            if (newPos != handlePos)
            {
                float delta = Vector3.Dot(newPos - handlePos, rotation * Vector3.right);
                size.x = Mathf.Max(1f, size.x + delta);
            }
            
            // -X
            handlePos = center + rotation * new Vector3(-size.x / 2, 0, 0);
            newPos = Handles.Slider(handlePos, rotation * Vector3.left);
            if (newPos != handlePos)
            {
                float delta = Vector3.Dot(newPos - handlePos, rotation * Vector3.left);
                size.x = Mathf.Max(1f, size.x + delta);
            }
            
            // +Y
            handlePos = center + rotation * new Vector3(0, size.y / 2, 0);
            newPos = Handles.Slider(handlePos, rotation * Vector3.up);
            if (newPos != handlePos)
            {
                float delta = Vector3.Dot(newPos - handlePos, rotation * Vector3.up);
                size.y = Mathf.Max(1f, size.y + delta);
            }
            
            // -Y
            handlePos = center + rotation * new Vector3(0, -size.y / 2, 0);
            newPos = Handles.Slider(handlePos, rotation * Vector3.down);
            if (newPos != handlePos)
            {
                float delta = Vector3.Dot(newPos - handlePos, rotation * Vector3.down);
                size.y = Mathf.Max(1f, size.y + delta);
            }
            
            // +Z
            handlePos = center + rotation * new Vector3(0, 0, size.z / 2);
            newPos = Handles.Slider(handlePos, rotation * Vector3.forward);
            if (newPos != handlePos)
            {
                float delta = Vector3.Dot(newPos - handlePos, rotation * Vector3.forward);
                size.z = Mathf.Max(1f, size.z + delta);
            }
            
            // -Z
            handlePos = center + rotation * new Vector3(0, 0, -size.z / 2);
            newPos = Handles.Slider(handlePos, rotation * Vector3.back);
            if (newPos != handlePos)
            {
                float delta = Vector3.Dot(newPos - handlePos, rotation * Vector3.back);
                size.z = Mathf.Max(1f, size.z + delta);
            }
            
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(navMesh, "Resize NavMesh3D");
                navMesh.size = size;
                EditorUtility.SetDirty(navMesh);
            }
        }
    }
}
