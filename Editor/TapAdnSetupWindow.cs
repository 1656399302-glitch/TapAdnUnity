using System.IO;
using UnityEditor;
using UnityEngine;

namespace WXGameMaker.TapAdn.Editor
{
    public sealed class TapAdnSetupWindow : EditorWindow
    {
        private bool installed;
        private void OnEnable() { installed = SdkInstaller.IsReady(); }
        [MenuItem("Tools/TapADN/SDK Setup")]
        public static void Open() => GetWindow<TapAdnSetupWindow>("TapADN SDK Setup");
        private void OnGUI()
        {
            EditorGUILayout.LabelField("TapADN direct / Android / rewarded video", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Pinned SDK", SdkLock.Version);
            EditorGUILayout.LabelField("Installation", installed ? "Verified and ready" : "Not ready");
            EditorGUILayout.HelpBox(SdkInstaller.Status, MessageType.Info);
            if (GUILayout.Button("Install / Repair from verified cache")) Install(false);
            if (GUILayout.Button("Download again and repair")) Install(true);
            if (GUILayout.Button("Create local configuration")) CreateLocalConfiguration();
            EditorGUILayout.HelpBox("SDK installation never initializes advertising. Initialize explicitly after a real privacy consent event. Media keys belong in the ignored local asset.", MessageType.Info);
        }
        private async void Install(bool download) { await SdkInstaller.InstallAsync(download); installed = SdkInstaller.IsReady(); Repaint(); }
        [MenuItem("Tools/TapADN/Create Local Configuration")]
        public static void CreateLocalConfiguration()
        {
            const string directory = "Assets/TapAdnLocal/Resources";
            const string path = directory + "/TapAdnLocalConfig.asset";
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            var config = AssetDatabase.LoadAssetAtPath<TapAdnConfig>(path);
            if (config == null)
            {
                config = CreateInstance<TapAdnConfig>();
                config.Placements = new[] { new RewardedPlacement { Key = "rewarded" } };
                AssetDatabase.CreateAsset(config, path);
            }
            Selection.activeObject = config;
        }
    }

    [CustomEditor(typeof(TapAdnConfig))]
    internal sealed class TapAdnConfigEditor : UnityEditor.Editor
    {
        private bool reveal;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();
            var key = serializedObject.FindProperty("mediaKey");
            reveal = EditorGUILayout.Toggle("Reveal local media key", reveal);
            key.stringValue = reveal ? EditorGUILayout.TextField("Media Key", key.stringValue) : EditorGUILayout.PasswordField("Media Key", key.stringValue);
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Do not commit this asset or embed server secrets. Client media parameters are included in the built game.", MessageType.Info);
        }
    }
}
