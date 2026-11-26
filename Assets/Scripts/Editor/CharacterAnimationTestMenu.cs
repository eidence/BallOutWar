using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BOW.Editor
{
    /// <summary>Menu item that opens a throwaway scene and plays the character animation test in it.</summary>
    public static class CharacterAnimationTestMenu
    {
        [MenuItem("Ball Out War/Character Animation Test")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var type = Type.GetType("BOW.Game.CharacterAnimationTest, BOW.Game");
            if (type == null)
            {
                Debug.LogError("CharacterAnimationTest not found; check the BOW.Game assembly compiles.");
                return;
            }
            new GameObject("Character Animation Test", type);
            EditorApplication.isPlaying = true;
        }
    }
}
