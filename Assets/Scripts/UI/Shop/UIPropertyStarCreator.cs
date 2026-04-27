using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIPropertyStarCreator : MonoBehaviour
{
    [SerializeField, Tooltip("Префаб одного сегмента звезды")] 
    private GameObject SegmentPrefab;

    [SerializeField, Tooltip("Отступ от краёв звезды для текста")]
    private float TextOffset = 1f;

    private List<GameObject> SegmentList;

    private void Awake()
    {
        SegmentList = new List<GameObject>();
    }

    public void CreateStar(List<UISegmentStarInfo> list)
    {
        if (SegmentList.Count == list.Count)
        {
            for (int i = 0; i < list.Count; i++) SegmentList[i].GetComponent<UISegmentStar>().SetInfo(list[i]);
            return;
        }
        else
        {
            for (int i = 0; i < transform.childCount; i++) Destroy(transform.GetChild(i).gameObject);
            SegmentList.Clear();

            RectTransform rtBackground = transform.GetComponent<RectTransform>();
            Vector3 center = rtBackground.TransformPoint(rtBackground.rect.center);

            float RotationStep = 360f / list.Count;
            float height = Mathf.Min(rtBackground.rect.size.x, rtBackground.rect.size.y) * 0.5f;
            height -= TextOffset;
            float width = height / Mathf.Tan(Mathf.Deg2Rad * ((180 - RotationStep) * 0.5f));

            for (int i = 0; i < list.Count; i++)
            {
                GameObject obj = Instantiate(SegmentPrefab, center, Quaternion.identity, transform);
                RectTransform rt = obj.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(width, height);
                rt.Rotate(new Vector3(0, 0, RotationStep * i));
                UISegmentStar info = obj.GetComponent<UISegmentStar>();
                info.SetInfo(list[i]);
                info.TextRotate(new Vector3(0, 0, -RotationStep * i));
                SegmentList.Add(obj);
            }
        }
    }
}
