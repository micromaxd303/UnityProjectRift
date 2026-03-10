#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TransitionMatrixAsset))]
public class TransitionMatrixAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        EditorGUILayout.Space(10);
        
        if (GUILayout.Button("Generate All Combinations", GUILayout.Height(30)))
        {
            var asset = (TransitionMatrixAsset)target;
            asset.GetType()
                .GetMethod("GenerateAllCombinations", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance)
                ?.Invoke(asset, null);
            
            EditorUtility.SetDirty(asset);
        }
        
        EditorGUILayout.HelpBox(
            "1. Нажми 'Generate All Combinations'\n" +
            "2. Настрой каждый переход\n" +
            "3. Blocked = нельзя, Allowed = можно, Conditional = по условию",
            MessageType.Info
        );
    }
}
#endif