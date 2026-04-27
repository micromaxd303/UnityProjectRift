using UnityEngine;

public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem instance;

    public TooltipPrefab prefab;

    public float timer = 3f;

    private TooltipPrefab prefabObject;
    private bool isActive = false;

    private void Awake()
    {
        instance = this;
        GameObject TooltipObject = Instantiate(prefab.gameObject, transform);
        prefabObject = TooltipObject.GetComponent<TooltipPrefab>();
        prefabObject.SetActive(false);
    }

    private void Update()
    {
        if (isActive)
        {
            prefabObject.transform.position = Input.mousePosition;
        }
    }

    public void Show(TooltipUIData data)
    {
        isActive = true;
        prefabObject.transform.position = Input.mousePosition;
        prefabObject.SetActive(true);
        prefabObject.SetInfo(data);
    }

    public void Hide()
    {
        isActive = false;
        prefabObject.SetActive(false);
    }
}
