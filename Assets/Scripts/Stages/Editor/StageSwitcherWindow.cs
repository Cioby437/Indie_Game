using UnityEditor;
using UnityEngine;

// Stages > Stage Switcher Window: one row per stage in the open scene.
// Dock it next to the Inspector to switch stages with one click.
public class StageSwitcherWindow : EditorWindow
{
    public static void Open()
    {
        StageSwitcherWindow window = GetWindow<StageSwitcherWindow>("Stages");
        window.minSize = new Vector2(260f, 120f);
        window.Show();
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnGUI()
    {
        StageManager manager = EditorApplication.isPlaying && StageManager.Instance != null
            ? StageManager.Instance
            : FindFirstObjectByType<StageManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            EditorGUILayout.HelpBox("No StageManager in the open scene.\n" +
                                    "Use Stages > Setup > Build Cutscene Rig In Open Scene.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField(EditorApplication.isPlaying
            ? "Click a stage to jump to it."
            : "Click a stage to enter play mode there.", EditorStyles.miniLabel);

        EditorGUILayout.Space(4f);

        for (int i = 0; i < manager.stages.Count; i++)
        {
            GameStage stage = manager.stages[i];

            if (stage == null)
            {
                continue;
            }

            string stageName = stage.StageName;
            bool current = EditorApplication.isPlaying && manager.CurrentIndex == i;

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = current ? new Color(0.45f, 0.85f, 0.5f) : Color.white;

            if (GUILayout.Button((current ? "▶ " : "") + stageName, GUILayout.Height(24f)))
            {
                StageMenu.PlayStage(stageName);
            }

            GUI.backgroundColor = Color.white;

            bool skip = StageMenu.IsSkippedAlways(stageName);
            bool newSkip = GUILayout.Toggle(skip, "Skip Always", GUILayout.Width(90f));

            if (newSkip != skip)
            {
                StageMenu.SetSkippedAlways(stageName, newSkip);
            }

            EditorGUILayout.EndHorizontal();
        }

        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.Space(6f);

            if (GUILayout.Button("Next Stage"))
            {
                manager.Next();
            }
        }
    }
}
