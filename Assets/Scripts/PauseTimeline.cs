using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public class PauseTimeline : MonoBehaviour
{
    public PlayableDirector director;

    public void FreezeTimeline()
    {
        if (director.playableGraph.IsValid())
        {
            director.playableGraph.GetRootPlayable(0).SetSpeed(0);
        }
    }

    public void ResumeTimeline()
    {
        if (director.playableGraph.IsValid())
        {
            director.playableGraph.GetRootPlayable(0).SetSpeed(1);
        }
    }
}
