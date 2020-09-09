using UnityEngine;

public class ExcludeFog : MonoBehaviour
{

    bool doWeHaveFogInScene;

    private void Awake()
    {
        doWeHaveFogInScene = RenderSettings.fog;
    }

    private void OnPreRender()
    {
        RenderSettings.fog = false;
    }
    private void OnPostRender()
    {
        RenderSettings.fog = doWeHaveFogInScene;
    }
}