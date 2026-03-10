using UnityEngine;

public class CaveGenerator : MonoBehaviour
{
    public MarchingCubesConfig config;
    
    private IPlatformProvider _platformProvider;
    private BackendSelector _backendSelector;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _platformProvider = new PlatformProvider();
        
        _backendSelector = new BackendSelector(_platformProvider);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            BackendRecommendation bckRec = _backendSelector.Recommend(config);
        
        
            Debug.Log(bckRec.BackendType);
            Debug.Log(bckRec.GraphicsAPI);
            Debug.Log(bckRec.Reason);
            Debug.Log("----");
        }
    }
}
