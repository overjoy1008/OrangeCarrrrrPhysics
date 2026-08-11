using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OrangeCarrrrr.Editor
{
    /// <summary>
    /// Opens the test-track simulator scene when the project is launched with no
    /// scene of its own to restore.
    ///
    /// Unity remembers the last scene setup per project, so this only fires on a
    /// genuinely fresh open — a clone, a wiped <c>Library</c>, or a first launch —
    /// where the editor would otherwise land on an empty untitled scene. Once a
    /// real scene is open, including the village one, nothing here interferes.
    /// </summary>
    [InitializeOnLoad]
    internal static class DefaultSceneOnOpen
    {
        private const string DefaultScenePath = "Assets/_Project/Scenes/flat_test.unity";

        static DefaultSceneOnOpen()
        {
            // The scene manager is not ready during static construction, so the
            // check is deferred to the first editor tick.
            EditorApplication.delayCall += OpenDefaultSceneIfNothingRestored;
        }

        private static void OpenDefaultSceneIfNothingRestored()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene active = SceneManager.GetActiveScene();

            // A restored scene has a path; an untitled one does not. Never replace
            // a scene the user is actually in, and never discard unsaved work.
            if (!string.IsNullOrEmpty(active.path)) return;
            if (active.isDirty) return;
            if (SceneManager.sceneCount > 1) return;
            if (!File.Exists(DefaultScenePath)) return;

            EditorSceneManager.OpenScene(DefaultScenePath, OpenSceneMode.Single);
        }
    }
}
