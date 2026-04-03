using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIPropertyStarCreator : MonoBehaviour
{
    [SerializeField] private GameObject SegmentPrefab;
    private List<GameObject> SegmentList;

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
            float RotationStep = 1;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject obj = Instantiate(SegmentPrefab, transform.position, Quaternion.identity, transform);
                obj.GetComponent<RectTransform>().Rotate(new Vector3(0, 0, RotationStep * i));
                obj.GetComponent<UISegmentStar>().SetInfo(list[i]);
                SegmentList.Add(obj);
            }
        }
    }

    public class UISegmentStarInfo
    {
        public Color BackgroundColor;
        public Color IconColor;
        public Sprite Icon;
        public string Text;
    }
}
