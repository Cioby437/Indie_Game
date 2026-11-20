using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Plays one AnimationClip at a time on an Animator (no Animator Controller needed) and holds its last frame.
// Used for the cassette and the cassette player's lid.
public sealed class ClipPlayer
{
    private readonly Animator animator;
    private PlayableGraph graph;
    private AnimationClipPlayable playable;

    public ClipPlayer(Animator animator)
    {
        this.animator = animator;

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    // Shows one frame of the clip, without playing.
    public void PoseAt(AnimationClip clip, float time)
    {
        if (!Create(clip))
        {
            return;
        }

        playable.SetTime(time);
        playable.Pause();
        graph.Play();
        graph.Evaluate();
    }

    // Plays the clip once, then holds its last frame.
    public IEnumerator Play(AnimationClip clip)
    {
        if (!Create(clip))
        {
            yield break;
        }

        playable.SetTime(0);
        graph.Play();

        while (playable.IsValid() && playable.GetTime() < clip.length)
        {
            yield return null;
        }

        if (playable.IsValid())
        {
            playable.SetTime(clip.length);
            playable.Pause();
        }
    }

    public void Dispose()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }

    private bool Create(AnimationClip clip)
    {
        if (animator == null || clip == null)
        {
            return false;
        }

        Dispose();

        graph = PlayableGraph.Create(animator.name + " Clip");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        output.SetSourcePlayable(playable);

        return true;
    }
}
