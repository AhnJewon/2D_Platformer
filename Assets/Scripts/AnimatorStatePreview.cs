using UnityEngine;

// Test helper: drives the Player Animator's "Speed" parameter so a fixed state plays (used by Assets/Scenes/RigTest.unity).
public class AnimatorStatePreview : MonoBehaviour
{
    public string stateName = "Idle";
    void Start()
    {
        var anim = GetComponentInChildren<Animator>();
        anim.SetFloat("Speed", stateName == "Run" ? 5f : stateName == "Walk" ? 2f : 0f);
        anim.Play(stateName);
    }
}
