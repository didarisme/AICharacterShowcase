using System.Threading;
using UnityEditor;
using UnityEngine;

namespace DynamicNpcs.Editor
{
    [CustomEditor(typeof(RagSourceAsset))]
    public class RagSourceAssetEditor : UnityEditor.Editor
    {
        private DynamicNpcSettings _settings;
        private bool _baking;
        private float _progress;
        private string _status = "";
        private CancellationTokenSource _cts;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var source = (RagSourceAsset)target;

            EditorGUILayout.Space(8);
            _settings = (DynamicNpcSettings)EditorGUILayout.ObjectField(
                "Settings (for embedding server)", _settings, typeof(DynamicNpcSettings), false);

            bool indexExists = System.IO.File.Exists(source.IndexPath);
            EditorGUILayout.LabelField("Index status:", indexExists ? "Baked" : "Not baked");
            if (indexExists)
                EditorGUILayout.LabelField("Path:", source.IndexPath);

            using (new EditorGUI.DisabledScope(_baking || _settings == null || source.sourceFile == null))
            {
                if (GUILayout.Button(indexExists ? "Re-bake Index" : "Bake Index"))
                    _ = BakeAsync(source);
            }

            if (_baking)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), _progress, _status);
                if (GUILayout.Button("Cancel"))
                    _cts?.Cancel();
                Repaint();
            }
            else if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private async System.Threading.Tasks.Task BakeAsync(RagSourceAsset source)
        {
            _baking = true;
            _cts = new CancellationTokenSource();
            _status = "Starting...";
            try
            {
                await RagBaker.BakeAsync(source, _settings,
                    (progress, status) => { _progress = progress; _status = status; },
                    _cts.Token);
                _status = "Done. Index saved to " + source.IndexPath;
            }
            catch (System.OperationCanceledException)
            {
                _status = "Cancelled.";
            }
            catch (System.Exception e)
            {
                _status = "Error: " + e.Message;
                Debug.LogError("[DynamicNPCs] RAG bake failed: " + e);
            }
            finally
            {
                _baking = false;
                Repaint();
            }
        }
    }
}